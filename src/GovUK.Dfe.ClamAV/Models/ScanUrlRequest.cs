namespace GovUK.Dfe.ClamAV.Models;

public class ScanUrlRequest
{
    public string Url { get; set; } = string.Empty;
    public bool IsBase64 { get; set; } = false;
}

