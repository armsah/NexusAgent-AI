using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NexusAgent.Application.Search;
using NexusAgent.Domain.Documents;
using NexusAgent.Infrastructure.Persistence;
using NexusAgent.Infrastructure.Persistence.Search;
using Pgvector;

namespace NexusAgent.IntegrationTests;

public sealed class DocumentSearchEndpointTests
{
    private const string ConnectionString =
        "Host=127.0.0.1;Port=15432;Database=nexusagent;" +
        "Username=nexusagent;Password=nexusagent_dev_only";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Search_ExcludesUnauthorizedDeletedAndStaleContent(
        bool hybrid)
    {
        await using var factory = new SearchFactory(hybrid);
        using var client = factory.CreateClient();

        var keyword = "nexusprobe" + Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var ids = new List<Guid>();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider
            .GetRequiredService<NexusAgentDbContext>();

        try
        {
            KnowledgeDocument Create(
                string tenant,
                string title,
                string[] principals,
                bool deleted = false)
            {
                var document = KnowledgeDocument.Create(
                    Guid.NewGuid(),
                    tenant,
                    title,
                    "internal",
                    "owner",
                    principals,
                    now);

                if (deleted)
                    document.Delete(tenant, now.AddSeconds(1));

                ids.Add(document.Id);
                db.KnowledgeDocuments.Add(document);

                return document;
            }

            var visible = Create(
                "local-development",
                keyword + " visible",
                ["local-developer"]);

            Create(
                "local-development",
                keyword + " forbidden",
                ["different-user"]);

            Create(
                "different-tenant",
                keyword + " cross-tenant",
                ["local-developer"]);

            Create(
                "local-development",
                keyword + " deleted",
                ["local-developer"],
                deleted: true);

            var stale = Create(
                "local-development",
                "Stale content document",
                ["local-developer"]);

            await db.SaveChangesAsync();

            db.DocumentSearchIndexes.Add(
                new DocumentSearchIndex
                {
                    DocumentId = stale.Id,
                    TenantId = stale.TenantId,
                    ContentVersion = stale.ContentVersion + 1,
                    Content = keyword,
                    IndexedAt = now
                });

            await db.SaveChangesAsync();

            using var response = await client.GetAsync(
                "/api/search/documents?query=" + keyword);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var result = await response.Content
                .ReadFromJsonAsync<SearchResponse>();

            Assert.NotNull(result);
            Assert.Equal(hybrid ? "hybrid" : "lexical", result.Mode);

            Assert.Contains(result.Results, x => x.Id == visible.Id);

            foreach (var forbiddenId in ids.Where(x => x != visible.Id))
            {
                Assert.DoesNotContain(
                    result.Results,
                    x => x.Id == forbiddenId);
            }
        }
        finally
        {
            await CleanupAsync(db, ids);
        }
    }

