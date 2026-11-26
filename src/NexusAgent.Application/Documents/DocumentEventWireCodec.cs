using System.Text.Json;

namespace NexusAgent.Application.Documents;

public static class DocumentEventWireCodec
{
    public static string Serialize(DocumentIngestionEvent value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.EventId == Guid.Empty ||
            value.DocumentId == Guid.Empty ||
            string.IsNullOrWhiteSpace(value.TenantId) ||
            value.TenantId.Length > 256 ||
            value.TenantId != value.TenantId.Trim() ||
            value.TenantId.Any(char.IsControl) ||
            value.DocumentVersion < 1 ||
            value.ContentVersion < 1 ||
            value.ContentVersion > value.DocumentVersion ||
            value.EventType is not (
                DocumentIngestionEventTypes.Created or
                DocumentIngestionEventTypes.Updated or
                DocumentIngestionEventTypes.Deleted))
        {
            throw new ArgumentException("Invalid document event.", nameof(value));
        }

        return JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            eventId = value.EventId,
            eventType = value.EventType,
            tenantId = value.TenantId,
            documentId = value.DocumentId,
            documentVersion = value.DocumentVersion,
            contentVersion = value.ContentVersion,
            occurredAt = value.OccurredAt.UtcDateTime
        });
    }
}