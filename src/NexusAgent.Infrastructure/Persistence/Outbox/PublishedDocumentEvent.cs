namespace NexusAgent.Infrastructure.Persistence.Outbox;

public sealed class PublishedDocumentEvent
{
    private PublishedDocumentEvent() { }

    public Guid EventId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string TenantId { get; private set; } = string.Empty;
    public Guid DocumentId { get; private set; }
    public int DocumentVersion { get; private set; }
    public string Payload { get; private set; } = string.Empty;
    public DateTimeOffset PublishedAt { get; private set; }

    public static PublishedDocumentEvent Create(
        Guid eventId,
        string eventType,
        string tenantId,
        Guid documentId,
        int documentVersion,
        string payload,
        DateTimeOffset publishedAt)
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

        return new PublishedDocumentEvent
        {
            EventId = eventId,
            EventType = eventType,
            TenantId = tenantId,
            DocumentId = documentId,
            DocumentVersion = documentVersion,
            Payload = payload,
            PublishedAt = publishedAt
        };
    }
}