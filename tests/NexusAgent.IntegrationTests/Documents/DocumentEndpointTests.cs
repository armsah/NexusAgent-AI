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

            builder.ConfigureAppConfiguration(
                (_, configuration) =>
                {
                    configuration.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["ConnectionStrings:NexusAgent"] =
                                ConnectionString,
                            ["DocumentStorage:RootPath"] =
                                _storageRoot
                        });
                });
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();

            if (Directory.Exists(_storageRoot))
                Directory.Delete(_storageRoot, recursive: true);
        }
    }
}

