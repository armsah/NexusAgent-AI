using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NexusAgent.Application.Documents;
using NexusAgent.Application.Search;
using NexusAgent.Domain.Documents;
using NexusAgent.Infrastructure.Persistence;
using NexusAgent.Infrastructure.Persistence.Search;
using Pgvector;
using Xunit;

namespace NexusAgent.IntegrationTests;

public sealed class DocumentVectorIndexTests
{
    private const string ConnectionString =
        "Host=127.0.0.1;Port=15432;Database=nexusagent;" +
        "Username=nexusagent;Password=nexusagent_dev_only";

    private static DbContextOptions<NexusAgentDbContext> Options() =>
        new DbContextOptionsBuilder<NexusAgentDbContext>()
            .UseNpgsql(ConnectionString, npgsql => npgsql.UseVector())
            .Options;

    [Fact]
    public async Task IndexAsync_PersistsTextAndVectorWithMatchingVersions()
    {
        var tenant = "vector-test-" + Guid.NewGuid().ToString("N");
        var documentId = Guid.NewGuid();

        await using var db = new NexusAgentDbContext(Options());

        try
        {
            await InsertDocumentAsync(db, tenant, documentId);

            var provider = new FakeEmbeddingProvider();
            var indexer = CreateIndexer(
                db,
                new InMemoryContentStore("Secure retrieval architecture"),
                provider);

            await using (var transaction =
                await db.Database.BeginTransactionAsync())
            {
                await indexer.IndexAsync(tenant, documentId);
                await transaction.CommitAsync();
            }

            db.ChangeTracker.Clear();

            var text = await db.DocumentSearchIndexes
                .AsNoTracking()
                .SingleAsync(x => x.DocumentId == documentId);

            var vector = await db.DocumentVectorIndexes
                .AsNoTracking()
                .SingleAsync(x => x.DocumentId == documentId);

            Assert.Equal("Secure retrieval architecture", text.Content);
            Assert.Equal(text.ContentVersion, vector.ContentVersion);
            Assert.Equal(tenant, vector.TenantId);
            Assert.Equal(provider.ModelId, vector.EmbeddingModel);
            Assert.Equal(1536, vector.Embedding.ToArray().Length);
            Assert.Equal(1f, vector.Embedding.ToArray()[0]);
        }
        finally
        {
            await CleanupAsync(db, documentId);
        }
    }

