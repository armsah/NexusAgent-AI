namespace NexusAgent.Application.Abstractions;

public interface IDocumentIngestionEventPublisher
{
    Task PublishAsync(
        Guid eventId,
        string eventType,
        string tenantId,
        Guid documentId,
        int documentVersion,
        string payload,
        CancellationToken cancellationToken = default);
}