using NexusAgent.Infrastructure.Messaging;
using NexusAgent.Infrastructure.Persistence;

namespace NexusAgent.Workers;

public sealed class WorkflowKafkaWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<WorkflowKafkaWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        var bootstrapServers =
            Environment.GetEnvironmentVariable(
                "NEXUS_KAFKA_BOOTSTRAP_SERVERS");

        if (string.IsNullOrWhiteSpace(bootstrapServers))
        {
            logger.LogInformation("Workflow Kafka dispatch is disabled.");
            return;
        }

        using var transport = new KafkaEventTransport(bootstrapServers);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();

                var dbContext = scope.ServiceProvider
                    .GetRequiredService<NexusAgentDbContext>();

                var dispatcher = new PostgresWorkflowEventDispatcher(
                    dbContext, transport);

                if (!await dispatcher.DispatchNextAsync(stoppingToken))
                    await Task.Delay(500, stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    "Workflow dispatch failed: {ExceptionType}",
                    exception.GetType().Name);

                await Task.Delay(2000, stoppingToken);
            }
        }
    }
}