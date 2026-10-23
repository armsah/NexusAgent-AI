using Microsoft.EntityFrameworkCore;
using NexusAgent.Infrastructure.Persistence;
using NexusAgent.Infrastructure.Persistence.Outbox;

namespace NexusAgent.Workers;

public sealed class Worker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        var batchSize = configuration.GetValue(
            "Outbox:BatchSize", 1);

        var pollingMilliseconds = configuration.GetValue(
            "Outbox:PollingIntervalMilliseconds", 1000);

        var leaseSeconds = configuration.GetValue(
            "Outbox:LeaseSeconds", 60);

        if (batchSize is < 1 or > 1000)
            throw new InvalidOperationException(
                "Outbox:BatchSize must be between 1 and 1000.");

        if (pollingMilliseconds < 100)
            throw new InvalidOperationException(
                "Outbox polling interval must be at least 100 ms.");

        if (leaseSeconds is < 1 or > 3600)
            throw new InvalidOperationException(
                "Outbox lease must be between 1 and 3600 seconds.");

        logger.LogInformation(
            "Outbox worker started. BatchSize={BatchSize}, PollingMs={PollingMs}",
            batchSize, pollingMilliseconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            var claimedCount = 0;

            try
            {
                await using var scope =
                    scopeFactory.CreateAsyncScope();

                var db = scope.ServiceProvider
                    .GetRequiredService<NexusAgentDbContext>();

                var claimer = new PostgresOutboxClaimer(db);

                var messages = await claimer.ClaimAsync(
                    batchSize,
                    TimeSpan.FromSeconds(leaseSeconds),
                    stoppingToken);

                claimedCount = messages.Count;

                foreach (var message in messages)
                {
                    stoppingToken.ThrowIfCancellationRequested();

                    await ProcessMessageAsync(
                        message,
                        stoppingToken);
                }
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Outbox polling cycle failed.");
            }

            if (claimedCount == 0)
            {
                try
                {
                    await Task.Delay(
                        pollingMilliseconds,
                        stoppingToken);
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        logger.LogInformation("Outbox worker stopped.");
    }

    private async Task ProcessMessageAsync(
        ClaimedOutboxMessage message,
        CancellationToken cancellationToken)
    {
        await using var scope =
            scopeFactory.CreateAsyncScope();

        var db = scope.ServiceProvider
            .GetRequiredService<NexusAgentDbContext>();

        try
        {
            var publisher = scope.ServiceProvider
                .GetRequiredService<NexusAgent.Application.Abstractions.IDocumentIngestionEventPublisher>();

            using var leaseTimeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

            leaseTimeout.CancelAfter(
                TimeSpan.FromSeconds(
                    Math.Max(1, configuration.GetValue("Outbox:LeaseSeconds", 60) - 1)));

            await publisher.PublishAsync(
                message.Id,
                message.EventType,
                message.TenantId,
                message.DocumentId,
                message.DocumentVersion,
                message.Payload,
                leaseTimeout.Token);

            var acknowledger = new PostgresOutboxAcknowledger(db);

            var acknowledged = await acknowledger.AcknowledgeAsync(
                message.Id,
                message.ClaimToken,
                cancellationToken);

            if (!acknowledged)
            {
                logger.LogWarning(
                    "Outbox acknowledgment rejected because the claim is stale. " +
                    "MessageId={MessageId}",
                    message.Id);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Outbox processing failed. MessageId={MessageId}",
                message.Id);

            try
            {
                var recorder = new PostgresOutboxFailureRecorder(db);

                var recorded = await recorder.RecordFailureAsync(
                    message.Id,
                    message.ClaimToken,
                    "Outbox publication failed. ExceptionType=" +
                    exception.GetType().Name,
                    CancellationToken.None);

                if (!recorded)
                {
                    logger.LogWarning(
                        "Failure was not recorded because the claim expired " +
                        "or was replaced. MessageId={MessageId}",
                        message.Id);
                }
            }
            catch (Exception recordingException)
            {
                logger.LogError(
                    recordingException,
                    "Could not record outbox failure. MessageId={MessageId}. " +
                    "The lease will expire for recovery.",
                    message.Id);
            }
        }
    }
}