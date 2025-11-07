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
}

