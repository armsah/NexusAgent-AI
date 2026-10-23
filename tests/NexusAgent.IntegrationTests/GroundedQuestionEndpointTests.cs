using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NexusAgent.Application.Questions;
using NexusAgent.Domain.Documents;
using NexusAgent.Infrastructure.Persistence;
using NexusAgent.Infrastructure.Persistence.Search;

namespace NexusAgent.IntegrationTests;

public sealed class GroundedQuestionEndpointTests
{
    private const string ConnectionString =
        "Host=127.0.0.1;Port=15432;Database=nexusagent;" +
        "Username=nexusagent;Password=nexusagent_dev_only";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Answer_UsesOnlyAuthorizedCurrentEvidence(bool generated)
    {
        await using var factory = new QuestionFactory(
            generated ? GeneratorMode.Valid : GeneratorMode.Disabled);

        using var client = factory.CreateClient();

        var keyword = "nexusground" + Guid.NewGuid().ToString("N");
        var ids = new List<Guid>();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider
            .GetRequiredService<NexusAgentDbContext>();

        try
        {
            var visible = AddDocument(
                db, ids, "local-development",
                "Visible policy", ["local-developer"]);

            var forbidden = AddDocument(
                db, ids, "local-development",
                "Forbidden policy", ["other-user"]);

            var otherTenant = AddDocument(
                db, ids, "other-tenant",
                "Other tenant policy", ["local-developer"]);

            var deleted = AddDocument(
                db, ids, "local-development",
                "Deleted policy", ["local-developer"]);

            deleted.Delete(
                "local-development",
                DateTimeOffset.UtcNow.AddSeconds(1));

            var stale = AddDocument(
                db, ids, "local-development",
                "Stale policy", ["local-developer"]);

            await db.SaveChangesAsync();

            AddIndex(db, visible, keyword + " approved evidence");
            AddIndex(db, forbidden, keyword + " forbidden evidence");
            AddIndex(db, otherTenant, keyword + " other tenant evidence");
            AddIndex(db, deleted, keyword + " deleted evidence");

            db.DocumentSearchIndexes.Add(new DocumentSearchIndex
            {
                DocumentId = stale.Id,
                TenantId = stale.TenantId,
                ContentVersion = stale.ContentVersion + 1,
                Content = keyword + " stale evidence",
                IndexedAt = DateTimeOffset.UtcNow
            });

            await db.SaveChangesAsync();

            using var response = await client.PostAsJsonAsync(
                "/api/questions/answer",
                new { question = keyword });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var result = await response.Content
                .ReadFromJsonAsync<QuestionResponse>();

            Assert.NotNull(result);
            Assert.False(result.Abstained);

            var citation = Assert.Single(result.Citations);

            Assert.Equal(visible.Id, citation.DocumentId);
            Assert.Equal(visible.ContentVersion, citation.ContentVersion);
            Assert.Equal(visible.Version, citation.DocumentVersion);
            Assert.Equal("S1", citation.SourceId);
            Assert.Contains("[S1]", result.Answer);
            Assert.DoesNotContain("forbidden evidence", result.Answer);
            Assert.DoesNotContain("other tenant evidence", result.Answer);
            Assert.DoesNotContain("deleted evidence", result.Answer);
            Assert.DoesNotContain("stale evidence", result.Answer);
        }
        finally
        {
            await CleanupAsync(db, ids);
        }
    }

