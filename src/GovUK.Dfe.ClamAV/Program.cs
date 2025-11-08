using GovUK.Dfe.ClamAV.Endpoints;
using GovUK.Dfe.ClamAV.Handlers;
using GovUK.Dfe.ClamAV.Services;
using GovUK.Dfe.CoreLibs.AsyncProcessing.Configurations;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

var maxFileSizeMb = int.TryParse(Environment.GetEnvironmentVariable("MAX_FILE_SIZE_MB"), out var m) ? m : 200;

// Configure Kestrel for better upload performance
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = (long)maxFileSizeMb * 1024 * 1024;
    options.Limits.MinRequestBodyDataRate = new Microsoft.AspNetCore.Server.Kestrel.Core.MinDataRate(
        bytesPerSecond: 100,
        gracePeriod: TimeSpan.FromSeconds(10));
    options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(2);
});

// Limit request body size to MAX_FILE_SIZE_MB
builder.Services.Configure<FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = (long)maxFileSizeMb * 1024 * 1024;
    o.ValueLengthLimit = int.MaxValue;
    o.MultipartHeadersLengthLimit = int.MaxValue;
    o.BufferBody = false; // Don't buffer in memory
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

// Register processing service
builder.Services.AddScoped<IScanProcessingService, ScanProcessingService>();

// Register handlers
builder.Services.AddScoped<FileScanHandler>();
builder.Services.AddScoped<UrlScanHandler>();

// Add background service factory with parallelism
builder.Services.AddBackgroundServiceWithParallelism(
    maxConcurrentWorkers: 4,
    channelCapacity: 100
);

// Register job cleanup service
builder.Services.AddHostedService<JobCleanupService>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "ClamAV Scan API v1");
});

// Map endpoints
app.MapHealthEndpoints();
app.MapScanEndpoints();

app.Run();