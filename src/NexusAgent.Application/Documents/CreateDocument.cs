using NexusAgent.Application.Abstractions;
using NexusAgent.Domain.Documents;

namespace NexusAgent.Application.Documents;

public sealed record CreateDocumentCommand(
    string TenantId,
    string Title,
    string Classification,
    string OwnerId,
    IReadOnlyCollection<string> AllowedPrincipalIds,
    Stream Content);

public sealed record CreateDocumentResult(
    Guid DocumentId,
    int Version,
    string StorageKey,
    string Sha256,
    long Length);

public sealed class CreateDocumentHandler(
    IKnowledgeDocumentRepository repository,
    IDocumentContentStore contentStore,
    IClock clock,
    IIdGenerator idGenerator)
{
    public async Task<CreateDocumentResult> HandleAsync(
        CreateDocumentCommand command,
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

        // Validate metadata before writing content.
        var document = KnowledgeDocument.Create(
            idGenerator.NewId(),
            command.TenantId,
            command.Title,
            command.Classification,
            command.OwnerId,
            command.AllowedPrincipalIds,
            clock.UtcNow);

        var version = DocumentVersion.Capture(
            idGenerator.NewId(),
            document);

        // The file is stored before database persistence.
        var stored = await contentStore.PutAsync(
            document.TenantId,
            document.Id,
            document.Version,
            command.Content,
            cancellationToken);

        try
        {
            await repository.AddAsync(
                document,
                version,
                cancellationToken);
        }
        catch
        {
            // Compensate for a failed database write.
            // Cleanup uses a separate token so cancellation
            // does not prevent deleting the orphaned file.
            await contentStore.DeleteAsync(
                document.TenantId,
                document.Id,
                document.Version,
                CancellationToken.None);

            throw;
        }

        return new CreateDocumentResult(
            document.Id,
            document.Version,
            stored.StorageKey,
            stored.Sha256,
            stored.Length);
    }
}
