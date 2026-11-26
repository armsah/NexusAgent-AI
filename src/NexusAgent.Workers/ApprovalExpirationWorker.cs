using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexusAgent.Infrastructure.Persistence;

namespace NexusAgent.Workers;

public sealed class ApprovalExpirationWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ApprovalExpirationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider
                    .GetRequiredService<NexusAgentDbContext>();

                var resolved = await db.Database.SqlQueryRaw<int>(
                    """
                    SELECT expire_workflow_approvals(100) AS "Value"
                    """).SingleAsync(stoppingToken);

                if (resolved > 0)
                    logger.LogInformation(
                        "Resolved {Count} expired approvals.", resolved);

                await Task.Delay(
                    resolved == 100 ? 100 : 1000,
                    stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    "Approval expiration failed: {ExceptionType}",
                    exception.GetType().Name);

                await Task.Delay(2000, stoppingToken);
            }
        }
    }
}
