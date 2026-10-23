using NexusAgent.Application.Abstractions;
using NexusAgent.Domain.Common;
using NexusAgent.Domain.Documents;

namespace NexusAgent.Application.Documents;

public sealed record UpdateDocumentCommand(
    string TenantId,
    string RequesterId,
    Guid DocumentId,
    int ExpectedVersion,
    string Title,
    string Classification,
    IReadOnlyCollection<string> AllowedPrincipalIds);

public sealed record UpdateDocumentResult(
    Guid DocumentId,
    int Version,
    int ContentVersion,
    string Title,
    string Classification,
    IReadOnlyCollection<string> AllowedPrincipalIds);

public sealed class UpdateDocumentHandler(
    IKnowledgeDocumentRepository repository,
    IClock clock,
    IIdGenerator idGenerator)
{
    public async Task<UpdateDocumentResult?> HandleAsync(
        UpdateDocumentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.TenantId) ||
            string.IsNullOrWhiteSpace(command.RequesterId) ||
            command.DocumentId == Guid.Empty)
        {
            return null;
        }

        var document = await repository.GetAsync(
            command.TenantId,
            command.DocumentId,
            cancellationToken);

        if (document is null ||
            !document.IsAccessibleBy(
                command.TenantId,
                command.RequesterId) ||
            !string.Equals(
                document.OwnerId,
                command.RequesterId,
                StringComparison.Ordinal))
        {
            return null;
        }

        if (command.ExpectedVersion != document.Version)
        {
            throw new DomainException(
                "Document version conflict.");
        }

        document.Update(
            command.TenantId,
            command.Title,
            command.Classification,
            command.AllowedPrincipalIds,
            clock.UtcNow);

        var snapshot = DocumentVersion.Capture(
            idGenerator.NewId(),
            document);

        await repository.SaveVersionAsync(
            document,
            snapshot,
            cancellationToken);

        return new UpdateDocumentResult(
            document.Id,
            document.Version,
            document.ContentVersion,
            document.Title,
            document.Classification,
            [.. document.AllowedPrincipalIds]);
    }
}
