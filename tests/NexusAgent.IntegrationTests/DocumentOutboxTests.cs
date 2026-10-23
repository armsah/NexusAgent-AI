using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NexusAgent.Application.Documents;
using NexusAgent.Domain.Documents;
using NexusAgent.Infrastructure.Persistence;
using NexusAgent.Infrastructure.Persistence.Repositories;

namespace NexusAgent.IntegrationTests;

public sealed class DocumentOutboxTests
{
    private const string ConnectionString =
        "Host=127.0.0.1;Port=15432;Database=nexusagent;" +
        "Username=nexusagent;Password=nexusagent_dev_only";

    [Fact]
    public async Task UpdateAndDelete_PersistOrderedOutboxEvents()
    {
        var documentId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var options = new DbContextOptionsBuilder<NexusAgentDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        await using (var db = new NexusAgentDbContext(options))
        {
            await db.Database.MigrateAsync();

            var document = KnowledgeDocument.Create(
                documentId,
                "tenant-outbox-lifecycle",
                "Original Title",
                "Internal",
                "owner-outbox-test",
                ["owner-outbox-test"],
                now);

            var repository = new KnowledgeDocumentRepository(db);

            await repository.AddAsync(
                document,
                DocumentVersion.Capture(Guid.NewGuid(), document));

            document.Update(
                document.TenantId,
                "Updated Title",
                "Confidential",
                ["owner-outbox-test"],
                now.AddMinutes(1));

            await repository.SaveVersionAsync(
                document,
                DocumentVersion.Capture(Guid.NewGuid(), document));

            document.Delete(
                document.TenantId,
                now.AddMinutes(2));

            await repository.SaveVersionAsync(
                document,
                DocumentVersion.Capture(Guid.NewGuid(), document));
        }

        await using (var db = new NexusAgentDbContext(options))
        {
            var document = await db.KnowledgeDocuments
                .AsNoTracking()
                .SingleAsync(x => x.Id == documentId);

            var versions = await db.DocumentVersions
                .AsNoTracking()
                .Where(x => x.DocumentId == documentId)
                .OrderBy(x => x.Version)
                .ToListAsync();

            var messages = await db.OutboxMessages
                .AsNoTracking()
                .Where(x => x.DocumentId == documentId)
                .OrderBy(x => x.DocumentVersion)
                .ToListAsync();

            Assert.Equal(3, document.Version);
            Assert.Equal(1, document.ContentVersion);

            Assert.Equal(
                [1, 2, 3],
                versions.Select(x => x.Version).ToArray());

            Assert.Equal(
                [false, false, true],
                versions.Select(x => x.IsDeleted).ToArray());

            Assert.Equal(3, messages.Count);

            Assert.Equal(
                [
                    DocumentIngestionEventTypes.Created,
                    DocumentIngestionEventTypes.Updated,
                    DocumentIngestionEventTypes.Deleted
                ],
                messages.Select(x => x.EventType).ToArray());

            Assert.Equal(
                [1, 2, 3],
                messages.Select(x => x.DocumentVersion).ToArray());

            foreach (var message in messages)
            {
                Assert.Equal(
                    "tenant-outbox-lifecycle",
                    message.TenantId);

                Assert.Equal(documentId, message.DocumentId);
                Assert.Null(message.ProcessedAt);
                Assert.Equal(0, message.RetryCount);
                Assert.Null(message.LastError);

                var payload =
                    JsonSerializer.Deserialize<DocumentIngestionEvent>(
                        message.Payload);

                Assert.NotNull(payload);
                Assert.Equal(message.Id, payload.EventId);
                Assert.Equal(message.EventType, payload.EventType);
                Assert.Equal(message.TenantId, payload.TenantId);
                Assert.Equal(message.DocumentId, payload.DocumentId);
                Assert.Equal(
                    message.DocumentVersion,
                    payload.DocumentVersion);

                Assert.Equal(1, payload.ContentVersion);
            }
        }
    }
    [Fact]
    public async Task ContentReplacement_PersistsUpdatedEventWithNewContentVersion()
    {
        var documentId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var options = new DbContextOptionsBuilder<NexusAgentDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        await using (var db = new NexusAgentDbContext(options))
        {
            await db.Database.MigrateAsync();

            var document = KnowledgeDocument.Create(
                documentId,
                "tenant-outbox-content",
                "Original Content",
                "Internal",
                "owner-outbox-test",
                ["owner-outbox-test"],
                now);

            var repository = new KnowledgeDocumentRepository(db);

            await repository.AddAsync(
                document,
                DocumentVersion.Capture(Guid.NewGuid(), document));

            document.Update(
                document.TenantId,
                "Updated Metadata",
                "Internal",
                ["owner-outbox-test"],
                now.AddMinutes(1));

            await repository.SaveVersionAsync(
                document,
                DocumentVersion.Capture(Guid.NewGuid(), document));

            document.UpdateContent(
                document.TenantId,
                now.AddMinutes(2));

            await repository.SaveVersionAsync(
                document,
                DocumentVersion.Capture(Guid.NewGuid(), document));
        }

        await using (var db = new NexusAgentDbContext(options))
        {
            var document = await db.KnowledgeDocuments
                .AsNoTracking()
                .SingleAsync(x => x.Id == documentId);

            var versions = await db.DocumentVersions
                .AsNoTracking()
                .Where(x => x.DocumentId == documentId)
                .OrderBy(x => x.Version)
                .ToListAsync();

            var messages = await db.OutboxMessages
                .AsNoTracking()
                .Where(x => x.DocumentId == documentId)
                .OrderBy(x => x.DocumentVersion)
                .ToListAsync();

            Assert.Equal(3, document.Version);
            Assert.Equal(2, document.ContentVersion);

            Assert.Equal(
                [1, 2, 3],
                versions.Select(x => x.Version).ToArray());

            Assert.Equal(
                [1, 1, 2],
                versions.Select(x => x.ContentVersion).ToArray());

            Assert.Equal(3, messages.Count);

            Assert.Equal(
                [
                    DocumentIngestionEventTypes.Created,
                    DocumentIngestionEventTypes.Updated,
                    DocumentIngestionEventTypes.Updated
                ],
                messages.Select(x => x.EventType).ToArray());

            Assert.Equal(
                [1, 2, 3],
                messages.Select(x => x.DocumentVersion).ToArray());

            var contentUpdateEvent =
                JsonSerializer.Deserialize<DocumentIngestionEvent>(
                    messages[2].Payload);

            Assert.NotNull(contentUpdateEvent);
            Assert.Equal(documentId, contentUpdateEvent.DocumentId);
            Assert.Equal(3, contentUpdateEvent.DocumentVersion);
            Assert.Equal(2, contentUpdateEvent.ContentVersion);
            Assert.Equal(
                DocumentIngestionEventTypes.Updated,
                contentUpdateEvent.EventType);

            Assert.All(messages, message =>
            {
                Assert.Null(message.ProcessedAt);
                Assert.Equal(0, message.RetryCount);
                Assert.Null(message.LastError);
            });
        }
    }
    [Fact]
    public async Task FailedDuplicateCreate_DoesNotCommitOutboxEvent()
    {
        var documentId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var options = new DbContextOptionsBuilder<NexusAgentDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        await using (var db = new NexusAgentDbContext(options))
        {
            await db.Database.MigrateAsync();

            var document = KnowledgeDocument.Create(
                documentId,
                "tenant-outbox-rollback",
                "Original Document",
                "Internal",
                "owner-outbox-test",
                ["owner-outbox-test"],
                now);

            var repository = new KnowledgeDocumentRepository(db);

            await repository.AddAsync(
                document,
                DocumentVersion.Capture(Guid.NewGuid(), document));
        }

        await using (var db = new NexusAgentDbContext(options))
        {
            var duplicateDocument = KnowledgeDocument.Create(
                documentId,
                "tenant-outbox-rollback",
                "Duplicate Document",
                "Confidential",
                "owner-outbox-test",
                ["owner-outbox-test"],
                now.AddMinutes(1));

            var repository = new KnowledgeDocumentRepository(db);

            await Assert.ThrowsAsync<DbUpdateException>(
                () => repository.AddAsync(
                    duplicateDocument,
                    DocumentVersion.Capture(
                        Guid.NewGuid(),
                        duplicateDocument)));
        }

        await using (var db = new NexusAgentDbContext(options))
        {
            var documents = await db.KnowledgeDocuments
                .AsNoTracking()
                .Where(x => x.Id == documentId)
                .ToListAsync();

            var versions = await db.DocumentVersions
                .AsNoTracking()
                .Where(x => x.DocumentId == documentId)
                .ToListAsync();

            var messages = await db.OutboxMessages
                .AsNoTracking()
                .Where(x => x.DocumentId == documentId)
                .ToListAsync();

            var document = Assert.Single(documents);
            var version = Assert.Single(versions);
            var message = Assert.Single(messages);

            Assert.Equal("Original Document", document.Title);
            Assert.Equal(1, document.Version);

            Assert.Equal("Original Document", version.Title);
            Assert.Equal(1, version.Version);

            Assert.Equal(
                DocumentIngestionEventTypes.Created,
                message.EventType);

            Assert.Equal(1, message.DocumentVersion);

            var payload =
                JsonSerializer.Deserialize<DocumentIngestionEvent>(
                    message.Payload);

            Assert.NotNull(payload);
            Assert.Equal(documentId, payload.DocumentId);
            Assert.Equal(1, payload.DocumentVersion);
        }
    }
    [Fact]
    public async Task ConcurrentUpdates_OnlyWinnerCommitsOutboxEvent()
    {
        var documentId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var options = new DbContextOptionsBuilder<NexusAgentDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        await using (var setupDb = new NexusAgentDbContext(options))
        {
            await setupDb.Database.MigrateAsync();

            var document = KnowledgeDocument.Create(
                documentId,
                "tenant-outbox-concurrency",
                "Original Title",
                "Internal",
                "owner-outbox-test",
                ["owner-outbox-test"],
                now);

            var repository = new KnowledgeDocumentRepository(setupDb);

            await repository.AddAsync(
                document,
                DocumentVersion.Capture(Guid.NewGuid(), document));
        }

        await using var firstDb = new NexusAgentDbContext(options);
        await using var secondDb = new NexusAgentDbContext(options);

        var firstRepository = new KnowledgeDocumentRepository(firstDb);
        var secondRepository = new KnowledgeDocumentRepository(secondDb);

        // Both contexts read document version 1 before either writes.
        var firstDocument = await firstRepository.GetAsync(
            "tenant-outbox-concurrency",
            documentId);

        var secondDocument = await secondRepository.GetAsync(
            "tenant-outbox-concurrency",
            documentId);

        Assert.NotNull(firstDocument);
        Assert.NotNull(secondDocument);

        Assert.Equal(1, firstDocument.Version);
        Assert.Equal(1, secondDocument.Version);

        firstDocument.Update(
            firstDocument.TenantId,
            "Winning Update",
            "Internal",
            ["owner-outbox-test"],
            now.AddMinutes(1));

        secondDocument.Update(
            secondDocument.TenantId,
            "Rejected Update",
            "Confidential",
            ["owner-outbox-test"],
            now.AddMinutes(2));

        // First writer commits successfully.
        await firstRepository.SaveVersionAsync(
            firstDocument,
            DocumentVersion.Capture(Guid.NewGuid(), firstDocument));

        // Second writer still holds stale version 1.
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => secondRepository.SaveVersionAsync(
                secondDocument,
                DocumentVersion.Capture(
                    Guid.NewGuid(),
                    secondDocument)));

