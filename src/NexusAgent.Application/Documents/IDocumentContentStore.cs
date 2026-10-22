namespace NexusAgent.Application.Documents;

public sealed record StoredDocumentContent(
    string StorageKey,
    string Sha256,
    long Length);

public interface IDocumentContentStore
{
    Task<StoredDocumentContent> PutAsync(
        string tenantId,
        Guid documentId,
        int version,
        Stream content,
        CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(
        string tenantId,
        Guid documentId,
        int version,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string tenantId,
        Guid documentId,
        int version,
        CancellationToken cancellationToken = default);
}
