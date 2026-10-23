using NexusAgent.Application.Abstractions;
using NexusAgent.Application.Documents;
using NexusAgent.Domain.Documents;

namespace NexusAgent.UnitTests.Documents;

public sealed class GetDocumentContentHandlerTests
{
    [Theory]
    [InlineData("other-tenant", "allowed-user", false)]
    [InlineData("tenant-a", "unauthorized-user", false)]
    [InlineData("tenant-a", "allowed-user", true)]
    public async Task Access_is_checked_before_storage_is_opened(
        string tenantId,
        string requesterId,
        bool isDeleted)
    {
        var document = KnowledgeDocument.Create(
            Guid.NewGuid(),
            "tenant-a",
            "Test document",
            "internal",
            "owner",
            ["allowed-user"],
            DateTimeOffset.UtcNow);

        if (isDeleted)
        {
            document.Delete("tenant-a", DateTimeOffset.UtcNow);
        }

        var repository = new FakeRepository(document);
        var storage = new TrackingContentStore();

        var handler = new GetDocumentContentHandler(
            new GetDocumentHandler(repository),
            storage);

        var result = await handler.HandleAsync(
            new GetDocumentContentQuery(
                tenantId,
                requesterId,
                document.Id));

        Assert.Null(result);
        Assert.Equal(0, storage.OpenReadCalls);
    }

    [Fact]
    public async Task Authorized_request_opens_current_document_version()
    {
        var document = KnowledgeDocument.Create(
            Guid.NewGuid(),
            "tenant-a",
            "Test document",
            "internal",
            "owner",
            ["allowed-user"],
            DateTimeOffset.UtcNow);

        var repository = new FakeRepository(document);
        var storage = new TrackingContentStore();

        var handler = new GetDocumentContentHandler(
            new GetDocumentHandler(repository),
            storage);

        var result = await handler.HandleAsync(
            new GetDocumentContentQuery(
                "tenant-a",
                "allowed-user",
                document.Id));

        Assert.NotNull(result);
        Assert.Equal(document.Version, result.Version);
        Assert.Equal(1, storage.OpenReadCalls);
        Assert.Equal(document.Version, storage.LastOpenedVersion);

        await result.Content.DisposeAsync();
    }

    [Fact]
    public async Task Metadata_update_preserves_original_content_version()
    {
        var document = KnowledgeDocument.Create(
            Guid.NewGuid(),
            "tenant-a",
            "Original title",
            "internal",
            "owner",
            ["allowed-user"],
            DateTimeOffset.UtcNow);

        Assert.Equal(1, document.Version);
        Assert.Equal(1, document.ContentVersion);

        document.Update(
            "tenant-a",
            "Updated title",
            "confidential",
            ["allowed-user"],
            DateTimeOffset.UtcNow.AddMinutes(1));

        Assert.Equal(2, document.Version);
        Assert.Equal(1, document.ContentVersion);

        var repository = new FakeRepository(document);
        var storage = new TrackingContentStore();

        var handler = new GetDocumentContentHandler(
            new GetDocumentHandler(repository),
            storage);

        var result = await handler.HandleAsync(
            new GetDocumentContentQuery(
                "tenant-a",
                "allowed-user",
                document.Id));

        Assert.NotNull(result);
        Assert.Equal(1, result.Version);
        Assert.Equal(1, storage.OpenReadCalls);
        Assert.Equal(1, storage.LastOpenedVersion);

        await result.Content.DisposeAsync();
    }
    private sealed class FakeRepository(KnowledgeDocument document)
        : IKnowledgeDocumentRepository
    {
        public Task<KnowledgeDocument?> GetAsync(
            string tenantId,
            Guid documentId,
            CancellationToken cancellationToken = default)
        {
            KnowledgeDocument? result =
                document.Id == documentId &&
                document.TenantId == tenantId
                    ? document
                    : null;

            return Task.FromResult(result);
        }

        public Task AddAsync(
            KnowledgeDocument document,
            DocumentVersion initialVersion,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SaveVersionAsync(
            KnowledgeDocument document,
            DocumentVersion version,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class TrackingContentStore : IDocumentContentStore
    {
        public int OpenReadCalls { get; private set; }

        public int? LastOpenedVersion { get; private set; }

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
            CancellationToken cancellationToken = default)
        {
            OpenReadCalls++;
            LastOpenedVersion = version;

            return Task.FromResult<Stream>(
                new MemoryStream("test content"u8.ToArray()));
        }

        public Task DeleteAsync(
            string tenantId,
            Guid documentId,
            int version,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
