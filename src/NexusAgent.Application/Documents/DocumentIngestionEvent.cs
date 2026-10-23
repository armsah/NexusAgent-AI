namespace NexusAgent.Application.Documents;

public static class DocumentIngestionEventTypes
{
    public const string Created = "document.created";
    public const string Updated = "document.updated";
    public const string Deleted = "document.deleted";
}

public sealed record DocumentIngestionEvent(
    Guid EventId,
    string EventType,
    string TenantId,
    Guid DocumentId,
    int DocumentVersion,
    int ContentVersion,
    DateTimeOffset OccurredAt);