    [Fact]
    public async Task HybridSearch_RejectsStaleAndMismatchedVectors()
    {
        await using var factory = new SearchFactory(hybrid: true);
        using var client = factory.CreateClient();

        var now = DateTimeOffset.UtcNow;
        var ids = new List<Guid>();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider
            .GetRequiredService<NexusAgentDbContext>();

        try
        {
            KnowledgeDocument Create(string title)
            {
                var document = KnowledgeDocument.Create(
                    Guid.NewGuid(),
                    "local-development",
                    title,
                    "internal",
                    "owner",
                    ["local-developer"],
                    now);

                ids.Add(document.Id);
                db.KnowledgeDocuments.Add(document);
                return document;
            }

            var current = Create("Semantic current");
            var stale = Create("Semantic stale");
            var wrongModel = Create("Semantic wrong model");

            await db.SaveChangesAsync();

            void AddVector(
                KnowledgeDocument document,
                int contentVersion,
                string model)
            {
                var embedding = new float[1536];
                embedding[0] = 1;

                db.DocumentVectorIndexes.Add(
                    new DocumentVectorIndex
                    {
                        DocumentId = document.Id,
                        TenantId = document.TenantId,
                        ContentVersion = contentVersion,
                        EmbeddingModel = model,
                        Embedding = new Vector(embedding),
                        IndexedAt = now
                    });
            }

            AddVector(current, current.ContentVersion, "test-search-model");
            AddVector(stale, stale.ContentVersion + 1, "test-search-model");
            AddVector(wrongModel, wrongModel.ContentVersion, "other-model");

            await db.SaveChangesAsync();

            using var response = await client.GetAsync(
                "/api/search/documents?query=uniquesemantic" +
                Guid.NewGuid().ToString("N"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var result = await response.Content
                .ReadFromJsonAsync<SearchResponse>();

            Assert.NotNull(result);
            Assert.Equal("hybrid", result.Mode);
            Assert.Contains(result.Results, x => x.Id == current.Id);
            Assert.DoesNotContain(result.Results, x => x.Id == stale.Id);
            Assert.DoesNotContain(result.Results, x => x.Id == wrongModel.Id);
        }
        finally
        {
            await CleanupAsync(db, ids);
        }
    }

    [Fact]
    public async Task HybridSearch_FusesLexicalAndSemanticCandidates()
    {
        await using var factory = new SearchFactory(hybrid: true);
        using var client = factory.CreateClient();

        var keyword = "fusionprobe" + Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var ids = new List<Guid>();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider
            .GetRequiredService<NexusAgentDbContext>();

        try
        {
            var lexical = KnowledgeDocument.Create(
                Guid.NewGuid(),
                "local-development",
                keyword + " lexical",
                "internal",
                "owner",
                ["local-developer"],
                now);

            var semantic = KnowledgeDocument.Create(
                Guid.NewGuid(),
                "local-development",
                "Unrelated semantic document",
                "internal",
                "owner",
                ["local-developer"],
                now);

            ids.AddRange([lexical.Id, semantic.Id]);

            db.KnowledgeDocuments.AddRange(lexical, semantic);

            var embedding = new float[1536];
            embedding[0] = 1;

            db.DocumentVectorIndexes.Add(
                new DocumentVectorIndex
                {
                    DocumentId = semantic.Id,
                    TenantId = semantic.TenantId,
                    ContentVersion = semantic.ContentVersion,
                    EmbeddingModel = "test-search-model",
                    Embedding = new Vector(embedding),
                    IndexedAt = now
                });

            await db.SaveChangesAsync();

            using var response = await client.GetAsync(
                "/api/search/documents?query=" + keyword);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var result = await response.Content
                .ReadFromJsonAsync<SearchResponse>();

            Assert.NotNull(result);
            Assert.Equal("hybrid", result.Mode);
            Assert.Contains(result.Results, x => x.Id == lexical.Id);
            Assert.Contains(result.Results, x => x.Id == semantic.Id);
        }
        finally
        {
            await CleanupAsync(db, ids);
        }
    }

    private static async Task CleanupAsync(
        NexusAgentDbContext db,
        IReadOnlyCollection<Guid> ids)
    {
        db.ChangeTracker.Clear();

        await db.DocumentVectorIndexes
            .Where(x => ids.Contains(x.DocumentId))
            .ExecuteDeleteAsync();

        await db.DocumentSearchIndexes
            .Where(x => ids.Contains(x.DocumentId))
            .ExecuteDeleteAsync();

        await db.KnowledgeDocuments
            .Where(x => ids.Contains(x.Id))
            .ExecuteDeleteAsync();
    }

    private sealed record SearchResponse(
        string Mode,
        int Count,
        List<SearchItem> Results);

    private sealed record SearchItem(Guid Id);

    private sealed class SearchFactory(bool hybrid)
        : WebApplicationFactory<Program>
    {
        private readonly string _storageRoot = Path.Combine(
            Path.GetTempPath(),
            "nexusagent-search-tests",
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

            builder.UseSetting("Embeddings:Provider", "Disabled");

            if (hybrid)
            {
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<ITextEmbeddingProvider>();
                    services.AddSingleton<ITextEmbeddingProvider>(
                        new FakeEmbeddingProvider());
                });
            }
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();

            if (Directory.Exists(_storageRoot))
                Directory.Delete(_storageRoot, recursive: true);
        }
    }

    private sealed class FakeEmbeddingProvider : ITextEmbeddingProvider
    {
        public string ModelId => "test-search-model";

        public int Dimensions => 1536;

        public Task<float[]> EmbedAsync(
            string text,
            CancellationToken cancellationToken = default)
        {
            var embedding = new float[1536];
            embedding[0] = 1;

            return Task.FromResult(embedding);
        }
    }
}