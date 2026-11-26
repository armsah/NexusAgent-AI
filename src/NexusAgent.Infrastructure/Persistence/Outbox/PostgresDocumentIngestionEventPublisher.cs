using NexusAgent.Infrastructure.Messaging;
using NexusAgent.Application.Documents;
using System.Text.Json;
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

        // Preserve the domain payload when Kafka publication is disabled.
        // For Kafka publications retain the exact serialized wire envelope.
        var persistedPayload = payload;

        var bootstrapServers =
            Environment.GetEnvironmentVariable("NEXUS_KAFKA_BOOTSTRAP_SERVERS");

        if (!string.IsNullOrWhiteSpace(bootstrapServers))
        {
            using var json = JsonDocument.Parse(payload);
            var fields = json.RootElement.EnumerateObject()
                .ToDictionary(
                    field => field.Name,
                    field => field.Value,
                    StringComparer.OrdinalIgnoreCase);

            var contentVersion = fields["contentVersion"].GetInt32();
            var occurredAt = fields["occurredAt"].GetDateTimeOffset();

            var wirePayload = DocumentEventWireCodec.Serialize(
                new DocumentIngestionEvent(
                    EventId: eventId,
                    EventType: eventType,
                    TenantId: tenantId,
                    DocumentId: documentId,
                    DocumentVersion: documentVersion,
                    ContentVersion: contentVersion,
                    OccurredAt: occurredAt));

            using var transport = new KafkaEventTransport(bootstrapServers);

            await transport.PublishAsync(
                KafkaEventTransport.DocumentsTopic,
                tenantId,
                documentId,
                wirePayload,
                cancellationToken);

            persistedPayload = wirePayload;
        }


        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO published_document_events
                (event_id, event_type, tenant_id, document_id,
                 document_version, payload, published_at)
            VALUES
                ({eventId}, {eventType}, {tenantId}, {documentId},
                 {documentVersion}, CAST({persistedPayload} AS jsonb),
                 clock_timestamp())
            ON CONFLICT (event_id) DO UPDATE
            SET payload = EXCLUDED.payload
            WHERE published_document_events.tenant_id = EXCLUDED.tenant_id
              AND published_document_events.document_id = EXCLUDED.document_id
              AND published_document_events.document_version = EXCLUDED.document_version
              AND published_document_events.event_type = EXCLUDED.event_type
              AND NOT (published_document_events.payload ? 'schemaVersion')
              AND EXCLUDED.payload ? 'schemaVersion';
            """,
            cancellationToken);
    }
}
