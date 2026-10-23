using Microsoft.EntityFrameworkCore;
using Npgsql;
using NexusAgent.Application.Abstractions;
using NexusAgent.Application.Documents;
using NexusAgent.Infrastructure.Persistence.Outbox;
using System.Text.Json;
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

        var ingestionEvent = new DocumentIngestionEvent(
            Guid.NewGuid(),
            DocumentIngestionEventTypes.Created,
            document.TenantId,
            document.Id,
            document.Version,
            document.ContentVersion,
            document.UpdatedAt);

        var outboxMessage = OutboxMessage.Create(
            ingestionEvent.EventId,
            ingestionEvent.TenantId,
            ingestionEvent.EventType,
            ingestionEvent.DocumentId,
            ingestionEvent.DocumentVersion,
            JsonSerializer.Serialize(ingestionEvent),
            ingestionEvent.OccurredAt);

        await dbContext.KnowledgeDocuments.AddAsync(
            document, cancellationToken);

        await dbContext.DocumentVersions.AddAsync(
            initialVersion, cancellationToken);

        await dbContext.OutboxMessages.AddAsync(
            outboxMessage, cancellationToken);

        // EF Core commits all three inserts atomically.
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

        var eventType = version.IsDeleted
            ? DocumentIngestionEventTypes.Deleted
            : DocumentIngestionEventTypes.Updated;

        var ingestionEvent = new DocumentIngestionEvent(
            Guid.NewGuid(),
            eventType,
            document.TenantId,
            document.Id,
            document.Version,
            document.ContentVersion,
            document.UpdatedAt);

        var outboxMessage = OutboxMessage.Create(
            ingestionEvent.EventId,
            ingestionEvent.TenantId,
            ingestionEvent.EventType,
            ingestionEvent.DocumentId,
            ingestionEvent.DocumentVersion,
            JsonSerializer.Serialize(ingestionEvent),
            ingestionEvent.OccurredAt);

        await dbContext.DocumentVersions.AddAsync(
            version, cancellationToken);

        await dbContext.OutboxMessages.AddAsync(
            outboxMessage, cancellationToken);

        // Document update, version snapshot, and event commit together.
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException postgres &&
                  postgres.SqlState == PostgresErrorCodes.UniqueViolation &&
                  postgres.ConstraintName == "IX_document_versions_document_id_version")
        {
            throw new DbUpdateConcurrencyException(
                "The document was modified by another request.",
                exception);
        }
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
