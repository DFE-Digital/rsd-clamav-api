namespace GovUK.Dfe.ClamAV.Models;

public class JobsListResponse
{
    public IEnumerable<JobSummary> Jobs { get; set; } = new List<JobSummary>();
    public int Count { get; set; }
}

public class JobSummary
{
    public string JobId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public long? FileSize { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public double? ScanDurationMs { get; set; }
}

