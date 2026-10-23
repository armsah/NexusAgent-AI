using NexusAgent.Application.Abstractions;
using NexusAgent.Domain.Common;
using NexusAgent.Domain.Documents;

namespace NexusAgent.Application.Documents;

public sealed record DeleteDocumentCommand(
    string TenantId,
    string RequesterId,
    Guid DocumentId,
    int ExpectedVersion);

public sealed record DeleteDocumentResult(
    Guid DocumentId,
    int Version,
    bool IsDeleted);

public sealed class DeleteDocumentHandler(
    IKnowledgeDocumentRepository repository,
    IClock clock,
    IIdGenerator idGenerator)
{
    public async Task<DeleteDocumentResult?> HandleAsync(
        DeleteDocumentCommand command,
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
            throw new DomainException("Document version conflict.");
        }

        document.Delete(command.TenantId, clock.UtcNow);

        var snapshot = DocumentVersion.Capture(
            idGenerator.NewId(),
            document);

        await repository.SaveVersionAsync(
            document,
            snapshot,
            cancellationToken);

        return new DeleteDocumentResult(
            document.Id,
            document.Version,
            IsDeleted: true);
    }
}
