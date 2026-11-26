using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NexusAgent.Application.Abstractions;
using NexusAgent.Application.Agents;
using NexusAgent.Application.Questions;
using NexusAgent.Application.Search;
using NexusAgent.Domain.Documents;
using NexusAgent.Infrastructure.Persistence;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace NexusAgent.Infrastructure.Questions;

public sealed class PostgresEvidenceRetriever(
    NexusAgentDbContext db,
    IWorkflowRepository workflows,
    IServiceProvider services)
    : IAuthorizedEvidenceRetriever
{
    public async Task<IReadOnlyList<GroundingEvidence>> RetrieveAsync(
        Guid workflowId,
        string tenantId,
        string question,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var workflow = await workflows.GetByIdAsync(
            workflowId, cancellationToken);

        if (workflow is null ||
            !string.Equals(workflow.TenantId, tenantId,
                StringComparison.Ordinal) ||
            !string.Equals(workflow.Request, question,
                StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                "Evidence access denied.");
        }

        var requesterId = workflow.RequesterId;

        var authorized =
            from document in db.KnowledgeDocuments.AsNoTracking()
            join index in db.DocumentSearchIndexes.AsNoTracking()
                on document.Id equals index.DocumentId
            where document.TenantId == tenantId
                && document.Status == DocumentStatus.Active
                && document.AllowedPrincipalIds.Contains(requesterId)
                && index.TenantId == document.TenantId
                && index.ContentVersion == document.ContentVersion
            select new
            {
                document.Id,
                document.Title,
                document.Version,
                document.ContentVersion,
                index.Content
            };

        var lexical = await authorized
            .Where(item =>
                EF.Functions.ToTsVector("english", item.Content)
                    .Matches(EF.Functions.PlainToTsQuery(
                        "english", question)))
            .OrderByDescending(item =>
                EF.Functions.ToTsVector("english", item.Content)
                    .Rank(EF.Functions.PlainToTsQuery(
                        "english", question)))
            .ThenBy(item => item.Id)
            .Select(item => item.Id)
            .Take(60)
            .ToListAsync(cancellationToken);

        var semantic = new List<Guid>();

        var provider = services.GetService<ITextEmbeddingProvider>();

        if (provider is not null)
        {
            var embedding = await provider.EmbedAsync(
                question, cancellationToken);

            EmbeddingValidation.Validate(provider, embedding);

            var queryVector = new Vector(embedding);
            var model = provider.ModelId;

            semantic = await (
                from item in authorized
                join vector in db.DocumentVectorIndexes.AsNoTracking()
                    on item.Id equals vector.DocumentId
                where vector.TenantId == tenantId
                    && vector.ContentVersion == item.ContentVersion
                    && vector.EmbeddingModel == model
                orderby vector.Embedding.CosineDistance(queryVector),
                    item.Id
                select item.Id)
                .Take(60)
                .ToListAsync(cancellationToken);
        }

        var scores = new Dictionary<Guid, double>();

        void Accumulate(IReadOnlyList<Guid> ranked)
        {
            for (var i = 0; i < ranked.Count; i++)
            {
                scores.TryGetValue(ranked[i], out var previous);
                scores[ranked[i]] = previous + 1.0 / (61 + i);
            }
        }

        Accumulate(lexical);
        Accumulate(semantic);

        var selected = scores
            .OrderByDescending(item => item.Value)
            .ThenBy(item => item.Key)
            .Take(5)
            .Select(item => item.Key)
            .ToArray();

        if (selected.Length == 0)
            return Array.Empty<GroundingEvidence>();

        var candidates = await authorized
            .Where(item => selected.Contains(item.Id))
            .ToListAsync(cancellationToken);

        var ordered = candidates
            .OrderBy(item => Array.IndexOf(selected, item.Id))
            .Where(item =>
                !PromptInjectionDefense.IsSuspicious(item.Content) &&
                !PromptInjectionDefense.IsSuspicious(item.Title))
            .Select((item, position) =>
                new GroundingEvidence(
                    $"S{position + 1}",
                    item.Id,
                    item.Title,
                    item.Version,
                    item.ContentVersion,
                    item.Content.Length <= 600
                        ? item.Content
                        : item.Content[..600]))
            .ToArray();

        cancellationToken.ThrowIfCancellationRequested();

        return PromptInjectionDefense.FilterEvidence(ordered)
            .ToArray();
    }
}