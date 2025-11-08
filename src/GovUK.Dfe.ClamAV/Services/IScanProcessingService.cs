namespace GovUK.Dfe.ClamAV.Services;

public interface IScanProcessingService
{
    Task<bool> ProcessFileScanAsync(string jobId, string tempFilePath, CancellationToken cancellationToken);
    Task<bool> ProcessUrlScanAsync(string jobId, string url, string tempFilePath, long maxFileSize, CancellationToken cancellationToken);
}