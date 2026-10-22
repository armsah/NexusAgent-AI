using Microsoft.EntityFrameworkCore;
using NexusAgent.Application.Abstractions;
using NexusAgent.Domain.Documents;

namespace NexusAgent.Infrastructure.Persistence.Repositories;

public sealed class KnowledgeDocumentRepository(
    NexusAgentDbContext dbContext)
    : IKnowledgeDocumentRepository
{
    public Task<KnowledgeDocument?> GetAsync(
        string tenantId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("Tenant ID is required.", nameof(tenantId));

        return dbContext.KnowledgeDocuments
            .SingleOrDefaultAsync(
                document =>
                    document.Id == documentId &&
                    document.TenantId == tenantId,
                cancellationToken);
    }

    public async Task AddAsync(
        KnowledgeDocument document,
        DocumentVersion initialVersion,
        CancellationToken cancellationToken = default)
    {
        ValidateVersion(document, initialVersion);

        await dbContext.KnowledgeDocuments.AddAsync(
            document, cancellationToken);

        await dbContext.DocumentVersions.AddAsync(
            initialVersion, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveVersionAsync(
        KnowledgeDocument document,
        DocumentVersion version,
        CancellationToken cancellationToken = default)
    {
        ValidateVersion(document, version);

        if (dbContext.Entry(document).State == EntityState.Detached)
            throw new InvalidOperationException(
                "Document must be loaded through the tenant-scoped repository.");

        await dbContext.DocumentVersions.AddAsync(
            version, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void ValidateVersion(
        KnowledgeDocument document,
        DocumentVersion version)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(version);

        if (version.DocumentId != document.Id ||
            version.TenantId != document.TenantId ||
            version.Version != document.Version)
        {
            throw new InvalidOperationException(
                "Document version does not match the document aggregate.");
        }
    }
}
