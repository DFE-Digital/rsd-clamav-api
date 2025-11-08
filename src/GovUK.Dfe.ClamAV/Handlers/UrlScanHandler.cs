using GovUK.Dfe.ClamAV.Models;
using GovUK.Dfe.ClamAV.Services;
using GovUK.Dfe.CoreLibs.AsyncProcessing.Interfaces;

namespace GovUK.Dfe.ClamAV.Handlers;

public class UrlScanHandler
{
    private readonly IScanJobService _jobService;
    private readonly IScanProcessingService _scanProcessing;
    private readonly IBackgroundServiceFactory _backgroundService;
    private readonly int _maxFileSizeMb;

    public UrlScanHandler(
        IScanJobService jobService,
        IScanProcessingService scanProcessing,
        IBackgroundServiceFactory backgroundService,
        IConfiguration configuration)
    {
        _jobService = jobService;
        _scanProcessing = scanProcessing;
        _backgroundService = backgroundService;
        _maxFileSizeMb = int.TryParse(
            configuration["MAX_FILE_SIZE_MB"] ?? Environment.GetEnvironmentVariable("MAX_FILE_SIZE_MB"),
            out var m) ? m : 200;
    }

    public async Task<IResult> HandleAsync(ScanUrlRequest urlRequest)
    {
        if (string.IsNullOrWhiteSpace(urlRequest.Url))
            return Results.BadRequest(new { error = "URL is required" });

        var fileUrl = urlRequest.Url;

        // Decode from Base64 if needed
        if (urlRequest.IsBase64)
        {
            try
            {
                var bytes = Convert.FromBase64String(fileUrl);
                fileUrl = System.Text.Encoding.UTF8.GetString(bytes);
            }
            catch (FormatException)
            {
                return Results.BadRequest(new { error = "Invalid Base64 encoded URL" });
            }
        }

        if (!Uri.TryCreate(fileUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return Results.BadRequest(new { error = "Invalid URL. Must be a valid HTTP or HTTPS URL." });
        }

        // Extract original filename from URL
        var fileName = Path.GetFileName(uri.LocalPath);
        if (string.IsNullOrWhiteSpace(fileName) || fileName == "/" || fileName == "\\")
        {
            // No filename in URL, use domain name
            fileName = $"{uri.Host.Replace(".", "_")}.bin";
        }

        // Create job immediately with "downloading" status
        var jobId = _jobService.CreateJob(fileName, 0);
        _jobService.UpdateJobStatus(jobId, "downloading");

        var tempPath = Path.Combine(Path.GetTempPath(), $"clamav_{jobId}_{Guid.NewGuid():N}_{fileName}");
        var maxFileSizeBytes = (long)_maxFileSizeMb * 1024 * 1024;

        // Enqueue for background processing
        _ = _backgroundService.EnqueueTask(async (ct) =>
        {
            return await _scanProcessing.ProcessUrlScanAsync(jobId, fileUrl, tempPath, maxFileSizeBytes, ct);
        });

        return Results.Accepted($"/scan/async/{jobId}", new
        {
            jobId,
            status = "downloading",
            fileName,
            message = "Download started. Use the jobId to check status.",
            statusUrl = $"/scan/async/{jobId}",
            sourceUrl = fileUrl
        });
    }
}

