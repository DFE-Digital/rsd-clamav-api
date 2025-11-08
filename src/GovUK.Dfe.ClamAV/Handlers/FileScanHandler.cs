using GovUK.Dfe.ClamAV.Services;
using GovUK.Dfe.CoreLibs.AsyncProcessing.Interfaces;
using nClam;
using System.Net;

namespace GovUK.Dfe.ClamAV.Handlers;

public class FileScanHandler(
    IScanJobService jobService,
    IScanProcessingService scanProcessing,
    IBackgroundServiceFactory backgroundService,
    IConfiguration configuration)
{
    public async Task<IResult> HandleSyncAsync(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return Results.BadRequest(new { error = "Missing or empty file" });

        var host = configuration["CLAMD_HOST"] ?? Environment.GetEnvironmentVariable("CLAMD_HOST") ?? "127.0.0.1";
        var port = int.TryParse(configuration["CLAMD_PORT"] ?? Environment.GetEnvironmentVariable("CLAMD_PORT"), out var p) ? p : 3310;

        var clam = new ClamClient(host, port) { MaxStreamSize = file.Length };

        await using var stream = file.OpenReadStream();
        var startTime = DateTime.UtcNow;
        var result = await clam.SendAndScanFileAsync(stream);
        var scanDuration = DateTime.UtcNow - startTime;

        return result.Result switch
        {
            ClamScanResults.Clean => Results.Ok(new
            {
                status = "clean",
                engine = "clamav",
                fileName = file.FileName,
                size = file.Length,
                scanDurationMs = scanDuration.TotalMilliseconds
            }),
            ClamScanResults.VirusDetected => Results.Ok(new
            {
                status = "infected",
                engine = "clamav",
                malware = result.InfectedFiles.FirstOrDefault()?.VirusName ?? "unknown",
                fileName = file.FileName,
                size = file.Length,
                scanDurationMs = scanDuration.TotalMilliseconds
            }),
            _ => Results.Problem(new
            {
                status = "error",
                engine = "clamav",
                raw = result.RawResult
            }.ToString(), statusCode: (int)HttpStatusCode.InternalServerError)
        };
    }

    public async Task<IResult> HandleAsyncAsync(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return Results.BadRequest(new { error = "Missing or empty file" });

        // Create job first
        var jobId = jobService.CreateJob(file.FileName, file.Length);

        // Save to temp file
        var tempPath = Path.Combine(Path.GetTempPath(), $"clamav_{jobId}_{Path.GetFileName(file.FileName)}");

        try
        {
            await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, useAsync: true);
            await file.CopyToAsync(fileStream);
        }
        catch (Exception ex)
        {
            jobService.UpdateJobStatus(jobId, "error", error: $"Failed to save file: {ex.Message}");
            return Results.Problem("Failed to process upload", statusCode: 500);
        }

        // Enqueue for background processing
        _ = backgroundService.EnqueueTask(async (ct) =>
        {
            return await scanProcessing.ProcessFileScanAsync(jobId, tempPath, ct);
        });

        return Results.Accepted($"/scan/async/{jobId}", new
        {
            jobId,
            status = "queued",
            message = "File uploaded successfully. Use the jobId to check scan status.",
            statusUrl = $"/scan/async/{jobId}"
        });
    }
}

