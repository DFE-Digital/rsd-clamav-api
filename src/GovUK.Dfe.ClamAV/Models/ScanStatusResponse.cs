namespace GovUK.Dfe.ClamAV.Models;

public class ScanStatusResponse
{
    public string JobId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public long? FileSize { get; set; }
    public string? Engine { get; set; }
    public string? Malware { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public double? ScanDurationMs { get; set; }
}

