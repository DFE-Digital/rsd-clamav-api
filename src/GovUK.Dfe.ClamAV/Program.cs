using GovUK.Dfe.ClamAV.Endpoints;
using GovUK.Dfe.ClamAV.Handlers;
using GovUK.Dfe.ClamAV.Services;
using GovUK.Dfe.ClamAV.Swagger;
using GovUK.Dfe.CoreLibs.Security.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Identity.Web;
using Microsoft.OpenApi;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Configure to read from appsettings.json and environment-specific files
builder.Configuration
    .SetBasePath(builder.Environment.ContentRootPath)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args);

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
// Redis lets any replica answer a status poll and keeps jobs across restarts. Without it, jobs live in
// this process only and the API must run as a single replica.
var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
var useRedisJobStore = !string.IsNullOrWhiteSpace(redisConnectionString);
if (useRedisJobStore)
{
    var redisOptions = ConfigurationOptions.Parse(redisConnectionString!);
    redisOptions.AbortOnConnectFail = false;
    builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisOptions));
    builder.Services.AddSingleton<IScanJobService, RedisScanJobService>();
}
else
{
    builder.Services.AddSingleton<IScanJobService, ScanJobService>();
}

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

if (!useRedisJobStore)
{
    app.Logger.LogWarning(
        "ConnectionStrings:Redis is not set: scan jobs are kept in memory. Run a single replica, " +
        "or a status poll that reaches another replica returns 404");
}

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