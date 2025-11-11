using GovUK.Dfe.ClamAV.Handlers;
using GovUK.Dfe.ClamAV.Models;
using GovUK.Dfe.ClamAV.Services;

namespace GovUK.Dfe.ClamAV.Endpoints;

public static class ScanEndpoints
{
    public static void MapScanEndpoints(this IEndpointRouteBuilder app)
    {
        var scanGroup = app.MapGroup("/scan")
            .WithTags("Scan");

        // Synchronous scan
        scanGroup.MapPost("", async (IFormFile file, FileScanHandler handler) => 
            await handler.HandleSyncAsync(file))
            .Accepts<IFormFile>("multipart/form-data")
            .Produces(200)
            .Produces(400)
            .Produces(500)
            .RequireAuthorization()
            .WithName("ScanSync")
            .WithDescription("Upload a file for synchronous virus scanning. Waits for scan to complete before returning.")
            .DisableAntiforgery();

        // Asynchronous file upload scan
        scanGroup.MapPost("/async", async (IFormFile file, FileScanHandler handler) => 
            await handler.HandleAsyncAsync(file))
            .Accepts<IFormFile>("multipart/form-data")
            .Produces(202)
            .Produces(400)
            .RequireAuthorization()
            .WithName("ScanAsync")
            .WithDescription("Upload a file for asynchronous virus scanning. Returns immediately with a job ID.")
            .DisableAntiforgery();

        // Asynchronous URL scan
        scanGroup.MapPost("/async/url", async (ScanUrlRequest urlRequest, UrlScanHandler handler) => 
            await handler.HandleAsync(urlRequest))
            .Accepts<ScanUrlRequest>("application/json")
            .Produces(202)
            .Produces(400)
            .Produces(401)
            .RequireAuthorization()
            .WithName("ScanAsyncUrl")
            .WithDescription("Download a file from a URL and scan it asynchronously. Returns immediately with job ID. Download and scan happen in background. Set 'isBase64' to true if the URL is Base64 encoded.");

        // Check scan status
        scanGroup.MapGet("/async/{jobId}", (string jobId, IScanJobService jobService) =>
        {
            var job = jobService.GetJob(jobId);
            if (job == null)
                return Results.NotFound(new { error = "Job not found" });

            return Results.Ok(new
            {
                jobId = job.JobId,
                status = job.Status,
                fileName = job.FileName,
                fileSize = job.FileSize,
                engine = job.Engine,
                malware = job.Malware,
                error = job.Error,
                createdAt = job.CreatedAt,
                completedAt = job.CompletedAt,
                scanDurationMs = job.ScanDuration?.TotalMilliseconds
            });
        })
        .Produces(200)
        .Produces(404)
        .Produces(401)
        .RequireAuthorization()
        .WithName("GetScanStatus")
        .WithDescription("Get the status of an asynchronous scan job");

        // List all jobs
        scanGroup.MapGet("/jobs", (IScanJobService jobService) =>
        {
            var jobs = jobService.GetAllJobs().Take(100); // Limit to 100 most recent
            return Results.Ok(new
            {
                jobs = jobs.Select(j => new
                {
                    jobId = j.JobId,
                    status = j.Status,
                    fileName = j.FileName,
                    fileSize = j.FileSize,
                    createdAt = j.CreatedAt,
                    completedAt = j.CompletedAt,
                    scanDurationMs = j.ScanDuration?.TotalMilliseconds
                }),
                count = jobs.Count()
            });
        })
        .Produces(200)
        .Produces(401)
        .RequireAuthorization()
        .WithName("ListJobs")
        .WithDescription("List recent scan jobs (for monitoring/debugging)");
    }
}

