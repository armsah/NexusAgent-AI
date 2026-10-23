namespace NexusAgent.Application.Documents;

public sealed record GetDocumentContentQuery(
    string TenantId,
    string RequesterId,
    Guid DocumentId);

public sealed record GetDocumentContentResult(
    Stream Content,
    int Version);

public sealed class GetDocumentContentHandler(
    GetDocumentHandler getDocumentHandler,
    IDocumentContentStore contentStore)
{
    public async Task<GetDocumentContentResult?> HandleAsync(
        GetDocumentContentQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Authorization must complete before opening document content.
        var document = await getDocumentHandler.HandleAsync(
            new GetDocumentQuery(
                query.TenantId,
                query.RequesterId,
                query.DocumentId),
            cancellationToken);

        if (document is null)
        {
            // Nonexistent and inaccessible documents are indistinguishable.
            return null;
        }

        var content = await contentStore.OpenReadAsync(
            query.TenantId,
            query.DocumentId,
            document.ContentVersion,
            cancellationToken);

        return new GetDocumentContentResult(
            content,
            document.ContentVersion);
    }
}
