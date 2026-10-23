using Microsoft.EntityFrameworkCore;
using NexusAgent.Domain.Documents;
using NexusAgent.Infrastructure.Persistence;
using NexusAgent.Infrastructure.Persistence.Repositories;

namespace NexusAgent.IntegrationTests;

public sealed class KnowledgeDocumentPersistenceTests
{
    private const string ConnectionString =
        "Host=127.0.0.1;Port=15432;Database=nexusagent;Username=nexusagent;Password=nexusagent_dev_only";

    private static DbContextOptions<NexusAgentDbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<NexusAgentDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
    }

    [Fact]
    public async Task CreateDocument_PersistsDocumentAndInitialVersion()
    {
        var options = CreateOptions();
        var documentId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using (var db = new NexusAgentDbContext(options))
        {
            await db.Database.MigrateAsync();

            var document = KnowledgeDocument.Create(
                documentId,
                "tenant-a",
                "Architecture Guide",
                "Internal",
                "owner-1",
                ["user-1", "user-2"],
                now);

            var version = DocumentVersion.Capture(
                Guid.NewGuid(),
                document);

            var repository = new KnowledgeDocumentRepository(db);

            await repository.AddAsync(document, version);
        }

        await using (var db = new NexusAgentDbContext(options))
        {
            var document = await db.KnowledgeDocuments
                .SingleAsync(d => d.Id == documentId);

            var version = await db.DocumentVersions
                .SingleAsync(v => v.DocumentId == documentId);

            Assert.Equal("tenant-a", document.TenantId);
            Assert.Equal("Architecture Guide", document.Title);
            Assert.Equal(1, document.Version);
            Assert.Equal(DocumentStatus.Active, document.Status);

            Assert.Equal(1, version.Version);
            Assert.False(version.IsDeleted);
            Assert.Equal(documentId, version.DocumentId);
        }
    }

    [Fact]
    public async Task CrossTenantRead_ReturnsNull()
    {
        var options = CreateOptions();
        var documentId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using var db = new NexusAgentDbContext(options);

        await db.Database.MigrateAsync();

        var document = KnowledgeDocument.Create(
            documentId,
            "tenant-a",
            "Private Document",
            "Confidential",
            "owner-1",
            ["user-1"],
            now);

        var repository = new KnowledgeDocumentRepository(db);

        await repository.AddAsync(
            document,
            DocumentVersion.Capture(Guid.NewGuid(), document));

        var result = await repository.GetAsync(
            "tenant-b",
            documentId);

        Assert.Null(result);

        var authorizedTenantResult = await repository.GetAsync(
            "tenant-a",
            documentId);

        Assert.NotNull(authorizedTenantResult);
    }

    [Fact]
    public async Task UpdateDocument_AppendsVersionWithoutChangingHistory()
    {
        var options = CreateOptions();
        var documentId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using (var db = new NexusAgentDbContext(options))
        {
            await db.Database.MigrateAsync();

            var document = KnowledgeDocument.Create(
                documentId,
                "tenant-a",
                "Original Title",
                "Internal",
                "owner-1",
                ["user-1"],
                now);

            var repository = new KnowledgeDocumentRepository(db);

            await repository.AddAsync(
                document,
                DocumentVersion.Capture(Guid.NewGuid(), document));

            document.Update(
                "tenant-a",
                "Updated Title",
                "Confidential",
                ["user-2"],
                now.AddMinutes(1));

            await repository.SaveVersionAsync(
                document,
                DocumentVersion.Capture(Guid.NewGuid(), document));
        }

        await using (var db = new NexusAgentDbContext(options))
        {
            var versions = await db.DocumentVersions
                .Where(v => v.DocumentId == documentId)
                .OrderBy(v => v.Version)
                .ToListAsync();

            Assert.Equal(2, versions.Count);

            Assert.Equal("Original Title", versions[0].Title);
            Assert.Equal("Internal", versions[0].Classification);
            Assert.Equal(["user-1"], versions[0].AllowedPrincipalIds);

            Assert.Equal("Updated Title", versions[1].Title);
            Assert.Equal("Confidential", versions[1].Classification);
            Assert.Equal(["user-2"], versions[1].AllowedPrincipalIds);

            var current = await db.KnowledgeDocuments
                .SingleAsync(d => d.Id == documentId);

            Assert.Equal(2, current.Version);
        }
    }

    [Fact]
    public async Task DeleteDocument_AppendsTombstoneAndDeniesAccess()
    {
        var options = CreateOptions();
        var documentId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using (var db = new NexusAgentDbContext(options))
        {
            await db.Database.MigrateAsync();

            var document = KnowledgeDocument.Create(
                documentId,
                "tenant-a",
                "Document To Delete",
                "Internal",
                "owner-1",
                ["user-1"],
                now);

            var repository = new KnowledgeDocumentRepository(db);

            await repository.AddAsync(
                document,
                DocumentVersion.Capture(Guid.NewGuid(), document));

            document.Delete("tenant-a", now.AddMinutes(1));

            await repository.SaveVersionAsync(
                document,
                DocumentVersion.Capture(Guid.NewGuid(), document));
        }

        await using (var db = new NexusAgentDbContext(options))
        {
            var document = await db.KnowledgeDocuments
                .SingleAsync(d => d.Id == documentId);

            var versions = await db.DocumentVersions
                .Where(v => v.DocumentId == documentId)
                .OrderBy(v => v.Version)
                .ToListAsync();

            Assert.Equal(DocumentStatus.Deleted, document.Status);
            Assert.Equal(2, document.Version);

            Assert.Equal(2, versions.Count);
            Assert.False(versions[0].IsDeleted);
            Assert.True(versions[1].IsDeleted);

            Assert.False(document.IsAccessibleBy(
                "tenant-a",
                "user-1"));
        }
    }

    [Fact]
    public async Task ConcurrentUpdate_RejectsStaleDocumentVersion()
    {
        var options = CreateOptions();
        var documentId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        // Create version 1.
        await using (var setupDb = new NexusAgentDbContext(options))
        {
            await setupDb.Database.MigrateAsync();

            var document = KnowledgeDocument.Create(
                documentId,
                "tenant-a",
                "Original Title",
                "Internal",
                "owner-1",
                ["user-1"],
                now);

            var setupRepository = new KnowledgeDocumentRepository(setupDb);

            await setupRepository.AddAsync(
                document,
                DocumentVersion.Capture(Guid.NewGuid(), document));
        }

        // Both contexts independently load version 1.
        await using var firstDb = new NexusAgentDbContext(options);
        await using var secondDb = new NexusAgentDbContext(options);

        var firstRepository = new KnowledgeDocumentRepository(firstDb);
        var secondRepository = new KnowledgeDocumentRepository(secondDb);

        var firstDocument = await firstRepository.GetAsync(
            "tenant-a",
            documentId);

        var secondDocument = await secondRepository.GetAsync(
            "tenant-a",
            documentId);

        Assert.NotNull(firstDocument);
        Assert.NotNull(secondDocument);

        Assert.Equal(1, firstDocument.Version);
        Assert.Equal(1, secondDocument.Version);

        // First writer commits version 2.
        firstDocument.Update(
            "tenant-a",
            "First Writer",
            "Internal",
            ["user-1"],
            now.AddMinutes(1));

        await firstRepository.SaveVersionAsync(
            firstDocument,
            DocumentVersion.Capture(Guid.NewGuid(), firstDocument));

        // Second writer attempts to commit stale version 2.
        secondDocument.Update(
            "tenant-a",
            "Second Writer",
            "Internal",
            ["user-1"],
            now.AddMinutes(2));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => secondRepository.SaveVersionAsync(
                secondDocument,
                DocumentVersion.Capture(Guid.NewGuid(), secondDocument)));

        // The first writer's data must remain authoritative.
        await using var verificationDb = new NexusAgentDbContext(options);

        var persisted = await verificationDb.KnowledgeDocuments
            .SingleAsync(document => document.Id == documentId);

        var versions = await verificationDb.DocumentVersions
            .Where(version => version.DocumentId == documentId)
            .OrderBy(version => version.Version)
            .ToListAsync();

        Assert.Equal("First Writer", persisted.Title);
        Assert.Equal(2, persisted.Version);
        Assert.Equal(1, persisted.ContentVersion);

        Assert.Equal(2, versions.Count);
        Assert.Equal("Original Title", versions[0].Title);
        Assert.Equal("First Writer", versions[1].Title);
    }
    [Fact]
    public async Task DuplicateVersion_IsRejectedByPostgreSql()
    {
        var options = CreateOptions();
        var documentId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using var db = new NexusAgentDbContext(options);

        await db.Database.MigrateAsync();

        var document = KnowledgeDocument.Create(
            documentId,
            "tenant-a",
            "Duplicate Version Test",
            "Internal",
            "owner-1",
            ["user-1"],
            now);

        var repository = new KnowledgeDocumentRepository(db);

        await repository.AddAsync(
            document,
            DocumentVersion.Capture(Guid.NewGuid(), document));

        var duplicate = DocumentVersion.Capture(
            Guid.NewGuid(),
            document);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => repository.SaveVersionAsync(document, duplicate));
    }
}
