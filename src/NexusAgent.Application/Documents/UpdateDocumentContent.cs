using NexusAgent.Application.Abstractions;
using NexusAgent.Domain.Common;
using NexusAgent.Domain.Documents;

namespace NexusAgent.Application.Documents;

public sealed record UpdateDocumentContentCommand(
    string TenantId,
    string RequesterId,
    Guid DocumentId,
    int ExpectedVersion,
    Stream Content);

public sealed record UpdateDocumentContentResult(
    Guid DocumentId,
    int Version,
    int ContentVersion,
    string StorageKey,
    string Sha256,
    long Length);

public sealed class UpdateDocumentContentHandler(
    IKnowledgeDocumentRepository repository,
    IDocumentContentStore contentStore,
    IClock clock,
    IIdGenerator idGenerator)
{
    public async Task<UpdateDocumentContentResult?> HandleAsync(
        UpdateDocumentContentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Content);

        if (!command.Content.CanRead)
        {
            throw new ArgumentException(
                "Document content must be readable.",
                nameof(command));
        }

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

        document.UpdateContent(
            command.TenantId,
            clock.UtcNow);

        var snapshot = DocumentVersion.Capture(
            idGenerator.NewId(),
            document);

        var stored = await contentStore.PutAsync(
            document.TenantId,
            document.Id,
            document.ContentVersion,
            command.Content,
            cancellationToken);

        try
        {
            await repository.SaveVersionAsync(
                document,
                snapshot,
                cancellationToken);
        }
        catch
        {
            await contentStore.DeleteAsync(
                document.TenantId,
                document.Id,
                document.ContentVersion,
                CancellationToken.None);

            throw;
        }

        return new UpdateDocumentContentResult(
            document.Id,
            document.Version,
            document.ContentVersion,
            stored.StorageKey,
            stored.Sha256,
            stored.Length);
    }
}
