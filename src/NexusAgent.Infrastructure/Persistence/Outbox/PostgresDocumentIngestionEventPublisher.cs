using Microsoft.EntityFrameworkCore;
using NexusAgent.Application.Abstractions;

namespace NexusAgent.Infrastructure.Persistence.Outbox;

public sealed class PostgresDocumentIngestionEventPublisher(
    NexusAgentDbContext dbContext)
    : IDocumentIngestionEventPublisher
{
    public async Task PublishAsync(
        Guid eventId,
        string eventType,
        string tenantId,
        Guid documentId,
        int documentVersion,
        string payload,
        CancellationToken cancellationToken = default)
    {
        if (eventId == Guid.Empty)
            throw new ArgumentException("Event ID is required.", nameof(eventId));

        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("Event type is required.", nameof(eventType));

        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("Tenant ID is required.", nameof(tenantId));

        if (documentId == Guid.Empty)
            throw new ArgumentException("Document ID is required.", nameof(documentId));

        if (documentVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(documentVersion));

        if (string.IsNullOrWhiteSpace(payload))
            throw new ArgumentException("Payload is required.", nameof(payload));

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO published_document_events
                (event_id, event_type, tenant_id, document_id,
                 document_version, payload, published_at)
            VALUES
                ({eventId}, {eventType}, {tenantId}, {documentId},
                 {documentVersion}, CAST({payload} AS jsonb),
                 clock_timestamp())
            ON CONFLICT (event_id) DO NOTHING;
            """,
            cancellationToken);
    }
}