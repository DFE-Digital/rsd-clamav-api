using System.Collections.Concurrent;
using GovUK.Dfe.ClamAV.Models;

namespace GovUK.Dfe.ClamAV.Services;

public interface IScanJobService
{
    Task<string> CreateJobAsync(string fileName, long fileSize, string status = "queued");
    Task<ScanJob?> GetJobAsync(string jobId);
    Task UpdateJobStatusAsync(string jobId, string status, string? malware = null, string? error = null);
    Task UpdateFileSizeAsync(string jobId, long fileSize);
    Task CompleteJobAsync(string jobId);
    Task CleanupOldJobsAsync(TimeSpan maxAge);
    Task<IReadOnlyList<ScanJob>> GetRecentJobsAsync(int count);
}

/// <summary>
/// Keeps scan jobs in this process only. A status request that reaches another replica gets 404, so use
/// <see cref="RedisScanJobService"/> (set <c>ConnectionStrings:Redis</c>) whenever more than one replica can run.
/// </summary>
public class ScanJobService(ILogger<ScanJobService> logger) : IScanJobService
{
    private readonly ConcurrentDictionary<string, ScanJob> _jobs = new();

    public Task<string> CreateJobAsync(string fileName, long fileSize, string status = "queued")
    {
        var jobId = Guid.NewGuid().ToString();
        var job = new ScanJob
        {
            JobId = jobId,
            FileName = fileName,
            FileSize = fileSize,
            Status = status,
            CreatedAt = DateTime.UtcNow
        };

        _jobs[jobId] = job;
        logger.LogInformation("Created scan job {JobId} for file {FileName} ({FileSize} bytes)", 
            jobId, fileName, fileSize);
        
        return Task.FromResult(jobId);
    }

    public Task<ScanJob?> GetJobAsync(string jobId)
    {
        _jobs.TryGetValue(jobId, out var job);
        return Task.FromResult(job);
    }

    public Task UpdateJobStatusAsync(string jobId, string status, string? malware = null, string? error = null)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.Status = status;
            if (malware != null) job.Malware = malware;
            if (error != null) job.Error = error;
            
            logger.LogInformation("Updated scan job {JobId} to status {Status}", jobId, status);
        }

        return Task.CompletedTask;
    }

    public Task UpdateFileSizeAsync(string jobId, long fileSize)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.FileSize = fileSize;
        }

        return Task.CompletedTask;
    }

    public Task CompleteJobAsync(string jobId)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.CompletedAt = DateTime.UtcNow;
            logger.LogInformation("Completed scan job {JobId} in {Duration}ms", 
                jobId, job.ScanDuration?.TotalMilliseconds ?? 0);
        }

        return Task.CompletedTask;
    }

    public Task CleanupOldJobsAsync(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        var oldJobs = _jobs.Where(kvp => kvp.Value.CreatedAt < cutoff).Select(kvp => kvp.Key).ToList();
        
        foreach (var jobId in oldJobs)
        {
            if (_jobs.TryRemove(jobId, out _))
            {
                logger.LogDebug("Cleaned up old scan job {JobId}", jobId);
            }
        }
        
        if (oldJobs.Count > 0)
        {
            logger.LogInformation("Cleaned up {Count} old scan jobs", oldJobs.Count);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ScanJob>> GetRecentJobsAsync(int count)
    {
        IReadOnlyList<ScanJob> jobs = _jobs.Values.OrderByDescending(j => j.CreatedAt).Take(count).ToList();
        return Task.FromResult(jobs);
    }
}
