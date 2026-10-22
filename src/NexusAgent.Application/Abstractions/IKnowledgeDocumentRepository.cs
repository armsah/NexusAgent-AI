using NexusAgent.Domain.Documents;

namespace NexusAgent.Application.Abstractions;

public interface IKnowledgeDocumentRepository
{
    Task<KnowledgeDocument?> GetAsync(
        string tenantId,
        Guid documentId,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        KnowledgeDocument document,
        DocumentVersion initialVersion,
        CancellationToken cancellationToken = default);

    Task SaveVersionAsync(
        KnowledgeDocument document,
        DocumentVersion version,
        CancellationToken cancellationToken = default);
}
