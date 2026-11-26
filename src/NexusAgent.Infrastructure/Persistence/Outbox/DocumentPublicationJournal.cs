using Microsoft.EntityFrameworkCore;
using NexusAgent.Infrastructure.Messaging;

namespace NexusAgent.Infrastructure.Persistence.Outbox;

public sealed class DocumentPublicationJournal(
    NexusAgentDbContext dbContext)
{
    public async Task<string> PrepareAsync(
        Guid eventId,
        string eventType,
        string tenantId,
        Guid documentId,
        int documentVersion,
        string wirePayload,
        CancellationToken cancellationToken = default)
    {
        if (dbContext.Database.CurrentTransaction is not null ||
            global::System.Transactions.Transaction.Current is not null)
        {
            throw new InvalidOperationException(
                "Publication intent requires an independently committed journal.");
        }

        var partitionKey =
            KafkaEventTransport.PartitionKey(tenantId, documentId);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO public.kafka_document_publications
                (event_id, tenant_id, document_id,
                 document_version, event_type, partition_key, wire_payload)
            VALUES
                ({eventId}, {tenantId}, {documentId},
                 {documentVersion}, {eventType}, {partitionKey}, {wirePayload})
            ON CONFLICT (event_id) DO NOTHING;
            """,
            cancellationToken);

        var matching = await dbContext.Database.SqlQuery<bool>(
            $"""
            SELECT EXISTS (
                SELECT 1
                FROM public.kafka_document_publications
                WHERE event_id = {eventId}
                  AND tenant_id = {tenantId}
                  AND document_id = {documentId}
                  AND document_version = {documentVersion}
                  AND event_type = {eventType}
                  AND partition_key = {partitionKey}
                  AND wire_payload = {wirePayload}
            ) AS "Value"
            """).SingleAsync(cancellationToken);

        if (!matching)
        {
            throw new InvalidOperationException(
                "Conflicting immutable publication identity.");
        }

        return await dbContext.Database.SqlQuery<string>(
            $"""
            SELECT wire_payload AS "Value"
            FROM public.kafka_document_publications
            WHERE event_id = {eventId}
            """).SingleAsync(cancellationToken);
    }

    public async Task<bool> IsAcknowledgedAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Database.SqlQuery<bool>(
            $"""
            SELECT acknowledged_at IS NOT NULL AS "Value"
            FROM public.kafka_document_publications
            WHERE event_id = {eventId}
            """).SingleAsync(cancellationToken);
    }

    public async Task AcknowledgeAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        var affected =
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE public.kafka_document_publications
                SET acknowledged_at =
                    COALESCE(acknowledged_at, clock_timestamp())
                WHERE event_id = {eventId}
                """,
                cancellationToken);

        if (affected != 1)
        {
            throw new InvalidOperationException(
                "Publication acknowledgement has no durable intent.");
        }
    }
}