    [Fact]
    public async Task IndexAsync_EmbeddingFailureDoesNotPersistEitherIndex()
    {
        var tenant = "vector-failure-" + Guid.NewGuid().ToString("N");
        var documentId = Guid.NewGuid();

        await using var db = new NexusAgentDbContext(Options());

        try
        {
            await InsertDocumentAsync(db, tenant, documentId);

            var indexer = CreateIndexer(
                db,
                new InMemoryContentStore("Failure simulation"),
                new FakeEmbeddingProvider(shouldFail: true));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => indexer.IndexAsync(tenant, documentId));

            db.ChangeTracker.Clear();

            Assert.False(await db.DocumentSearchIndexes
                .AnyAsync(x => x.DocumentId == documentId));

            Assert.False(await db.DocumentVectorIndexes
                .AnyAsync(x => x.DocumentId == documentId));
        }
        finally
        {
            await CleanupAsync(db, documentId);
        }
    }

    [Fact]
    public async Task IndexAsync_TransactionRollbackRemovesBothIndexes()
    {
        var tenant = "vector-rollback-" + Guid.NewGuid().ToString("N");
        var documentId = Guid.NewGuid();

        await using var db = new NexusAgentDbContext(Options());

        try
        {
            await InsertDocumentAsync(db, tenant, documentId);

            var indexer = CreateIndexer(
                db,
                new InMemoryContentStore("Rollback simulation"),
                new FakeEmbeddingProvider());

            await using (var transaction =
                await db.Database.BeginTransactionAsync())
            {
                await indexer.IndexAsync(tenant, documentId);

                Assert.True(await db.DocumentSearchIndexes
                    .AnyAsync(x => x.DocumentId == documentId));

                Assert.True(await db.DocumentVectorIndexes
                    .AnyAsync(x => x.DocumentId == documentId));

                await transaction.RollbackAsync();
            }

            db.ChangeTracker.Clear();

            Assert.False(await db.DocumentSearchIndexes
                .AnyAsync(x => x.DocumentId == documentId));

            Assert.False(await db.DocumentVectorIndexes
                .AnyAsync(x => x.DocumentId == documentId));
        }
        finally
        {
            await CleanupAsync(db, documentId);
        }
    }

    [Fact]
    public async Task IndexAsync_DeletedDocumentRemovesBothIndexes()
    {
        var tenant = "vector-delete-" + Guid.NewGuid().ToString("N");
        var documentId = Guid.NewGuid();

        await using var db = new NexusAgentDbContext(Options());

        try
        {
            await InsertDocumentAsync(db, tenant, documentId);

            var indexer = CreateIndexer(
                db,
                new InMemoryContentStore("Document to delete"),
                new FakeEmbeddingProvider());

            await using (var transaction =
                await db.Database.BeginTransactionAsync())
            {
                await indexer.IndexAsync(tenant, documentId);
                await transaction.CommitAsync();
            }

            db.ChangeTracker.Clear();

            var document = await db.KnowledgeDocuments
                .SingleAsync(x => x.Id == documentId);

            document.Delete(tenant, DateTimeOffset.UtcNow.AddSeconds(1));
            await db.SaveChangesAsync();

            db.ChangeTracker.Clear();

            await indexer.IndexAsync(tenant, documentId);

            Assert.False(await db.DocumentSearchIndexes
                .AnyAsync(x => x.DocumentId == documentId));

            Assert.False(await db.DocumentVectorIndexes
                .AnyAsync(x => x.DocumentId == documentId));
        }
        finally
        {
            await CleanupAsync(db, documentId);
        }
    }

    private static DocumentContentIndexer CreateIndexer(
        NexusAgentDbContext db,
        IDocumentContentStore contentStore,
        ITextEmbeddingProvider provider)
    {
        var services = new ServiceCollection()
            .AddSingleton<ITextEmbeddingProvider>(provider)
            .BuildServiceProvider();

        return new DocumentContentIndexer(db, contentStore, services);
    }

    private static async Task InsertDocumentAsync(
        NexusAgentDbContext db,
        string tenant,
        Guid documentId)
    {
        var document = KnowledgeDocument.Create(
            documentId,
            tenant,
            "Vector indexing test",
            "internal",
            "owner",
            ["owner"],
            DateTimeOffset.UtcNow);

        db.KnowledgeDocuments.Add(document);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static async Task CleanupAsync(
        NexusAgentDbContext db,
        Guid documentId)
    {
        db.ChangeTracker.Clear();

        await db.DocumentVectorIndexes
            .Where(x => x.DocumentId == documentId)
            .ExecuteDeleteAsync();

        await db.DocumentSearchIndexes
            .Where(x => x.DocumentId == documentId)
            .ExecuteDeleteAsync();

        await db.KnowledgeDocuments
            .Where(x => x.Id == documentId)
            .ExecuteDeleteAsync();
    }

    private sealed class FakeEmbeddingProvider(bool shouldFail = false)
        : ITextEmbeddingProvider
    {
        public string ModelId => "test-embedding-1536";

        public int Dimensions => 1536;

        public Task<float[]> EmbedAsync(
            string text,
            CancellationToken cancellationToken = default)
        {
            if (shouldFail)
                throw new InvalidOperationException(
                    "Simulated embedding provider failure.");

            var embedding = new float[1536];
            embedding[0] = 1f;

            return Task.FromResult(embedding);
        }
    }

    private sealed class InMemoryContentStore(string text)
        : IDocumentContentStore
    {
        public Task<StoredDocumentContent> PutAsync(
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
            Task.FromResult<Stream>(
                new MemoryStream(Encoding.UTF8.GetBytes(text)));

        public Task DeleteAsync(
            string tenantId,
            Guid documentId,
            int version,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}