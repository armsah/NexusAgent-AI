using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NexusAgent.Infrastructure.Persistence;

namespace NexusAgent.Infrastructure.Messaging;

public sealed class PostgresWorkflowEventDispatcher(
    NexusAgentDbContext dbContext,
    KafkaEventTransport transport)
{
    public async Task<bool> DispatchNextAsync(
        CancellationToken cancellationToken = default)
    {
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var connection = dbContext.Database.GetDbConnection();

        Guid eventId;
        Guid workflowId;
        string tenantId;
        string payload;

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction.GetDbTransaction();

            command.CommandText =
                """
                SELECT e.event_id, e.workflow_id, e.tenant_id, e.payload::text
                FROM kafka_workflow_events e
                WHERE e.published_at IS NULL
                  AND e.next_attempt_at <= clock_timestamp()
                  AND NOT EXISTS (
                      SELECT 1
                      FROM kafka_workflow_events earlier
                      WHERE earlier.tenant_id = e.tenant_id
                        AND earlier.workflow_id = e.workflow_id
                        AND earlier.sequence < e.sequence
                        AND earlier.published_at IS NULL
                  )
                ORDER BY e.created_at, e.event_id
                LIMIT 1
                FOR UPDATE OF e SKIP LOCKED
                """;

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
                return false;

            eventId = reader.GetGuid(0);
            workflowId = reader.GetGuid(1);
            tenantId = reader.GetString(2);
            payload = reader.GetString(3);
        }

        try
        {
            await transport.PublishAsync(
                KafkaEventTransport.WorkflowsTopic,
                tenantId,
                workflowId,
                payload,
                cancellationToken);
        }
        catch (Exception exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            var error = exception.GetType().Name;

            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE kafka_workflow_events
                SET attempts = attempts + 1,
                    last_error = {error},
                    next_attempt_at = clock_timestamp()
                        + make_interval(secs =>
                            LEAST(300, power(2, LEAST(attempts, 8)))::integer)
                WHERE event_id = {eventId}
                """,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE kafka_workflow_events
            SET published_at = clock_timestamp(),
                attempts = attempts + 1,
                last_error = NULL
            WHERE event_id = {eventId}
            """,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return true;
    }
}