        await using var verificationDb =
            new NexusAgentDbContext(options);

        var persistedDocument = await verificationDb.KnowledgeDocuments
            .AsNoTracking()
            .SingleAsync(x => x.Id == documentId);

        var versions = await verificationDb.DocumentVersions
            .AsNoTracking()
            .Where(x => x.DocumentId == documentId)
            .OrderBy(x => x.Version)
            .ToListAsync();

        var messages = await verificationDb.OutboxMessages
            .AsNoTracking()
            .Where(x => x.DocumentId == documentId)
            .OrderBy(x => x.DocumentVersion)
            .ToListAsync();

        Assert.Equal(2, persistedDocument.Version);
        Assert.Equal("Winning Update", persistedDocument.Title);

        Assert.Equal(
            [1, 2],
            versions.Select(x => x.Version).ToArray());

        Assert.Equal(2, messages.Count);

        Assert.Equal(
            [
                DocumentIngestionEventTypes.Created,
                DocumentIngestionEventTypes.Updated
            ],
            messages.Select(x => x.EventType).ToArray());

        Assert.Equal(
            [1, 2],
            messages.Select(x => x.DocumentVersion).ToArray());

        var updateEvent =
            JsonSerializer.Deserialize<DocumentIngestionEvent>(
                messages[1].Payload);