    [Theory]
    [InlineData(GeneratorMode.Disabled)]
    [InlineData(GeneratorMode.InvalidCitation)]
    [InlineData(GeneratorMode.Failure)]
    public async Task Answer_AbstainsWhenEvidenceOrGenerationIsInvalid(
        GeneratorMode mode)
    {
        await using var factory = new QuestionFactory(mode);
        using var client = factory.CreateClient();

        var keyword = "nexusmissing" + Guid.NewGuid().ToString("N");

        using var response = await client.PostAsJsonAsync(
            "/api/questions/answer",
            new { question = keyword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content
            .ReadFromJsonAsync<QuestionResponse>();

        Assert.NotNull(result);
        Assert.True(result.Abstained);
        Assert.Empty(result.Citations);
    }

    [Theory]
    [InlineData(GeneratorMode.InvalidCitation)]
    [InlineData(GeneratorMode.Failure)]
    public async Task Answer_FailsClosedWhenGeneratorCannotBeTrusted(
        GeneratorMode mode)
    {
        await using var factory = new QuestionFactory(mode);
        using var client = factory.CreateClient();

        var keyword = "nexusinvalid" + Guid.NewGuid().ToString("N");
        var ids = new List<Guid>();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider
            .GetRequiredService<NexusAgentDbContext>();

        try
        {
            var document = AddDocument(
                db, ids, "local-development",
                "Authorized policy", ["local-developer"]);

            await db.SaveChangesAsync();

            AddIndex(db, document, keyword + " verified evidence");
            await db.SaveChangesAsync();

            using var response = await client.PostAsJsonAsync(
                "/api/questions/answer",
                new { question = keyword });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var result = await response.Content
                .ReadFromJsonAsync<QuestionResponse>();

            Assert.NotNull(result);
            Assert.True(result.Abstained);
            Assert.Empty(result.Citations);
            Assert.DoesNotContain("invented", result.Answer);
        }
        finally
        {
            await CleanupAsync(db, ids);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Answer_RejectsEmptyQuestions(string question)
    {
        await using var factory = new QuestionFactory(GeneratorMode.Disabled);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/questions/answer",
            new { question });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static KnowledgeDocument AddDocument(
        NexusAgentDbContext db,
        List<Guid> ids,
        string tenant,
        string title,
        string[] principals)
    {
        var document = KnowledgeDocument.Create(
            Guid.NewGuid(),
            tenant,
            title,
            "internal",
            "owner",
            principals,
            DateTimeOffset.UtcNow);

        db.KnowledgeDocuments.Add(document);
        ids.Add(document.Id);

        return document;
    }

    private static void AddIndex(
        NexusAgentDbContext db,
        KnowledgeDocument document,
        string content)
    {
        db.DocumentSearchIndexes.Add(new DocumentSearchIndex
        {
            DocumentId = document.Id,
            TenantId = document.TenantId,
            ContentVersion = document.ContentVersion,
            Content = content,
            IndexedAt = DateTimeOffset.UtcNow
        });
    }

    private static async Task CleanupAsync(
        NexusAgentDbContext db,
        List<Guid> ids)
    {
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

    private sealed class QuestionFactory(GeneratorMode mode)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(
            IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");

            builder.UseSetting(
                "ConnectionStrings:NexusAgent",
                ConnectionString);

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IGroundedAnswerGenerator>();

                if (mode != GeneratorMode.Disabled)
                {
                    services.AddSingleton<IGroundedAnswerGenerator>(
                        new FakeGenerator(mode));
                }
            });
        }
    }

    private sealed class FakeGenerator(GeneratorMode mode)
        : IGroundedAnswerGenerator
    {
        public Task<GroundedGenerationResult> GenerateAsync(
            string question,
            IReadOnlyList<GroundingEvidence> evidence,
            CancellationToken cancellationToken = default)
        {
            if (mode == GeneratorMode.Failure)
            {
                throw new InvalidOperationException(
                    "Simulated model failure.");
            }

            if (mode == GeneratorMode.InvalidCitation)
            {
                return Task.FromResult(new GroundedGenerationResult(
                    "Invented statement [S99].",
                    ["S99"]));
            }

            return Task.FromResult(new GroundedGenerationResult(
                evidence[0].Excerpt + " [S1]",
                ["S1"]));
        }
    }

    public enum GeneratorMode
    {
        Disabled,
        Valid,
        InvalidCitation,
        Failure
    }

    private sealed record QuestionResponse(
        string Question,
        string Answer,
        bool Abstained,
        GroundedCitationResponse[] Citations);

    private sealed record GroundedCitationResponse(
        string SourceId,
        Guid DocumentId,
        string Title,
        int DocumentVersion,
        int ContentVersion,
        string Excerpt);
}