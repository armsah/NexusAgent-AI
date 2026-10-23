using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexusAgent.Infrastructure.Persistence;
using NexusAgent.Infrastructure.Storage;

namespace NexusAgent.IntegrationTests.Documents;

public sealed class DocumentEndpointTests
{
    private const string ConnectionString =
        "Host=127.0.0.1;Port=15432;Database=nexusagent;" +
        "Username=nexusagent;Password=nexusagent_dev_only";

    [Fact]
    public async Task Upload_ValidText_ReturnsCreatedAndPersistsContent()
    {
        await using var factory = new DocumentApiFactory();

        using var client = factory.CreateClient();

        var content = Encoding.UTF8.GetBytes(
            "NexusAgent HTTP ingestion test");

        using var form = CreateForm(
            content,
            "text/plain",
            "integration-test.txt");

        using var response = await client.PostAsync(
            "/api/documents/",
            form);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<UploadResult>();

        Assert.NotNull(result);
        Assert.Equal(1, result.Version);
        Assert.Equal(content.Length, result.Length);
        Assert.Equal(
            Convert.ToHexStringLower(SHA256.HashData(content)),
            result.Sha256);

        await using var scope = factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider
            .GetRequiredService<NexusAgentDbContext>();

        var document = await db.KnowledgeDocuments
            .AsNoTracking()
            .SingleAsync(x => x.Id == result.DocumentId);

        Assert.Equal("local-development", document.TenantId);
        Assert.Equal("local-developer", document.OwnerId);
        Assert.Equal(
            ["local-developer"],
            document.AllowedPrincipalIds);

        var version = await db.DocumentVersions
            .AsNoTracking()
            .SingleAsync(x => x.DocumentId == result.DocumentId);

        Assert.Equal(1, version.Version);

        var store = scope.ServiceProvider
            .GetRequiredService<
                NexusAgent.Application.Documents.IDocumentContentStore>();

        await using var stored = await store.OpenReadAsync(
            document.TenantId,
            document.Id,
            document.Version);

        using var output = new MemoryStream();
        await stored.CopyToAsync(output);

        Assert.Equal(content, output.ToArray());
    }

