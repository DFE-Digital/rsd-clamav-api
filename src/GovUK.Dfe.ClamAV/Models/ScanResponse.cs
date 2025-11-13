namespace GovUK.Dfe.ClamAV.Models;

public class ScanResponse
{
    public string Status { get; set; } = string.Empty;
    public string Engine { get; set; } = "clamav";
    public string? Malware { get; set; }
    public string FileName { get; set; } = string.Empty;
    public long Size { get; set; }
    public double ScanDurationMs { get; set; }
}

