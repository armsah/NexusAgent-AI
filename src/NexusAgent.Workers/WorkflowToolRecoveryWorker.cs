using Microsoft.EntityFrameworkCore;
using NexusAgent.Infrastructure.Persistence;

namespace NexusAgent.Workers;

public sealed class WorkflowToolRecoveryWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<WorkflowToolRecoveryWorker> logger)
    : BackgroundService
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

                var recovered = await db.Database.SqlQueryRaw<int>(
                    """
                    SELECT recover_workflow_tool_executions(100) AS "Value"
                    """).SingleAsync(stoppingToken);

                var expired = await db.Database.SqlQueryRaw<int>(
                    """
                    SELECT expire_unused_workflow_approvals(100) AS "Value"
                    """).SingleAsync(stoppingToken);

                if (expired > 0)
                {
                    logger.LogInformation(
                        "Expired {Count} unused workflow approvals.",
                        expired);
                }

                if (recovered > 0)
                {
                    logger.LogWarning(
                        "Marked {Count} interrupted tool executions uncertain.",
                        recovered);
                }

                await Task.Delay(
                    recovered == 100 ? 100 : 1000,
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
                    "Tool recovery failed: {ErrorType}",
                    exception.GetType().Name);

                await Task.Delay(2000, stoppingToken);
            }
        }
    }
}
