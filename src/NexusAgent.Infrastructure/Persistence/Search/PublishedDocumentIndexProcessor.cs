using Microsoft.EntityFrameworkCore;
using NexusAgent.Domain.Documents;

namespace NexusAgent.Infrastructure.Persistence.Search;

public sealed class PublishedDocumentIndexProcessor(
    NexusAgentDbContext db,
    DocumentContentIndexer indexer)
{
    public async Task<int> ProcessAsync(
        int batchSize = 25,
        CancellationToken cancellationToken = default)
    {
        if (batchSize is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(batchSize));

        await db.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            var acquired = await db.Database
                .SqlQueryRaw<bool>(
                    "SELECT pg_try_advisory_lock(7142026) AS \"Value\"")
                .SingleAsync(cancellationToken);

            if (!acquired)
                return 0;

            try
            {
                var events = await db.PublishedDocumentEvents
                    .AsNoTracking()
                    .Where(e => !db.ProcessedDocumentIndexEvents
                        .Any(p => p.EventId == e.EventId))
                    .OrderBy(e => e.PublishedAt)
                    .ThenBy(e => e.EventId)
                    .Take(batchSize)
                    .ToListAsync(cancellationToken);

                var processed = 0;

                foreach (var publishedEvent in events)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    await using var transaction =
                        await db.Database.BeginTransactionAsync(
                            cancellationToken);

                    var document = await db.KnowledgeDocuments
                        .AsNoTracking()
                        .SingleOrDefaultAsync(
                            x => x.Id == publishedEvent.DocumentId &&
                                 x.TenantId == publishedEvent.TenantId,
                            cancellationToken);

                    if (document is not null)
                    {
                        if (document.Status == DocumentStatus.Deleted)
                        {
                            await db.DocumentSearchIndexes
                                .Where(x => x.DocumentId == document.Id &&
                                            x.TenantId == document.TenantId)
                                .ExecuteDeleteAsync(cancellationToken);

                            await db.DocumentVectorIndexes
                                .Where(x => x.DocumentId == document.Id &&
                                            x.TenantId == document.TenantId)
                                .ExecuteDeleteAsync(cancellationToken);
                        }
                        else
                        {
                            await indexer.IndexAsync(
                                document.TenantId,
                                document.Id,
                                cancellationToken);
                        }
                    }

                    db.ProcessedDocumentIndexEvents.Add(
                        new ProcessedDocumentIndexEvent
                        {
                            EventId = publishedEvent.EventId,
                            ProcessedAt = DateTimeOffset.UtcNow
                        });

                    await db.SaveChangesAsync(cancellationToken);

                    await transaction.CommitAsync(cancellationToken);

                    processed++;
                }

                return processed;
            }
            finally
            {
                await db.Database.ExecuteSqlRawAsync(
                    "SELECT pg_advisory_unlock(7142026)",
                    CancellationToken.None);
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}