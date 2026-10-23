namespace NexusAgent.Infrastructure.Persistence.Search;

public sealed class ProcessedDocumentIndexEvent
{
    public Guid EventId { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
}