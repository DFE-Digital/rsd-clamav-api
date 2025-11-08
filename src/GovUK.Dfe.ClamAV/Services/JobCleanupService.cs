using GovUK.Dfe.CoreLibs.AsyncProcessing.Interfaces;

namespace GovUK.Dfe.ClamAV.Services;

public class JobCleanupService(
    IBackgroundServiceFactory backgroundService,
    IScanJobService jobService,
    ILogger<JobCleanupService> logger)
    : IHostedService, IDisposable
{
    private Timer? _timer;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Job Cleanup Service is starting");
        
        // Run cleanup every 10 minutes
        _timer = new Timer(
            callback: _ => ScheduleCleanup(),
            state: null,
            dueTime: TimeSpan.FromMinutes(1), // First run after 1 minute
            period: TimeSpan.FromMinutes(10)  // Then every 10 minutes
        );

        return Task.CompletedTask;
    }

    private void ScheduleCleanup()
    {
        _ = backgroundService.EnqueueTask((ct) =>
        {
            try
            {
                logger.LogInformation("Starting scheduled job cleanup");
                jobService.CleanupOldJobs(TimeSpan.FromHours(24)); // Keep jobs for 24 hours
                logger.LogInformation("Scheduled job cleanup completed");
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during scheduled job cleanup");
                return Task.FromResult(false);
            }
        });
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Job Cleanup Service is stopping");
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}

