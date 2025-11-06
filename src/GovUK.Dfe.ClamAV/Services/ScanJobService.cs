using System.Collections.Concurrent;
using GovUK.Dfe.ClamAV.Models;

namespace GovUK.Dfe.ClamAV.Services;

public interface IScanJobService
{
    string CreateJob(string fileName, long fileSize);
    ScanJob? GetJob(string jobId);
    void UpdateJobStatus(string jobId, string status, string? malware = null, string? error = null);
    void CompleteJob(string jobId);
    void CleanupOldJobs(TimeSpan maxAge);
    IEnumerable<ScanJob> GetAllJobs();
}

public class ScanJobService(ILogger<ScanJobService> logger) : IScanJobService
{
    private readonly ConcurrentDictionary<string, ScanJob> _jobs = new();

    public string CreateJob(string fileName, long fileSize)
    {
        var jobId = Guid.NewGuid().ToString();
        var job = new ScanJob
        {
            JobId = jobId,
            FileName = fileName,
            FileSize = fileSize,
            Status = "queued",
            CreatedAt = DateTime.UtcNow
        };

        _jobs[jobId] = job;
        logger.LogInformation("Created scan job {JobId} for file {FileName} ({FileSize} bytes)", 
            jobId, fileName, fileSize);
        
        return jobId;
    }

    public ScanJob? GetJob(string jobId)
    {
        _jobs.TryGetValue(jobId, out var job);
        return job;
    }

    public void UpdateJobStatus(string jobId, string status, string? malware = null, string? error = null)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.Status = status;
            if (malware != null) job.Malware = malware;
            if (error != null) job.Error = error;
            
            logger.LogInformation("Updated scan job {JobId} to status {Status}", jobId, status);
        }
    }

    public void CompleteJob(string jobId)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.CompletedAt = DateTime.UtcNow;
            logger.LogInformation("Completed scan job {JobId} in {Duration}ms", 
                jobId, job.ScanDuration?.TotalMilliseconds ?? 0);
        }
    }

    public void CleanupOldJobs(TimeSpan maxAge)
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
    }

    public IEnumerable<ScanJob> GetAllJobs()
    {
        return _jobs.Values.OrderByDescending(j => j.CreatedAt);
    }
}

