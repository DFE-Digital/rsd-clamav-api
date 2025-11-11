using GovUK.Dfe.ClamAV.Services;

namespace GovUK.Dfe.ClamAV.Endpoints;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        // Healthz endpoint is open for k8s/monitoring tools
        app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }))
            .WithTags("Health")
            .WithName("HealthCheck")
            .WithDescription("Basic health check endpoint (No Auth Required)")
            .AllowAnonymous();

        app.MapGet("/version", async (IClamAvInfoService clam) =>
        {
            var version = await clam.GetVersionAsync();
            return Results.Ok(new { clamavVersion = version });
        })
        .RequireAuthorization("CanRead")
        .WithTags("Health")
        .WithName("GetVersion")
        .WithDescription("Get ClamAV version information");
    }
}