    [Fact]
    public async Task Upload_ForgedPdf_ReturnsBadRequest()
    {
        await using var factory = new DocumentApiFactory();

        using var client = factory.CreateClient();

        using var form = CreateForm(
            Encoding.UTF8.GetBytes("Not a PDF"),
            "application/pdf",
            "forged.pdf");

        using var response = await client.PostAsync(
            "/api/documents/",
            form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_MissingTitle_ReturnsBadRequest()
    {
        await using var factory = new DocumentApiFactory();

        using var client = factory.CreateClient();

        using var form = CreateForm(
            Encoding.UTF8.GetBytes("Valid content"),
            "text/plain",
            "missing-title.txt",
            includeTitle: false);

        using var response = await client.PostAsync(
            "/api/documents/",
            form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_UnsupportedContentType_ReturnsBadRequest()
    {
        await using var factory = new DocumentApiFactory();

        using var client = factory.CreateClient();

        using var form = CreateForm(
            [1, 2, 3, 4],
            "application/octet-stream",
            "unsupported.bin");

        using var response = await client.PostAsync(
            "/api/documents/",
            form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_FileExceedsLimit_IsRejected()
    {
        await using var factory = new DocumentApiFactory();

        using var client = factory.CreateClient();

        using var form = CreateForm(
            new byte[10 * 1024 * 1024 + 1],
            "text/plain",
            "oversized.txt");

        using var response = await client.PostAsync(
            "/api/documents/",
            form);

        Assert.True(
            response.StatusCode is
                HttpStatusCode.RequestEntityTooLarge or
                HttpStatusCode.BadRequest,
            $"Unexpected status: {response.StatusCode}");
    }

    [Fact]
    public async Task Get_AuthorizedDocument_ReturnsOk()
    {
        await using var factory = new DocumentApiFactory();
        using var client = factory.CreateClient();

        var documentId = await CreateTestDocumentAsync(factory);

        using var response = await client.GetAsync(
            $"/api/documents/{documentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content
            .ReadFromJsonAsync<GetDocumentResponse>();

        Assert.NotNull(result);
        Assert.Equal(documentId, result.DocumentId);
        Assert.Equal(1, result.Version);
        Assert.Equal("HTTP Retrieval Test", result.Title);
        Assert.Equal("local-developer", result.OwnerId);
        Assert.Contains(
            "local-developer",
            result.AllowedPrincipalIds);
    }

    [Fact]
    public async Task Get_UnknownDocument_ReturnsNotFound()
    {
        await using var factory = new DocumentApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            $"/api/documents/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_UnauthorizedPrincipal_ReturnsNotFound()
    {
        await using var factory = new DocumentApiFactory();
        using var client = factory.CreateClient();

        var documentId = await CreateTestDocumentAsync(
            factory,
            allowedPrincipalIds: ["another-user"]);

        using var response = await client.GetAsync(
            $"/api/documents/{documentId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_CrossTenantDocument_ReturnsNotFound()
    {
        await using var factory = new DocumentApiFactory();
        using var client = factory.CreateClient();

        var documentId = await CreateTestDocumentAsync(
            factory,
            tenantId: "another-tenant");

        using var response = await client.GetAsync(
            $"/api/documents/{documentId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_DeletedDocument_ReturnsNotFound()
    {
        await using var factory = new DocumentApiFactory();
        using var client = factory.CreateClient();

        var documentId = await CreateTestDocumentAsync(
            factory,
            deleted: true);

        using var response = await client.GetAsync(
            $"/api/documents/{documentId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Content_UploadedDocument_ReturnsOriginalBytes()
    {
        await using var factory = new DocumentApiFactory();
        using var client = factory.CreateClient();

        var original = Encoding.UTF8.GetBytes(
            "NexusAgent secure content retrieval test");

        using var form = CreateForm(
            original,
            "text/plain",
            "retrieval-test.txt");

        using var uploadResponse = await client.PostAsync(
            "/api/documents/",
            form);

        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);

        var uploaded = await uploadResponse.Content
            .ReadFromJsonAsync<UploadResult>();

        Assert.NotNull(uploaded);

        using var response = await client.GetAsync(
            $"/api/documents/{uploaded.DocumentId}/content");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "application/octet-stream",
            response.Content.Headers.ContentType?.MediaType);

        var downloaded = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(original, downloaded);
    }

    [Fact]
    public async Task Content_UnknownDocument_ReturnsNotFound()
    {
        await using var factory = new DocumentApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            $"/api/documents/{Guid.NewGuid()}/content");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Content_UnauthorizedPrincipal_ReturnsNotFound()
    {
        await using var factory = new DocumentApiFactory();
        using var client = factory.CreateClient();

        var documentId = await CreateTestDocumentAsync(
            factory,
            allowedPrincipalIds: ["another-user"]);

        using var response = await client.GetAsync(
            $"/api/documents/{documentId}/content");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Content_CrossTenantDocument_ReturnsNotFound()
    {
        await using var factory = new DocumentApiFactory();
        using var client = factory.CreateClient();

        var documentId = await CreateTestDocumentAsync(
            factory,
            tenantId: "another-tenant");

        using var response = await client.GetAsync(
            $"/api/documents/{documentId}/content");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Content_DeletedDocument_ReturnsNotFound()
    {
        await using var factory = new DocumentApiFactory();
        using var client = factory.CreateClient();

        var documentId = await CreateTestDocumentAsync(
            factory,
            deleted: true);

        using var response = await client.GetAsync(
            $"/api/documents/{documentId}/content");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
    [Fact]
    public async Task Update_ValidMetadata_ReturnsOkAndPreservesHistory()
    {
        await using var factory = new DocumentApiFactory();
        using var client = factory.CreateClient();

        var documentId = await CreateTestDocumentAsync(factory);

        using var response = await client.PutAsJsonAsync(
            $"/api/documents/{documentId}",
            new
            {
                expectedVersion = 1,
                title = "Updated Architecture Guide",
                classification = "Confidential"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content
            .ReadFromJsonAsync<UpdateDocumentResponse>();

        Assert.NotNull(updated);
        Assert.Equal(documentId, updated.DocumentId);
        Assert.Equal(2, updated.Version);
        Assert.Equal(1, updated.ContentVersion);
        Assert.Equal("Updated Architecture Guide", updated.Title);
        Assert.Equal("Confidential", updated.Classification);
        Assert.Equal(["local-developer"], updated.AllowedPrincipalIds);

        await using var scope = factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider
            .GetRequiredService<NexusAgentDbContext>();

        var current = await db.KnowledgeDocuments
            .AsNoTracking()
            .SingleAsync(d => d.Id == documentId);

        var history = await db.DocumentVersions
            .AsNoTracking()
            .Where(v => v.DocumentId == documentId)
            .OrderBy(v => v.Version)
            .ToListAsync();

        Assert.Equal(2, current.Version);
        Assert.Equal(1, current.ContentVersion);
        Assert.Equal("Updated Architecture Guide", current.Title);

        Assert.Equal(2, history.Count);
        Assert.Equal("HTTP Retrieval Test", history[0].Title);
        Assert.Equal("Updated Architecture Guide", history[1].Title);
        Assert.Equal(1, history[0].Version);
        Assert.Equal(2, history[1].Version);
    }

    [Fact]
    public async Task Update_StaleVersion_ReturnsConflict()
    {
        await using var factory = new DocumentApiFactory();
        using var client = factory.CreateClient();

        var documentId = await CreateTestDocumentAsync(factory);

        using var firstResponse = await client.PutAsJsonAsync(
            $"/api/documents/{documentId}",
            new
            {
                expectedVersion = 1,
                title = "First Update",
                classification = "Internal"
            });

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        using var staleResponse = await client.PutAsJsonAsync(
            $"/api/documents/{documentId}",
            new
            {
                expectedVersion = 1,
                title = "Stale Update",
                classification = "Internal"
            });

        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);

        using var getResponse = await client.GetAsync(
            $"/api/documents/{documentId}");

        var current = await getResponse.Content
            .ReadFromJsonAsync<GetDocumentResponse>();

        Assert.NotNull(current);
        Assert.Equal(2, current.Version);
        Assert.Equal("First Update", current.Title);
    }

    [Theory]
    [InlineData(0, "Valid Title", "Internal")]
    [InlineData(1, "", "Internal")]
    [InlineData(1, "Valid Title", "")]
    public async Task Update_InvalidMetadata_ReturnsBadRequest(
        int expectedVersion,
        string title,
        string classification)
    {
        await using var factory = new DocumentApiFactory();
        using var client = factory.CreateClient();

        var documentId = await CreateTestDocumentAsync(factory);

        using var response = await client.PutAsJsonAsync(
            $"/api/documents/{documentId}",
            new
            {
                expectedVersion,
                title,
                classification
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_UnknownDocument_ReturnsNotFound()
    {
        await using var factory = new DocumentApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.PutAsJsonAsync(
            $"/api/documents/{Guid.NewGuid()}",
            new
            {
                expectedVersion = 1,
                title = "Updated Title",
                classification = "Internal"
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_UnauthorizedPrincipal_ReturnsNotFound()
    {
        await using var factory = new DocumentApiFactory();
        using var client = factory.CreateClient();

        var documentId = await CreateTestDocumentAsync(
            factory,
            allowedPrincipalIds: ["another-user"]);

        using var response = await client.PutAsJsonAsync(
            $"/api/documents/{documentId}",
            new
            {
                expectedVersion = 1,
                title = "Unauthorized Update",
                classification = "Internal"
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_CrossTenantDocument_ReturnsNotFound()
    {
        await using var factory = new DocumentApiFactory();
        using var client = factory.CreateClient();

        var documentId = await CreateTestDocumentAsync(
            factory,
            tenantId: "another-tenant");

        using var response = await client.PutAsJsonAsync(
            $"/api/documents/{documentId}",
            new
            {
                expectedVersion = 1,
                title = "Cross-Tenant Update",
                classification = "Internal"
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_DeletedDocument_ReturnsNotFound()
    {
        await using var factory = new DocumentApiFactory();
        using var client = factory.CreateClient();

        var documentId = await CreateTestDocumentAsync(
            factory,
            deleted: true);

        using var response = await client.PutAsJsonAsync(
            $"/api/documents/{documentId}",
            new
            {
                expectedVersion = 2,
                title = "Attempted Resurrection",
                classification = "Internal"
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider
            .GetRequiredService<NexusAgentDbContext>();

        var document = await db.KnowledgeDocuments
            .AsNoTracking()
            .SingleAsync(d => d.Id == documentId);

        var versionCount = await db.DocumentVersions
            .AsNoTracking()
            .CountAsync(v => v.DocumentId == documentId);

        Assert.Equal(
            NexusAgent.Domain.Documents.DocumentStatus.Deleted,
            document.Status);

        Assert.Equal(2, document.Version);
        Assert.Equal(2, versionCount);
    }
    [Fact]
    public async Task Update_NonOwnerWithReadAccess_ReturnsNotFound()
    {
        await using var factory = new DocumentApiFactory();
        using var client = factory.CreateClient();

        var documentId = Guid.NewGuid();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<
                NexusAgent.Application.Abstractions.IKnowledgeDocumentRepository>();

            var document = NexusAgent.Domain.Documents.KnowledgeDocument.Create(
                documentId,
                "local-development",
                "Shared Document",
                "Internal",
                "another-owner",
                ["local-developer"],
                DateTimeOffset.UtcNow);

            await repository.AddAsync(
                document,
                NexusAgent.Domain.Documents.DocumentVersion.Capture(
                    Guid.NewGuid(),
                    document));
        }

        // The requester can read this document.
        using var getResponse = await client.GetAsync(
            $"/api/documents/{documentId}");

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        // But the requester is not its owner.
        using var updateResponse = await client.PutAsJsonAsync(
            $"/api/documents/{documentId}",
            new
            {
                expectedVersion = 1,
                title = "Unauthorized Modification",
                classification = "Confidential"
            });

        Assert.Equal(HttpStatusCode.NotFound, updateResponse.StatusCode);

        await using var verificationScope = factory.Services.CreateAsyncScope();

        var db = verificationScope.ServiceProvider
            .GetRequiredService<NexusAgentDbContext>();

        var persisted = await db.KnowledgeDocuments
            .AsNoTracking()
            .SingleAsync(d => d.Id == documentId);

        var versionCount = await db.DocumentVersions
            .AsNoTracking()
            .CountAsync(v => v.DocumentId == documentId);

        Assert.Equal("Shared Document", persisted.Title);
        Assert.Equal(1, persisted.Version);
        Assert.Equal(1, versionCount);
    }
    private sealed record UpdateDocumentResponse(
        Guid DocumentId,
        int Version,
        int ContentVersion,
        string Title,
        string Classification,
        string[] AllowedPrincipalIds);
    private static async Task<Guid> CreateTestDocumentAsync(
        DocumentApiFactory factory,
        string tenantId = "local-development",
        string[]? allowedPrincipalIds = null,
        bool deleted = false)
    {
        await using var scope = factory.Services.CreateAsyncScope();

        var repository = scope.ServiceProvider.GetRequiredService<
            NexusAgent.Application.Abstractions.IKnowledgeDocumentRepository>();

        var document = NexusAgent.Domain.Documents.KnowledgeDocument.Create(
            Guid.NewGuid(),
            tenantId,
            "HTTP Retrieval Test",
            "internal",
            "local-developer",
            allowedPrincipalIds ?? ["local-developer"],
            DateTimeOffset.UtcNow);

        var initialVersion =
            NexusAgent.Domain.Documents.DocumentVersion.Capture(
                Guid.NewGuid(),
                document);

        await repository.AddAsync(document, initialVersion);

        if (deleted)
        {
            document.Delete(
                tenantId,
                DateTimeOffset.UtcNow.AddSeconds(1));

            var deletedVersion =
                NexusAgent.Domain.Documents.DocumentVersion.Capture(
                    Guid.NewGuid(),
                    document);

            await repository.SaveVersionAsync(
                document,
                deletedVersion);
        }

        return document.Id;
    }

    private sealed record GetDocumentResponse(
        Guid DocumentId,
        int Version,
        string Title,
        string Classification,
        string OwnerId,
        string[] AllowedPrincipalIds,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
    private static MultipartFormDataContent CreateForm(
        byte[] content,
        string contentType,
        string filename,
        bool includeTitle = true)
    {
        var form = new MultipartFormDataContent();

        if (includeTitle)
            form.Add(new StringContent("HTTP Ingestion Test"), "title");

        form.Add(new StringContent("internal"), "classification");

        var file = new ByteArrayContent(content);
        file.Headers.ContentType =
            new MediaTypeHeaderValue(contentType);

        form.Add(file, "file", filename);

        return form;
    }

    private sealed record UploadResult(
        Guid DocumentId,
        int Version,
        string StorageKey,
        string Sha256,
        long Length);

    private sealed class DocumentApiFactory
        : WebApplicationFactory<Program>
    {
        private readonly string _storageRoot = Path.Combine(
            Path.GetTempPath(),
            "nexusagent-http-tests",
            Guid.NewGuid().ToString("N"));

        protected override void ConfigureWebHost(
            IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");

            builder.UseSetting(
                "ConnectionStrings:NexusAgent",
                ConnectionString);

            builder.UseSetting(
                "DocumentStorage:RootPath",
                _storageRoot);
        }
        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();

            if (Directory.Exists(_storageRoot))
                Directory.Delete(_storageRoot, recursive: true);
        }
    }
}
