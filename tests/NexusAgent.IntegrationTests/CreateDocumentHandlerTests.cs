using System.Text;
using Microsoft.EntityFrameworkCore;
using NexusAgent.Application.Abstractions;
using NexusAgent.Application.Documents;
using NexusAgent.Domain.Common;
using NexusAgent.Infrastructure.Persistence;
using NexusAgent.Infrastructure.Persistence.Repositories;
using NexusAgent.Infrastructure.Storage;

namespace NexusAgent.IntegrationTests;

public sealed class CreateDocumentHandlerTests
{
    private const string ConnectionString =
        "Host=127.0.0.1;Port=15432;Database=nexusagent;" +
        "Username=nexusagent;Password=nexusagent_dev_only";

    [Fact]
    public async Task CreateDocument_PersistsMetadataAndContent()
    {
        var root = NewStorageRoot();

        try
        {
            await using var db = CreateDbContext();
            await db.Database.MigrateAsync();

            var store = new LocalDocumentContentStore(root);
            var handler = CreateHandler(
                new KnowledgeDocumentRepository(db),
                store);

            var content = Encoding.UTF8.GetBytes("NexusAgent ingestion test");

            using var stream = new MemoryStream(content);

            var result = await handler.HandleAsync(
                NewCommand(stream));

            Assert.Equal(1, result.Version);
            Assert.Equal(content.Length, result.Length);
            Assert.Equal(64, result.Sha256.Length);

            var document = await db.KnowledgeDocuments
                .AsNoTracking()
                .SingleAsync(x => x.Id == result.DocumentId);

            Assert.Equal("tenant-ingestion-test", document.TenantId);
            Assert.Equal("Ingestion Test", document.Title);

            var version = await db.DocumentVersions
                .AsNoTracking()
                .SingleAsync(x => x.DocumentId == result.DocumentId);

            Assert.Equal(1, version.Version);

            await using var storedStream = await store.OpenReadAsync(
                document.TenantId,
                document.Id,
                document.Version);

            using var output = new MemoryStream();
            await storedStream.CopyToAsync(output);

            Assert.Equal(content, output.ToArray());
        }
        finally
        {
            DeleteStorageRoot(root);
        }
    }

    [Fact]
    public async Task CreateDocument_InvalidMetadata_DoesNotWriteContent()
    {
        var root = NewStorageRoot();

        try
        {
            await using var db = CreateDbContext();
            var store = new LocalDocumentContentStore(root);
            var handler = CreateHandler(
                new KnowledgeDocumentRepository(db),
                store);

            using var stream = new MemoryStream([1, 2, 3]);

            var command = NewCommand(stream) with
            {
                Title = " "
            };

            await Assert.ThrowsAsync<DomainException>(
                () => handler.HandleAsync(command));

            Assert.False(Directory.Exists(root));
        }
        finally
        {
            DeleteStorageRoot(root);
        }
    }

    [Fact]
    public async Task CreateDocument_DatabaseFailure_DeletesStoredContent()
    {
        var root = NewStorageRoot();

        try
        {
            // No migration: intentionally point at a nonexistent database.
            var options = new DbContextOptionsBuilder<NexusAgentDbContext>()
                .UseNpgsql(
                    "Host=127.0.0.1;Port=15432;" +
                    "Database=nexusagent_missing_ingestion_test;" +
                    "Username=nexusagent;Password=nexusagent_dev_only")
                .Options;

            await using var db = new NexusAgentDbContext(options);

            var store = new LocalDocumentContentStore(root);
            var handler = CreateHandler(
                new KnowledgeDocumentRepository(db),
                store);

            using var stream = new MemoryStream([4, 5, 6]);

            await Assert.ThrowsAnyAsync<Exception>(
                () => handler.HandleAsync(NewCommand(stream)));

            var storedFiles = Directory.Exists(root)
                ? Directory.GetFiles(root, "*.bin", SearchOption.AllDirectories)
                : [];

            Assert.Empty(storedFiles);
        }
        finally
        {
            DeleteStorageRoot(root);
        }
    }

    private static CreateDocumentCommand NewCommand(Stream content) =>
        new(
            TenantId: "tenant-ingestion-test",
            Title: "Ingestion Test",
            Classification: "internal",
            OwnerId: "owner-ingestion-test",
            AllowedPrincipalIds: ["owner-ingestion-test"],
            Content: content);

    private static NexusAgentDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<NexusAgentDbContext>()
            .UseNpgsql(ConnectionString, npgsql => npgsql.UseVector())
            .Options;

        return new NexusAgentDbContext(options);
    }

    private static CreateDocumentHandler CreateHandler(
        IKnowledgeDocumentRepository repository,
        IDocumentContentStore store) =>
        new(
            repository,
            store,
            new FixedClock(),
            new TestIdGenerator());

    private static string NewStorageRoot() =>
        Path.Combine(
            Path.GetTempPath(),
            "nexusagent-ingestion-tests",
            Guid.NewGuid().ToString("N"));

    private static void DeleteStorageRoot(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow =>
            new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class TestIdGenerator : IIdGenerator
    {
        public Guid NewId() => Guid.NewGuid();
    }
}
