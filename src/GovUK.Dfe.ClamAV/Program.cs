using GovUK.Dfe.ClamAV.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.OpenApi.Models;
using nClam;
using System.Net;
using System.Threading.Channels;

var builder = WebApplication.CreateBuilder(args);

var maxFileSizeMb = int.TryParse(Environment.GetEnvironmentVariable("MAX_FILE_SIZE_MB"), out var m) ? m : 200;

// Limit request body size to MAX_FILE_SIZE_MB
builder.Services.Configure<FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = (long)maxFileSizeMb * 1024 * 1024;
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ClamAV Scan API",
        Version = "v1",
        Description = "API wrapper for ClamAV virus scanning with async job support"
    });
});

// Register services
builder.Services.AddSingleton<IClamAvInfoService, ClamAvInfoService>();
builder.Services.AddSingleton<IScanJobService, ScanJobService>();

// Create channel for background processing (bounded to prevent memory issues)
var scanChannel = Channel.CreateBounded<ScanRequest>(new BoundedChannelOptions(100)
{
    FullMode = BoundedChannelFullMode.Wait
});
builder.Services.AddSingleton(scanChannel);

// Register background service
builder.Services.AddHostedService<BackgroundScanService>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "ClamAV Scan API v1");
});


app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.MapGet("/version", async (IClamAvInfoService clam) =>
{
    var version = await clam.GetVersionAsync();
    return Results.Ok(new { clamavVersion = version });
});

app.MapPost("/scan/async", async (
    IFormFile file,
    IScanJobService jobService,
    Channel<ScanRequest> channel) =>
{
    if (file == null || file.Length == 0)
        return Results.BadRequest(new { error = "Missing or empty file" });

    // Create job
    var jobId = jobService.CreateJob(file.FileName, file.Length);

    // Read file data into memory (for background processing)
    using var memoryStream = new MemoryStream();
    await file.CopyToAsync(memoryStream);
    var fileData = memoryStream.ToArray();

    // Queue for background processing
    await channel.Writer.WriteAsync(new ScanRequest
    {
        JobId = jobId,
        FileData = fileData
    });

    return Results.Accepted($"/scan/async/{jobId}", new
    {
        jobId,
        status = "queued",
        message = "File uploaded successfully. Use the jobId to check scan status.",
        statusUrl = $"/scan/async/{jobId}"
    });
})
.Accepts<IFormFile>("multipart/form-data")
.Produces(202)
.Produces(400)
.WithName("ScanAsync")
.WithDescription("Upload a file for asynchronous virus scanning. Returns immediately with a job ID.")
.DisableAntiforgery();

// Check Async Scan Status
app.MapGet("/scan/async/{jobId}", (string jobId, IScanJobService jobService) =>
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
.WithName("GetScanStatus")
.WithDescription("Get the status of an asynchronous scan job");

app.MapPost("/scan", async (IFormFile file) =>
{
    if (file == null || file.Length == 0)
        return Results.BadRequest(new { error = "Missing or empty file" });

    var host = Environment.GetEnvironmentVariable("CLAMD_HOST") ?? "127.0.0.1";
    var port = int.TryParse(Environment.GetEnvironmentVariable("CLAMD_PORT"), out var p) ? p : 3310;

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
})
.Accepts<IFormFile>("multipart/form-data")
.Produces(200)
.Produces(400)
.Produces(500)
.WithName("ScanSync")
.WithDescription("Upload a file for synchronous virus scanning. Waits for scan to complete before returning.")
.DisableAntiforgery();

// List All Jobs 
app.MapGet("/scan/jobs", (IScanJobService jobService) =>
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
.WithName("ListJobs")
.WithDescription("List recent scan jobs (for monitoring/debugging)");


app.Run();