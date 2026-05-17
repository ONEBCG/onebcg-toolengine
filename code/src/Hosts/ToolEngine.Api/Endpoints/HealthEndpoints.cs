namespace ToolEngine.Api.Endpoints;

using Microsoft.Extensions.Diagnostics.HealthChecks;

public static class HealthEndpoints
{
    public static WebApplication MapHealthEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            ResultStatusCodes =
            {
                [HealthStatus.Healthy]   = 200,
                [HealthStatus.Degraded]  = 200,
                [HealthStatus.Unhealthy] = 503
            }
        });

        app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready")
        });

        return app;
    }
}
