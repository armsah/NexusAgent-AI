using NexusAgent.Application.Abstractions;
using NexusAgent.Application.Documents;
using NexusAgent.Domain.Documents;

namespace NexusAgent.UnitTests.Documents;

public sealed class GetDocumentHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Authorized_principal_can_retrieve_document()
    {
        var document = CreateDocument();
        var handler = new GetDocumentHandler(new FakeRepository(document));

        var result = await handler.HandleAsync(
            new GetDocumentQuery("tenant-a", "alice", document.Id));

        Assert.NotNull(result);
        Assert.Equal(document.Id, result.DocumentId);
        Assert.Equal(1, result.Version);
        Assert.Equal("Security Policy", result.Title);
        Assert.Contains("alice", result.AllowedPrincipalIds);
    }

    [Fact]
    public async Task Cross_tenant_request_returns_null()
    {
        var document = CreateDocument();
        var handler = new GetDocumentHandler(new FakeRepository(document));

        var result = await handler.HandleAsync(
            new GetDocumentQuery("tenant-b", "alice", document.Id));

        Assert.Null(result);
    }

    [Fact]
    public async Task Unauthorized_principal_returns_null()
    {
        var document = CreateDocument();
        var handler = new GetDocumentHandler(new FakeRepository(document));

        var result = await handler.HandleAsync(
            new GetDocumentQuery("tenant-a", "bob", document.Id));

        Assert.Null(result);
    }

    [Fact]
    public async Task Deleted_document_returns_null()
    {
        var document = CreateDocument();
        document.Delete("tenant-a", Now.AddMinutes(1));

        var handler = new GetDocumentHandler(new FakeRepository(document));

        var result = await handler.HandleAsync(
            new GetDocumentQuery("tenant-a", "alice", document.Id));

        Assert.Null(result);
    }

    [Fact]
    public async Task Unknown_document_returns_null()
    {
        var handler = new GetDocumentHandler(new FakeRepository(null));

        var result = await handler.HandleAsync(
            new GetDocumentQuery("tenant-a", "alice", Guid.NewGuid()));

        Assert.Null(result);
    }

    private static KnowledgeDocument CreateDocument()
    {
        return KnowledgeDocument.Create(
            Guid.NewGuid(),
            "tenant-a",
            "Security Policy",
            "internal",
            "alice",
            ["alice"],
            Now);
    }

    private sealed class FakeRepository(KnowledgeDocument? document)
        : IKnowledgeDocumentRepository
    {
        public Task<KnowledgeDocument?> GetAsync(
            string tenantId,
            Guid documentId,
            CancellationToken cancellationToken = default)
        {
            KnowledgeDocument? result =
                document is not null &&
                document.Id == documentId &&
                document.TenantId == tenantId
                    ? document
                    : null;

            return Task.FromResult(result);
        }

        public Task AddAsync(
            KnowledgeDocument document,
            DocumentVersion initialVersion,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task SaveVersionAsync(
            KnowledgeDocument document,
            DocumentVersion version,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
