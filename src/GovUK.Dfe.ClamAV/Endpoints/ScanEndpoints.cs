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
            .Produces<ScanResponse>(200)
            .Produces<ErrorResponse>(400)
            .Produces(500)
            //.RequireAuthorization("CanRead")
            .WithName("ScanSync")
            .WithDescription("Upload a file for synchronous virus scanning. Waits for scan to complete before returning.")
            .DisableAntiforgery();

        // Asynchronous file upload scan
        scanGroup.MapPost("/async", async (IFormFile file, FileScanHandler handler) => 
            await handler.HandleAsyncAsync(file))
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<AsyncScanResponse>(202)
            .Produces<ErrorResponse>(400)
            //.RequireAuthorization("CanRead")
            .WithName("ScanAsync")
            .WithDescription("Upload a file for asynchronous virus scanning. Returns immediately with a job ID.")
            .DisableAntiforgery();

        // Asynchronous URL scan
        scanGroup.MapPost("/async/url", async (ScanUrlRequest urlRequest, UrlScanHandler handler) => 
            await handler.HandleAsync(urlRequest))
            .Accepts<ScanUrlRequest>("application/json")
            .Produces<AsyncScanResponse>(202)
            .Produces<ErrorResponse>(400)
            .Produces(401)
            .Produces(403)
            //.RequireAuthorization("CanRead")
            .WithName("ScanAsyncUrl")
            .WithDescription("Download a file from a URL and scan it asynchronously. Returns immediately with job ID. Download and scan happen in background. Set 'isBase64' to true if the URL is Base64 encoded.");

        // Check scan status
        scanGroup.MapGet("/async/{jobId}", (string jobId, IScanJobService jobService) =>
        {
            var job = jobService.GetJob(jobId);
            if (job == null)
                return Results.NotFound(new ErrorResponse { Error = "Job not found" });

            return Results.Ok(new ScanStatusResponse
            {
                JobId = job.JobId,
                Status = job.Status,
                FileName = job.FileName,
                FileSize = job.FileSize,
                Engine = job.Engine,
                Malware = job.Malware,
                Error = job.Error,
                CreatedAt = job.CreatedAt,
                CompletedAt = job.CompletedAt,
                ScanDurationMs = job.ScanDuration?.TotalMilliseconds
            });
        })
        .Produces<ScanStatusResponse>(200)
        .Produces<ErrorResponse>(404)
        .Produces(401)
        .Produces(403)
        //.RequireAuthorization("CanRead")
        .WithName("GetScanStatus")
        .WithDescription("Get the status of an asynchronous scan job");

        // List all jobs
        scanGroup.MapGet("/jobs", (IScanJobService jobService) =>
        {
            var jobs = jobService.GetAllJobs().Take(100).ToList(); // Limit to 100 most recent
            return Results.Ok(new JobsListResponse
            {
                Jobs = jobs.Select(j => new JobSummary
                {
                    JobId = j.JobId,
                    Status = j.Status,
                    FileName = j.FileName,
                    FileSize = j.FileSize,
                    CreatedAt = j.CreatedAt,
                    CompletedAt = j.CompletedAt,
                    ScanDurationMs = j.ScanDuration?.TotalMilliseconds
                }),
                Count = jobs.Count
            });
        })
        .Produces<JobsListResponse>(200)
        .Produces(401)
        .Produces(403)
        //.RequireAuthorization("CanRead")
        .WithName("ListJobs")
        .WithDescription("List recent scan jobs (for monitoring/debugging)");
    }
}

