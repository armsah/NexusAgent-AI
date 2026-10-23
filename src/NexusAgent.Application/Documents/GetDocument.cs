using NexusAgent.Application.Abstractions;

namespace NexusAgent.Application.Documents;

public sealed record GetDocumentQuery(
    string TenantId,
    string RequesterId,
    Guid DocumentId);

public sealed record GetDocumentResult(
    Guid DocumentId,
    int Version,
    int ContentVersion,
    string Title,
    string Classification,
    string OwnerId,
    IReadOnlyCollection<string> AllowedPrincipalIds,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed class GetDocumentHandler(
    IKnowledgeDocumentRepository repository)
{
    public async Task<GetDocumentResult?> HandleAsync(
        GetDocumentQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (string.IsNullOrWhiteSpace(query.TenantId) ||
            string.IsNullOrWhiteSpace(query.RequesterId) ||
            query.DocumentId == Guid.Empty)
        {
            return null;
        }

        var document = await repository.GetAsync(
            query.TenantId,
            query.DocumentId,
            cancellationToken);

        if (document is null ||
            !document.IsAccessibleBy(
                query.TenantId,
                query.RequesterId))
        {
            // Do not reveal whether an inaccessible document exists.
            return null;
        }

        return new GetDocumentResult(
            document.Id,
            document.Version,
            document.ContentVersion,
            document.Title,
            document.Classification,
            document.OwnerId,
            [.. document.AllowedPrincipalIds],
            document.CreatedAt,
            document.UpdatedAt);
    }
}
