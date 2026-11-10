using System.Text.Json;
using GovUK.Dfe.ClamAV.Models;
using StackExchange.Redis;

namespace GovUK.Dfe.ClamAV.Services;

/// <summary>
/// Stores scan jobs in Redis so any replica can answer a status request, and jobs survive a restart.
/// Each job is a JSON string with a 24-hour expiry; a sorted set (score = creation time) lists recent jobs.
/// Only the replica that processes a job writes to it after creation, so read-modify-write is safe here.
/// </summary>
public sealed class RedisScanJobService(
    IConnectionMultiplexer redis,
    IConfiguration configuration,
    ILogger<RedisScanJobService> logger) : IScanJobService
{
    public static readonly TimeSpan JobRetention = TimeSpan.FromHours(24);

    private readonly string _prefix = configuration["ScanJobs:RedisKeyPrefix"] is { Length: > 0 } prefix
        ? prefix
        : "clamav:jobs:";

    private IDatabase Database => redis.GetDatabase();

    private RedisKey IndexKey => $"{_prefix}index";

    public async Task<string> CreateJobAsync(string fileName, long fileSize, string status = "queued")
    {
        var job = new ScanJob
        {
            JobId = Guid.NewGuid().ToString(),
            FileName = fileName,
            FileSize = fileSize,
            Status = status,
            CreatedAt = DateTime.UtcNow
        };

        await Database.StringSetAsync(JobKey(job.JobId), Serialize(job), JobRetention);
        await Database.SortedSetAddAsync(IndexKey, job.JobId, ToScore(job.CreatedAt));

        logger.LogInformation("Created scan job {JobId} for file {FileName} ({FileSize} bytes)",
            job.JobId, fileName, fileSize);

        return job.JobId;
    }

    public async Task<ScanJob?> GetJobAsync(string jobId)
    {
        var value = await Database.StringGetAsync(JobKey(jobId));
        return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<ScanJob>(value.ToString());
    }

    public Task UpdateJobStatusAsync(string jobId, string status, string? malware = null, string? error = null) =>
        UpdateAsync(jobId, job =>
        {
            job.Status = status;
            if (malware != null) job.Malware = malware;
            if (error != null) job.Error = error;
            logger.LogInformation("Updated scan job {JobId} to status {Status}", jobId, status);
        });

    public Task UpdateFileSizeAsync(string jobId, long fileSize) =>
        UpdateAsync(jobId, job => job.FileSize = fileSize);

    public Task CompleteJobAsync(string jobId) =>
        UpdateAsync(jobId, job =>
        {
            job.CompletedAt = DateTime.UtcNow;
            logger.LogInformation("Completed scan job {JobId} in {Duration}ms",
                jobId, job.ScanDuration?.TotalMilliseconds ?? 0);
        });

    public async Task CleanupOldJobsAsync(TimeSpan maxAge)
    {
        // Job keys expire on their own; this only trims the index.
        var removed = await Database.SortedSetRemoveRangeByScoreAsync(
            IndexKey, double.NegativeInfinity, ToScore(DateTime.UtcNow - maxAge));

        if (removed > 0)
        {
            logger.LogInformation("Cleaned up {Count} old scan jobs", removed);
        }
    }

    public async Task<IReadOnlyList<ScanJob>> GetRecentJobsAsync(int count)
    {
        var jobIds = await Database.SortedSetRangeByRankAsync(IndexKey, 0, count - 1, Order.Descending);
        if (jobIds.Length == 0)
        {
            return [];
        }

        var values = await Database.StringGetAsync(jobIds.Select(id => JobKey(id.ToString())).ToArray());
        return values
            .Where(value => !value.IsNullOrEmpty)
            .Select(value => JsonSerializer.Deserialize<ScanJob>(value.ToString())!)
            .ToList();
    }

    private async Task UpdateAsync(string jobId, Action<ScanJob> update)
    {
        var job = await GetJobAsync(jobId);
        if (job == null)
        {
            return;
        }

        update(job);
        await Database.StringSetAsync(JobKey(jobId), Serialize(job), expiry: null, keepTtl: true);
    }

    private RedisKey JobKey(string jobId) => $"{_prefix}{jobId}";

    private static string Serialize(ScanJob job) => JsonSerializer.Serialize(job);

    private static double ToScore(DateTime utc) => new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeMilliseconds();
}
