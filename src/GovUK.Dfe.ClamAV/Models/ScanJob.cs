namespace GovUK.Dfe.ClamAV.Models;

public class ScanJob
{
    public string JobId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string Status { get; set; } = "queued"; // queued, downloading, scanning, clean, infected, error
    public string? Malware { get; set; }
    public string? Error { get; set; }
    public string Engine { get; set; } = "clamav";
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public TimeSpan? ScanDuration => CompletedAt.HasValue ? CompletedAt.Value - CreatedAt : null;
}

