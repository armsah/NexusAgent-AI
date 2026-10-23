using Microsoft.EntityFrameworkCore;
using NexusAgent.Infrastructure.Persistence;

namespace NexusAgent.Api.Health;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapNexusAgentHealthEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/health",
            () => Results.Ok(new
            {
                status = "healthy"
            }));

        app.MapGet(
            "/health/ready",
            async (
                NexusAgentDbContext dbContext,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var canConnect = await dbContext.Database.CanConnectAsync(
                        cancellationToken);

                    return canConnect
                        ? Results.Ok(new
                        {
                            status = "ready"
                        })
                        : Results.StatusCode(
                            StatusCodes.Status503ServiceUnavailable);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    // Readiness must fail closed without leaking connection
                    // strings, database internals, or credentials.
                    return Results.StatusCode(
                        StatusCodes.Status503ServiceUnavailable);
                }
            });

        return app;
    }
}