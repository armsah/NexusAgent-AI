using System.Text;
using Microsoft.EntityFrameworkCore;
using NexusAgent.Application.Documents;
using NexusAgent.Application.Search;
using NexusAgent.Domain.Documents;
using Pgvector;

namespace NexusAgent.Infrastructure.Persistence.Search;

public sealed class DocumentContentIndexer(
    NexusAgentDbContext db,
    IDocumentContentStore contentStore,
    IServiceProvider services)
{
    public async Task IndexAsync(
        string tenantId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var document = await db.KnowledgeDocuments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == documentId && x.TenantId == tenantId,
                cancellationToken);

        if (document is null || document.Status != DocumentStatus.Active)
        {
            await DeleteIndexesAsync(tenantId, documentId, cancellationToken);
            return;
        }

        var contentVersion = document.ContentVersion;

        await using var stream = await contentStore.OpenReadAsync(
            tenantId,
            documentId,
            contentVersion,
            cancellationToken);

        var prefix = new byte[8];
        var prefixLength = await stream.ReadAsync(
            prefix.AsMemory(),
            cancellationToken);

        if (prefixLength >= 5 &&
            prefix.AsSpan(0, 5).SequenceEqual("%PDF-"u8))
        {
            await DeleteIndexesAsync(tenantId, documentId, cancellationToken);
            return;
        }

        if (!stream.CanSeek)
            throw new InvalidOperationException(
                "Content indexing requires a seekable stream.");

        stream.Position = 0;

        using var reader = new StreamReader(
            stream,
            new UTF8Encoding(false, true),
            detectEncodingFromByteOrderMarks: true);

        string content;

        try
        {
            content = await reader.ReadToEndAsync(cancellationToken);
        }
        catch (DecoderFallbackException)
        {
            await DeleteIndexesAsync(tenantId, documentId, cancellationToken);
            return;
        }

        var embeddingProvider =
            services.GetService(typeof(ITextEmbeddingProvider))
                as ITextEmbeddingProvider;

        float[]? embedding = null;

        if (embeddingProvider is not null &&
            !string.IsNullOrWhiteSpace(content))
        {
            embedding = await embeddingProvider.EmbedAsync(
                content,
                cancellationToken);

            EmbeddingValidation.Validate(embeddingProvider, embedding);
        }

        // The processor owns the surrounding transaction.
        // Lock the source document before persisting either index.
        var versions = await db.Database
            .SqlQueryRaw<int>(
                """
                SELECT content_version AS "Value"
                FROM knowledge_documents
                WHERE id = {0}
                  AND tenant_id = {1}
                  AND status = 'Active'
                FOR UPDATE
                """,
                documentId,
                tenantId)
            .ToListAsync(cancellationToken);

        if (versions.Count != 1 || versions[0] != contentVersion)
            return;

        var existingText = await db.DocumentSearchIndexes
            .SingleOrDefaultAsync(
                x => x.DocumentId == documentId &&
                     x.TenantId == tenantId,
                cancellationToken);

        if (existingText is null)
        {
            db.DocumentSearchIndexes.Add(new DocumentSearchIndex
            {
                DocumentId = documentId,
                TenantId = tenantId,
                ContentVersion = contentVersion,
                Content = content,
                IndexedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            existingText.ContentVersion = contentVersion;
            existingText.Content = content;
            existingText.IndexedAt = DateTimeOffset.UtcNow;
        }

        if (embeddingProvider is not null)
        {
            var existingVector = await db.DocumentVectorIndexes
                .SingleOrDefaultAsync(
                    x => x.DocumentId == documentId &&
                         x.TenantId == tenantId,
                    cancellationToken);

            if (embedding is null)
            {
                if (existingVector is not null)
                    db.DocumentVectorIndexes.Remove(existingVector);
            }
            else if (existingVector is null)
            {
                db.DocumentVectorIndexes.Add(new DocumentVectorIndex
                {
                    DocumentId = documentId,
                    TenantId = tenantId,
                    ContentVersion = contentVersion,
                    EmbeddingModel = embeddingProvider.ModelId,
                    Embedding = new Vector(embedding),
                    IndexedAt = DateTimeOffset.UtcNow
                });
            }
            else
            {
                existingVector.ContentVersion = contentVersion;
                existingVector.EmbeddingModel = embeddingProvider.ModelId;
                existingVector.Embedding = new Vector(embedding);
                existingVector.IndexedAt = DateTimeOffset.UtcNow;
            }
        }
        else
        {
            // Never retain vectors from an earlier provider configuration.
            await db.DocumentVectorIndexes
                .Where(x => x.DocumentId == documentId &&
                            x.TenantId == tenantId)
                .ExecuteDeleteAsync(cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task DeleteIndexesAsync(
        string tenantId,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        await db.DocumentSearchIndexes
            .Where(x => x.DocumentId == documentId &&
                        x.TenantId == tenantId)
            .ExecuteDeleteAsync(cancellationToken);

        await db.DocumentVectorIndexes
            .Where(x => x.DocumentId == documentId &&
                        x.TenantId == tenantId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}