using Microsoft.EntityFrameworkCore;
using NexusAgent.Api.Identity;
using NexusAgent.Application.Search;
using NexusAgent.Domain.Documents;
using NexusAgent.Infrastructure.Persistence;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace NexusAgent.Api.Search;

public static class DocumentSearchEndpoints
{
    public static IEndpointRouteBuilder MapDocumentSearchEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/api/search/documents",
            async (
                string query,
                NexusAgentDbContext db,
                DevelopmentRequestIdentity identity,
                IServiceProvider services,
                CancellationToken cancellationToken) =>
            {
                var normalizedQuery = query?.Trim();

                if (string.IsNullOrWhiteSpace(normalizedQuery) ||
                    normalizedQuery.Length > 200)
                {
                    return Results.BadRequest(new
                    {
                        error = "Query must contain 1-200 characters."
                    });
                }

                var provider =
                    services.GetService(typeof(ITextEmbeddingProvider))
                        as ITextEmbeddingProvider;

                Vector? queryVector = null;

                if (provider is not null)
                {
                    var embedding = await provider.EmbedAsync(
                        normalizedQuery,
                        cancellationToken);

                    EmbeddingValidation.Validate(provider, embedding);
                    queryVector = new Vector(embedding);
                }


                var authorizedDocuments = db.KnowledgeDocuments
                    .AsNoTracking()
                    .Where(document =>
                        document.TenantId == identity.TenantId &&
                        document.Status == DocumentStatus.Active &&
                        document.AllowedPrincipalIds.Contains(
                            identity.RequesterId));

                var lexicalCandidates =
                    from document in authorizedDocuments
                    join indexEntry in db.DocumentSearchIndexes.AsNoTracking()
                        on document.Id equals indexEntry.DocumentId
                        into indexEntries
                    from indexEntry in indexEntries.DefaultIfEmpty()
                    let titleVector = EF.Functions.ToTsVector(
                        "english",
                        document.Title)
                    let contentVector = EF.Functions.ToTsVector(
                        "english",
                        indexEntry == null ? "" : indexEntry.Content)
                    let contentIsCurrent = indexEntry != null &&
                        indexEntry.TenantId == document.TenantId &&
                        indexEntry.ContentVersion == document.ContentVersion
                    where titleVector.Matches(EF.Functions.PlainToTsQuery("english", normalizedQuery)) ||
                        (contentIsCurrent &&
                         contentVector.Matches(EF.Functions.PlainToTsQuery("english", normalizedQuery)))
                    select new
                    {
                        document.Id,
                        document.Title,
                        document.Classification,
                        document.Version,
                        document.ContentVersion,
                        Score =
                            (titleVector.Matches(EF.Functions.PlainToTsQuery("english", normalizedQuery))
                                ? titleVector.Rank(EF.Functions.PlainToTsQuery("english", normalizedQuery)) * 2.0f
                                : 0.0f) +
                            (contentIsCurrent &&
                             contentVector.Matches(EF.Functions.PlainToTsQuery("english", normalizedQuery))
                                ? contentVector.Rank(EF.Functions.PlainToTsQuery("english", normalizedQuery))
                                : 0.0f)
                    };

                if (provider is null || queryVector is null)
                {
                    var lexicalResults = await lexicalCandidates
                        .OrderByDescending(x => x.Score)
                        .ThenBy(x => x.Id)
                        .Take(20)
                        .ToListAsync(cancellationToken);

                    return Results.Ok(new
                    {
                        query = normalizedQuery,
                        mode = "lexical",
                        count = lexicalResults.Count,
                        results = lexicalResults
                    });
                }

                var modelId = provider.ModelId;

                var semanticCandidates =
                    from document in authorizedDocuments
                    join vectorEntry in db.DocumentVectorIndexes.AsNoTracking()
                        on document.Id equals vectorEntry.DocumentId
                    where vectorEntry.TenantId == document.TenantId &&
                          vectorEntry.ContentVersion == document.ContentVersion &&
                          vectorEntry.EmbeddingModel == modelId
                    select new
                    {
                        document.Id,
                        document.Title,
                        document.Classification,
                        document.Version,
                        document.ContentVersion,
                        Distance = vectorEntry.Embedding.CosineDistance(
                            queryVector)
                    };

                // Reciprocal rank fusion avoids combining incompatible
                // lexical-rank and cosine-distance score scales.
                var lexicalTop = await lexicalCandidates
                    .OrderByDescending(x => x.Score)
                    .ThenBy(x => x.Id)
                    .Take(60)
                    .ToListAsync(cancellationToken);

                var semanticTop = await semanticCandidates
                    .OrderBy(x => x.Distance)
                    .ThenBy(x => x.Id)
                    .Take(60)
                    .ToListAsync(cancellationToken);

                var ranks = new Dictionary<Guid, (
                    Guid Id,
                    string Title,
                    string Classification,
                    int Version,
                    int ContentVersion,
                    double Score)>();

                for (var i = 0; i < lexicalTop.Count; i++)
                {
                    var item = lexicalTop[i];
                    ranks[item.Id] = (
                        item.Id,
                        item.Title,
                        item.Classification,
                        item.Version,
                        item.ContentVersion,
                        1.0 / (60 + i + 1));
                }

                for (var i = 0; i < semanticTop.Count; i++)
                {
                    var item = semanticTop[i];
                    var contribution = 1.0 / (60 + i + 1);

                    if (ranks.TryGetValue(item.Id, out var existing))
                    {
                        ranks[item.Id] = existing with
                        {
                            Score = existing.Score + contribution
                        };
                    }
                    else
                    {
                        ranks[item.Id] = (
                            item.Id,
                            item.Title,
                            item.Classification,
                            item.Version,
                            item.ContentVersion,
                            contribution);
                    }
                }

                var results = ranks.Values
                    .OrderByDescending(x => x.Score)
                    .ThenBy(x => x.Id)
                    .Take(20)
                    .Select(x => new
                    {
                        x.Id,
                        x.Title,
                        x.Classification,
                        x.Version,
                        x.ContentVersion,
                        x.Score
                    })
                    .ToList();

                return Results.Ok(new
                {
                    query = normalizedQuery,
                    mode = "hybrid",
                    count = results.Count,
                    results
                });
            });

        return app;
    }
}