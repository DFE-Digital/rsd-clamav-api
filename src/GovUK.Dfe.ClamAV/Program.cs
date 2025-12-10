using GovUK.Dfe.ClamAV.Endpoints;
using GovUK.Dfe.ClamAV.Handlers;
using GovUK.Dfe.ClamAV.Services;
using GovUK.Dfe.ClamAV.Swagger;
using GovUK.Dfe.CoreLibs.Security.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Identity.Web;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

Console.WriteLine($"Environment: {builder.Environment.EnvironmentName}");

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

// Add Azure AD authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

builder.Services.AddApplicationAuthorization(builder.Configuration);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ClamAV Scan API",
        Version = "v1",
        Description = "API wrapper for ClamAV virus scanning with async job support (Azure AD Secured)"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer"
    });

    c.OperationFilter<AuthenticationHeaderOperationFilter>();
});


builder.Services.AddOpenApiDocument(configure => { configure.Title = "ClamAv Api"; });

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

// Add authentication & authorization middleware
app.UseAuthentication();
app.UseAuthorization();

// Map endpoints
app.MapHealthEndpoints();
app.MapScanEndpoints();

app.Run();