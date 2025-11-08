using GovUK.Dfe.ClamAV.Models;
using nClam;

namespace GovUK.Dfe.ClamAV.Services;

public class ScanProcessingService(
    IScanJobService jobService,
    IConfiguration configuration,
    ILogger<ScanProcessingService> logger)
    : IScanProcessingService
{
    public async Task<bool> ProcessFileScanAsync(string jobId, string tempFilePath, CancellationToken cancellationToken)
    {
        try
        {
            jobService.UpdateJobStatus(jobId, "scanning");
            logger.LogInformation("Started scanning job {JobId}", jobId);

            var scanResult = await ScanFileAsync(tempFilePath, cancellationToken);

            if (scanResult.IsSuccess)
            {
                jobService.UpdateJobStatus(jobId, scanResult.IsClean ? "clean" : "infected", 
                    malware: scanResult.MalwareName);
                logger.LogInformation("Job {JobId} scan complete: {Status}", jobId, 
                    scanResult.IsClean ? "Clean" : $"Infected with {scanResult.MalwareName}");
            }
            else
            {
                jobService.UpdateJobStatus(jobId, "error", error: scanResult.Error);
                logger.LogError("Job {JobId} scan error: {Error}", jobId, scanResult.Error);
            }

            jobService.CompleteJob(jobId);
            return scanResult.IsSuccess;
        }
        catch (Exception ex)
        {
            jobService.UpdateJobStatus(jobId, "error", error: ex.Message);
            logger.LogError(ex, "Error processing scan job {JobId}", jobId);
            jobService.CompleteJob(jobId);
            return false;
        }
        finally
        {
            // Clean up temp file
            CleanupTempFile(tempFilePath, jobId);
        }
    }

    public async Task<bool> ProcessUrlScanAsync(string jobId, string url, string tempFilePath, long maxFileSize, CancellationToken cancellationToken)
    {
        try
        {
            // Download phase
            jobService.UpdateJobStatus(jobId, "downloading");
            logger.LogInformation("Started downloading file from {Url} for job {JobId}", url, jobId);

            var downloadSuccess = await DownloadFileAsync(jobId, url, tempFilePath, maxFileSize, cancellationToken);
            if (!downloadSuccess)
            {
                return false; // Error already logged and job status updated
            }

            logger.LogInformation("Download complete for job {JobId}, starting scan", jobId);

            // Scan phase
            return await ProcessFileScanAsync(jobId, tempFilePath, cancellationToken);
        }
        catch (Exception ex)
        {
            jobService.UpdateJobStatus(jobId, "error", error: ex.Message);
            logger.LogError(ex, "Error processing URL scan job {JobId}", jobId);
            jobService.CompleteJob(jobId);
            CleanupTempFile(tempFilePath, jobId);
            return false;
        }
    }

    private async Task<bool> DownloadFileAsync(string jobId, string url, string tempFilePath, long maxFileSize, CancellationToken cancellationToken)
    {
        var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromMinutes(10);

        try
        {
            // First, make a HEAD request to check size
            using var headRequest = new HttpRequestMessage(HttpMethod.Head, url);
            using var headResponse = await httpClient.SendAsync(headRequest, cancellationToken);

            var contentLength = headResponse.Content.Headers.ContentLength;

            // If Content-Length is present, check it before downloading
            if (contentLength.HasValue)
            {
                logger.LogInformation("File at {Url} has Content-Length: {Size} bytes", url, contentLength.Value);

                if (contentLength.Value > maxFileSize)
                {
                    jobService.UpdateJobStatus(jobId, "error",
                        error: $"File size ({contentLength.Value:N0} bytes) exceeds maximum allowed size ({maxFileSize:N0} bytes)");
                    jobService.CompleteJob(jobId);
                    logger.LogWarning("Job {JobId} cancelled: File too large ({Size} bytes)", jobId, contentLength.Value);
                    return false;
                }

                // Update job with actual file size
                var job = jobService.GetJob(jobId);
                if (job != null)
                {
                    job.FileSize = contentLength.Value;
                }
            }
            else
            {
                logger.LogWarning("No Content-Length header for {Url}, will monitor size during download", url);
            }

            // Download the file with size monitoring
            using var getRequest = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await httpClient.SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, useAsync: true);
            await using var downloadStream = await response.Content.ReadAsStreamAsync(cancellationToken);

            var buffer = new byte[8192];
            long totalBytesRead = 0;
            int bytesRead;

            while ((bytesRead = await downloadStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                totalBytesRead += bytesRead;

                // Check if we've exceeded the size limit
                if (totalBytesRead > maxFileSize)
                {
                    // Close and delete the partial file
                    await fileStream.DisposeAsync();

                    try
                    {
                        if (File.Exists(tempFilePath))
                            File.Delete(tempFilePath);
                    }
                    catch (Exception deleteEx)
                    {
                        logger.LogWarning(deleteEx, "Failed to delete temp file {Path}", tempFilePath);
                    }

                    jobService.UpdateJobStatus(jobId, "error",
                        error: $"File size exceeds maximum allowed size ({maxFileSize:N0} bytes). Download cancelled.");
                    jobService.CompleteJob(jobId);
                    logger.LogWarning("Job {JobId} cancelled: File exceeded size limit during download", jobId);
                    return false;
                }

                await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
            }

            logger.LogInformation("Downloaded {Bytes} bytes from {Url} to {Path}", totalBytesRead, url, tempFilePath);

            // Update job with actual file size if we didn't have Content-Length
            if (!contentLength.HasValue)
            {
                var job = jobService.GetJob(jobId);
                if (job != null)
                {
                    job.FileSize = totalBytesRead;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error downloading file from {Url} for job {JobId}", url, jobId);
            jobService.UpdateJobStatus(jobId, "error", error: $"Failed to download file: {ex.Message}");
            jobService.CompleteJob(jobId);

            // Clean up temp file on error
            CleanupTempFile(tempFilePath, jobId);

            return false;
        }
    }

    private async Task<ScanResult> ScanFileAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            var host = configuration["CLAMD_HOST"] ?? Environment.GetEnvironmentVariable("CLAMD_HOST") ?? "127.0.0.1";
            var port = int.TryParse(configuration["CLAMD_PORT"] ?? Environment.GetEnvironmentVariable("CLAMD_PORT"), out var p) ? p : 3310;

            var clam = new ClamClient(host, port);

            await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            clam.MaxStreamSize = fileStream.Length;

            var result = await clam.SendAndScanFileAsync(fileStream, cancellationToken);

            return result.Result switch
            {
                ClamScanResults.Clean => new ScanResult { IsSuccess = true, IsClean = true },
                ClamScanResults.VirusDetected => new ScanResult
                {
                    IsSuccess = true,
                    IsClean = false,
                    MalwareName = result.InfectedFiles?.FirstOrDefault()?.VirusName ?? "unknown"
                },
                _ => new ScanResult { IsSuccess = false, Error = $"Unexpected result: {result.RawResult}" }
            };
        }
        catch (Exception ex)
        {
            return new ScanResult { IsSuccess = false, Error = ex.Message };
        }
    }

    private void CleanupTempFile(string tempFilePath, string jobId)
    {
        try
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
                logger.LogDebug("Deleted temp file for job {JobId}", jobId);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete temp file {Path}", tempFilePath);
        }
    }

    private class ScanResult
    {
        public bool IsSuccess { get; set; }
        public bool IsClean { get; set; }
        public string? MalwareName { get; set; }
        public string? Error { get; set; }
    }
}

