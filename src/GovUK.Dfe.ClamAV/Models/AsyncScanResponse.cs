namespace GovUK.Dfe.ClamAV.Models;

public class AsyncScanResponse
{
    public string JobId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public string Message { get; set; } = string.Empty;
    public string StatusUrl { get; set; } = string.Empty;
    public string? SourceUrl { get; set; }
}

