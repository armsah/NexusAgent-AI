using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using NexusAgent.Domain.Documents;
using NexusAgent.Infrastructure.Persistence;
using NexusAgent.Infrastructure.Persistence.Outbox;
using NexusAgent.Infrastructure.Persistence.Search;
using Xunit;

namespace NexusAgent.IntegrationTests;

public sealed class DocumentIndexReliabilityTests
{
    private const string ConnectionString =
        "Host=127.0.0.1;Port=15432;Database=nexusagent;" +
        "Username=nexusagent;Password=nexusagent_dev_only";

    private static DbContextOptions<NexusAgentDbContext> Options() =>
        new DbContextOptionsBuilder<NexusAgentDbContext>()
            .UseNpgsql(ConnectionString, npgsql => npgsql.UseVector())
            .Options;

    [Fact]
    public async Task DeletedDocumentRemovesIndexAndCheckpointsEvent()
    {
        var tenant = "index-test-" + Guid.NewGuid().ToString("N");
        var documentId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using var db = new NexusAgentDbContext(Options());

        var document = KnowledgeDocument.Create(
            documentId, tenant, "Deleted document",
            "internal", "owner", ["owner"], now);

        document.Delete(tenant, now.AddSeconds(1));

        db.KnowledgeDocuments.Add(document);

        db.DocumentSearchIndexes.Add(new DocumentSearchIndex
        {
            DocumentId = documentId,
            TenantId = tenant,
            ContentVersion = document.ContentVersion,
            Content = "Previously indexed confidential content",
            IndexedAt = now
        });

        db.PublishedDocumentEvents.Add(
            PublishedDocumentEvent.Create(
                eventId,
                "DocumentDeleted",
                tenant,
                documentId,
                document.Version,
                "{}",
                now));

        await db.SaveChangesAsync();

        try
        {
            var indexer = new DocumentContentIndexer(db, new MissingContentStore(), new ServiceCollection().BuildServiceProvider());

            var processor = new PublishedDocumentIndexProcessor(
                db,
                indexer);

            var processed = await processor.ProcessAsync();

            Assert.True(processed >= 1);

            Assert.False(await db.DocumentSearchIndexes
                .AnyAsync(x => x.DocumentId == documentId));

            Assert.True(await db.ProcessedDocumentIndexEvents
                .AnyAsync(x => x.EventId == eventId));

            var replay = await processor.ProcessAsync();

            Assert.Equal(0, replay);
        }
        finally
        {
            await db.ProcessedDocumentIndexEvents
                .Where(x => x.EventId == eventId)
                .ExecuteDeleteAsync();

            await db.PublishedDocumentEvents
                .Where(x => x.EventId == eventId)
                .ExecuteDeleteAsync();

            await db.DocumentSearchIndexes
                .Where(x => x.DocumentId == documentId)
                .ExecuteDeleteAsync();

            await db.KnowledgeDocuments
                .Where(x => x.Id == documentId)
                .ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task FailedIndexingDoesNotCheckpointEvent()
    {
        var tenant = "index-failure-" + Guid.NewGuid().ToString("N");
        var documentId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using var db = new NexusAgentDbContext(Options());

        var document = KnowledgeDocument.Create(
            documentId, tenant, "Missing content",
            "internal", "owner", ["owner"], now);

        db.KnowledgeDocuments.Add(document);

        db.PublishedDocumentEvents.Add(
            PublishedDocumentEvent.Create(
                eventId,
                "DocumentCreated",
                tenant,
                documentId,
                document.Version,
                "{}",
                now));

        await db.SaveChangesAsync();

        try
        {
            var indexer = new DocumentContentIndexer(db, new MissingContentStore(), new ServiceCollection().BuildServiceProvider());

            var processor = new PublishedDocumentIndexProcessor(
                db,
                indexer);

            await Assert.ThrowsAsync<FileNotFoundException>(
                () => processor.ProcessAsync());

            Assert.False(await db.ProcessedDocumentIndexEvents
                .AnyAsync(x => x.EventId == eventId));

            Assert.False(await db.DocumentSearchIndexes
                .AnyAsync(x => x.DocumentId == documentId));
        }
        finally
        {
            await db.ProcessedDocumentIndexEvents
                .Where(x => x.EventId == eventId)
                .ExecuteDeleteAsync();

            await db.PublishedDocumentEvents
                .Where(x => x.EventId == eventId)
                .ExecuteDeleteAsync();

            await db.DocumentSearchIndexes
                .Where(x => x.DocumentId == documentId)
                .ExecuteDeleteAsync();

            await db.KnowledgeDocuments
                .Where(x => x.Id == documentId)
                .ExecuteDeleteAsync();
        }
    }

    private sealed class MissingContentStore
        : NexusAgent.Application.Documents.IDocumentContentStore
    {
        public Task<NexusAgent.Application.Documents.StoredDocumentContent>
            PutAsync(
                string tenantId,
                Guid documentId,
                int version,
                Stream content,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Stream> OpenReadAsync(
            string tenantId,
            Guid documentId,
            int version,
            CancellationToken cancellationToken = default) =>
            throw new FileNotFoundException(
                "Simulated missing document content.");

        public Task DeleteAsync(
            string tenantId,
            Guid documentId,
            int version,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
