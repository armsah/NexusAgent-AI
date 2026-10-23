using Microsoft.EntityFrameworkCore;
using NexusAgent.Application.Questions;
using NexusAgent.Api.Identity;
using NexusAgent.Domain.Documents;
using NexusAgent.Infrastructure.Persistence;

namespace NexusAgent.Api.Questions;

public static class GroundedQuestionEndpoints
{
    public static IEndpointRouteBuilder MapGroundedQuestionEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapPost(
            "/api/questions/answer",
            async (
                GroundedQuestionRequest request,
                NexusAgentDbContext db,
                DevelopmentRequestIdentity identity,
                IServiceProvider services,
                CancellationToken cancellationToken) =>
            {
                var question = request.Question?.Trim();

                if (string.IsNullOrWhiteSpace(question) ||
                    question.Length > 500)
                {
                    return Results.BadRequest(new
                    {
                        error = "Question must contain 1-500 characters."
                    });
                }

                // Every evidence candidate is constrained by server-derived
                // tenant and requester identity before leaving PostgreSQL.
                var evidence = await (
                    from document in db.KnowledgeDocuments.AsNoTracking()
                    join index in db.DocumentSearchIndexes.AsNoTracking()
                        on document.Id equals index.DocumentId
                    where document.TenantId == identity.TenantId
                        && document.Status == DocumentStatus.Active
                        && document.AllowedPrincipalIds.Contains(
                            identity.RequesterId)
                        && index.TenantId == document.TenantId
                        && index.ContentVersion == document.ContentVersion
                        && EF.Functions.ToTsVector("english", index.Content)
                            .Matches(EF.Functions.PlainToTsQuery(
                                "english", question))
                    orderby EF.Functions.ToTsVector("english", index.Content)
                        .Rank(EF.Functions.PlainToTsQuery(
                            "english", question)) descending,
                        document.Id
                    select new
                    {
                        document.Id,
                        document.Title,
                        document.Version,
                        document.ContentVersion,
                        index.Content
                    })
                    .Take(5)
                    .ToListAsync(cancellationToken);

                var groundingEvidence = evidence
                    .Select((item, position) => new GroundingEvidence(
                        $"S{position + 1}",
                        item.Id,
                        item.Title,
                        item.Version,
                        item.ContentVersion,
                        item.Content.Length <= 600
                            ? item.Content
                            : item.Content[..600]))
                    .ToArray();

                GroundedGenerationResult? generated = null;

                var generator = services.GetService(
                    typeof(IGroundedAnswerGenerator))
                    as IGroundedAnswerGenerator;

                if (generator is not null && groundingEvidence.Length > 0)
                {
                    try
                    {
                        generated = await generator.GenerateAsync(
                            question,
                            groundingEvidence,
                            cancellationToken);
                    }
                    catch (OperationCanceledException)
                        when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception)
                    {
                        // A failed configured generator must not silently
                        // produce an unverified answer.
                        return Results.Ok(new GroundedQuestionResponse(
                            question,
                            "Insufficient authorized evidence to answer.",
                            true,
                            []));
                    }
                }

                var decision = GroundedAnswerComposer.Compose(
                    question,
                    groundingEvidence,
                    generated);

                var citations = decision.Citations
                    .Select(item => new GroundedCitation(
                        item.SourceId,
                        item.DocumentId,
                        item.Title,
                        item.DocumentVersion,
                        item.ContentVersion,
                        item.Excerpt))
                    .ToArray();

                return Results.Ok(new GroundedQuestionResponse(
                    question,
                    decision.Answer,
                    decision.Abstained,
                    citations));            });

        return app;
    }
}

public sealed record GroundedQuestionRequest(string Question);

public sealed record GroundedCitation(
    string SourceId,
    Guid DocumentId,
    string Title,
    int DocumentVersion,
    int ContentVersion,
    string Excerpt);

public sealed record GroundedQuestionResponse(
    string Question,
    string Answer,
    bool Abstained,
    IReadOnlyList<GroundedCitation> Citations);