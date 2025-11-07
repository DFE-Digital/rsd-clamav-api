using nClam;
using System.Threading.Channels;

namespace GovUK.Dfe.ClamAV.Services;

public class BackgroundScanService(
    Channel<ScanRequest> channel,
    IScanJobService jobService,
    ILogger<BackgroundScanService> logger,
    IConfiguration configuration)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Background scan service started");

        // Start cleanup task
        _ = Task.Run(async () => await CleanupJobsPeriodically(stoppingToken), stoppingToken);

        // Process scan requests
        await foreach (var request in channel.Reader.ReadAllAsync(stoppingToken))
        {
            _ = Task.Run(async () => await ProcessScanRequest(request), stoppingToken);
        }
    }

    private async Task ProcessScanRequest(ScanRequest request)
    {
        try
        {
            // If this is a URL download request, download first
            if (!string.IsNullOrEmpty(request.SourceUrl))
            {
                jobService.UpdateJobStatus(request.JobId, "downloading");
                logger.LogInformation("Started downloading file from {Url} for job {JobId}", request.SourceUrl, request.JobId);

                var downloadSuccess = await DownloadFileAsync(request);
                if (!downloadSuccess)
                {
                    return; // Error already logged and job status updated
                }

                logger.LogInformation("Download complete for job {JobId}, starting scan", request.JobId);
            }

            jobService.UpdateJobStatus(request.JobId, "scanning");
            logger.LogInformation("Started scanning job {JobId}", request.JobId);

            var host = configuration["CLAMD_HOST"] ?? Environment.GetEnvironmentVariable("CLAMD_HOST") ?? "127.0.0.1";
            var port = int.TryParse(configuration["CLAMD_PORT"] ?? Environment.GetEnvironmentVariable("CLAMD_PORT"), out var p) ? p : 3310;

            var clam = new ClamClient(host, port);

            // Read from temp file instead of memory
            await using var fileStream = new FileStream(request.TempFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            clam.MaxStreamSize = fileStream.Length;

            var result = await clam.SendAndScanFileAsync(fileStream);

            switch (result.Result)
            {
                case ClamScanResults.Clean:
                    jobService.UpdateJobStatus(request.JobId, "clean");
                    logger.LogInformation("Job {JobId} scan complete: Clean", request.JobId);
                    break;

                case ClamScanResults.VirusDetected:
                    var virusName = result.InfectedFiles.FirstOrDefault()?.VirusName ?? "unknown";
                    jobService.UpdateJobStatus(request.JobId, "infected", malware: virusName);
                    logger.LogWarning("Job {JobId} scan complete: Infected with {Virus}", request.JobId, virusName);
                    break;

                default:
                    jobService.UpdateJobStatus(request.JobId, "error", error: $"Unexpected result: {result.RawResult}");
                    logger.LogError("Job {JobId} scan error: {Result}", request.JobId, result.RawResult);
                    break;
            }

            jobService.CompleteJob(request.JobId);
        }
        catch (Exception ex)
        {
            jobService.UpdateJobStatus(request.JobId, "error", error: ex.Message);
            logger.LogError(ex, "Error processing scan job {JobId}", request.JobId);
            jobService.CompleteJob(request.JobId);
        }
        finally
        {
            // Clean up temp file
            try
            {
                if (File.Exists(request.TempFilePath))
                {
                    File.Delete(request.TempFilePath);
                    logger.LogDebug("Deleted temp file for job {JobId}", request.JobId);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete temp file {Path}", request.TempFilePath);
            }
        }
    }

    private async Task<bool> DownloadFileAsync(ScanRequest request)
    {
        var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromMinutes(10);

        try
        {
            // First, make a HEAD request to check size
            using var headRequest = new HttpRequestMessage(HttpMethod.Head, request.SourceUrl);
            using var headResponse = await httpClient.SendAsync(headRequest);

            var contentLength = headResponse.Content.Headers.ContentLength;

            // If Content-Length is present, check it before downloading
            if (contentLength.HasValue)
            {
                logger.LogInformation("File at {Url} has Content-Length: {Size} bytes", request.SourceUrl, contentLength.Value);

                if (contentLength.Value > request.MaxFileSize)
                {
                    jobService.UpdateJobStatus(request.JobId, "error", 
                        error: $"File size ({contentLength.Value:N0} bytes) exceeds maximum allowed size ({request.MaxFileSize:N0} bytes)");
                    jobService.CompleteJob(request.JobId);
                    logger.LogWarning("Job {JobId} cancelled: File too large ({Size} bytes)", request.JobId, contentLength.Value);
                    return false;
                }

                // Update job with actual file size
                var job = jobService.GetJob(request.JobId);
                if (job != null)
                {
                    job.FileSize = contentLength.Value;
                }
            }
            else
            {
                logger.LogWarning("No Content-Length header for {Url}, will monitor size during download", request.SourceUrl);
            }

            // Download the file with size monitoring
            using var getRequest = new HttpRequestMessage(HttpMethod.Get, request.SourceUrl);
            using var response = await httpClient.SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using var fileStream = new FileStream(request.TempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, useAsync: true);
            await using var downloadStream = await response.Content.ReadAsStreamAsync();

            var buffer = new byte[8192];
            long totalBytesRead = 0;
            int bytesRead;

            while ((bytesRead = await downloadStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                totalBytesRead += bytesRead;

                // Check if we've exceeded the size limit
                if (totalBytesRead > request.MaxFileSize)
                {
                    // Close and delete the partial file
                    await fileStream.DisposeAsync();

                    try
                    {
                        if (File.Exists(request.TempFilePath))
                            File.Delete(request.TempFilePath);
                    }
                    catch (Exception deleteEx)
                    {
                        logger.LogWarning(deleteEx, "Failed to delete temp file {Path}", request.TempFilePath);
                    }

                    jobService.UpdateJobStatus(request.JobId, "error", 
                        error: $"File size exceeds maximum allowed size ({request.MaxFileSize:N0} bytes). Download cancelled.");
                    jobService.CompleteJob(request.JobId);
                    logger.LogWarning("Job {JobId} cancelled: File exceeded size limit during download", request.JobId);
                    return false;
                }

                await fileStream.WriteAsync(buffer, 0, bytesRead);
            }

            logger.LogInformation("Downloaded {Bytes} bytes from {Url} to {Path}", totalBytesRead, request.SourceUrl, request.TempFilePath);

            // Update job with actual file size if we didn't have Content-Length
            if (!contentLength.HasValue)
            {
                var job = jobService.GetJob(request.JobId);
                if (job != null)
                {
                    job.FileSize = totalBytesRead;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error downloading file from {Url} for job {JobId}", request.SourceUrl, request.JobId);
            jobService.UpdateJobStatus(request.JobId, "error", error: $"Failed to download file: {ex.Message}");
            jobService.CompleteJob(request.JobId);

            // Clean up temp file on error
            try
            {
                if (File.Exists(request.TempFilePath))
                    File.Delete(request.TempFilePath);
            }
            catch { }

            return false;
        }
    }

    private async Task CleanupJobsPeriodically(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
                jobService.CleanupOldJobs(TimeSpan.FromHours(24)); // Keep jobs for 24 hours
            }
            catch (OperationCanceledException)
            {
                // Expected when stopping
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during job cleanup");
            }
        }
    }
}

public class ScanRequest
{
    public string JobId { get; set; } = string.Empty;
    public string TempFilePath { get; set; } = string.Empty;
    public string? SourceUrl { get; set; } // If set, download from URL first
    public long MaxFileSize { get; set; } // For URL downloads
}