        Assert.NotNull(updateEvent);
        Assert.Equal(documentId, updateEvent.DocumentId);
        Assert.Equal(2, updateEvent.DocumentVersion);
        Assert.Equal(1, updateEvent.ContentVersion);

        Assert.All(messages, message =>
        {
            Assert.Null(message.ProcessedAt);
            Assert.Equal(0, message.RetryCount);
        });
    }
    [Fact]
    public async Task PendingReader_FiltersOrdersAndLimitsMessages()
    {
        var tenantId = "tenant-outbox-reader-" +
            Guid.NewGuid().ToString("N");

        var now = DateTimeOffset.UtcNow.AddMinutes(-10);

        var options = new DbContextOptionsBuilder<NexusAgentDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        var first = NexusAgent.Infrastructure.Persistence.Outbox.OutboxMessage.Create(
            Guid.NewGuid(),
            tenantId,
            DocumentIngestionEventTypes.Created,
            Guid.NewGuid(),
            1,
            "{\"sequence\":1}",
            now);

        var second = NexusAgent.Infrastructure.Persistence.Outbox.OutboxMessage.Create(
            Guid.NewGuid(),
            tenantId,
            DocumentIngestionEventTypes.Updated,
            Guid.NewGuid(),
            2,
            "{\"sequence\":2}",
            now.AddMinutes(1));

        var third = NexusAgent.Infrastructure.Persistence.Outbox.OutboxMessage.Create(
            Guid.NewGuid(),
            tenantId,
            DocumentIngestionEventTypes.Deleted,
            Guid.NewGuid(),
            3,
            "{\"sequence\":3}",
            now.AddMinutes(2));

        second.MarkProcessed(now.AddMinutes(3));

        await using (var db = new NexusAgentDbContext(options))
        {
            await db.Database.MigrateAsync();

            await db.OutboxMessages.AddRangeAsync(first, second, third);
            await db.SaveChangesAsync();
        }

        await using (var db = new NexusAgentDbContext(options))
        {
            var reader =
                new NexusAgent.Infrastructure.Persistence.Outbox.PendingOutboxMessageReader(db);

            // The database may contain events from other tests.
            // Use a sufficiently large batch and filter by this test's tenant.
            var pending = await reader.ReadAsync(1000);

            var matching = pending
                .Where(message => message.TenantId == tenantId)
                .ToList();

            Assert.Equal(2, matching.Count);
            Assert.Equal(first.Id, matching[0].Id);
            Assert.Equal(third.Id, matching[1].Id);
            Assert.DoesNotContain(
                matching,
                message => message.Id == second.Id);

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => reader.ReadAsync(0));

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => reader.ReadAsync(1001));
        }
    }
    [Fact]
    public async Task CreateDocument_PersistsExactlyOneCreatedEvent()
    {
        var documentId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var options = new DbContextOptionsBuilder<NexusAgentDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        await using (var db = new NexusAgentDbContext(options))
        {
            await db.Database.MigrateAsync();

            var document = KnowledgeDocument.Create(
                documentId,
                "tenant-outbox-test",
                "Outbox Integration Test",
                "Internal",
                "owner-outbox-test",
                ["owner-outbox-test"],
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
                .AsNoTracking()
                .SingleAsync(x => x.Id == documentId);

            var version = await db.DocumentVersions
                .AsNoTracking()
                .SingleAsync(x => x.DocumentId == documentId);

            var messages = await db.OutboxMessages
                .AsNoTracking()
                .Where(x => x.DocumentId == documentId)
                .ToListAsync();

            var message = Assert.Single(messages);

            Assert.Equal(document.TenantId, message.TenantId);
            Assert.Equal(document.Id, message.DocumentId);
            Assert.Equal(document.Version, message.DocumentVersion);
            Assert.Equal(
                DocumentIngestionEventTypes.Created,
                message.EventType);

            Assert.Equal(1, version.Version);
            Assert.Equal(1, version.ContentVersion);

            Assert.Null(message.ProcessedAt);
            Assert.Equal(0, message.RetryCount);
            Assert.Null(message.LastError);

            var payload = JsonSerializer.Deserialize<DocumentIngestionEvent>(
                message.Payload);

            Assert.NotNull(payload);

            Assert.Equal(message.Id, payload.EventId);
            Assert.Equal(message.EventType, payload.EventType);
            Assert.Equal(message.TenantId, payload.TenantId);
            Assert.Equal(message.DocumentId, payload.DocumentId);
            Assert.Equal(message.DocumentVersion, payload.DocumentVersion);
            Assert.Equal(document.ContentVersion, payload.ContentVersion);
            Assert.InRange(
                Math.Abs(
                    (message.CreatedAt - payload.OccurredAt).Ticks),
                0,
                10);
        }
    }
}