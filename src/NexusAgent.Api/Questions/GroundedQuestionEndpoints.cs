using Microsoft.EntityFrameworkCore;
using NexusAgent.Application.Questions;
using NexusAgent.Application.Observability;
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
                using var requestActivity = NexusAgentTelemetry.StartActivity(
                    "grounded_answer.request");

                GroundedAnswerMetrics.RecordRequest();

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
                using var retrievalActivity = NexusAgentTelemetry.StartActivity(
                    "grounded_answer.retrieval");

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
                retrievalActivity?.SetTag("retrieval.candidate_count", evidence.Count);
                retrievalActivity?.Dispose();

                var groundingEvidence = evidence.Select((item, position) => new GroundingEvidence(
                        $"S{position + 1}",
                        item.Id,
                        item.Title,
                        item.Version,
                        item.ContentVersion,
                        item.Content.Length <= 600
                            ? item.Content
                            : item.Content[..600]))
                    .ToArray();

                using var screeningActivity = NexusAgentTelemetry.StartActivity(
                    "grounded_answer.evidence_screening");

                var safeEvidence = PromptInjectionDefense.FilterEvidence(
                    groundingEvidence);

                screeningActivity?.SetTag(
                    "screening.safe_count", safeEvidence.Count);

                screeningActivity?.SetTag(
                    "screening.rejected_count",
                    groundingEvidence.Length - safeEvidence.Count);
                screeningActivity?.Dispose();

                GroundedAnswerMetrics.RecordRetrieval(
                    groundingEvidence.Length,
                    safeEvidence.Count);

                GroundedGenerationResult? generated = null;

                var generator = services.GetService(
                    typeof(IGroundedAnswerGenerator))
                    as IGroundedAnswerGenerator;

                if (generator is not null && safeEvidence.Count > 0)
                {
                    try
                    {
                        using var generationActivity = NexusAgentTelemetry.StartActivity(
                            "grounded_answer.generation");

                        generated = await generator.GenerateAsync(
                            question,
                            safeEvidence,
                            cancellationToken);

                        generationActivity?.Dispose();
                    }
                    catch (OperationCanceledException)
                        when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception)
                    {
                        GroundedAnswerMetrics.RecordGenerationFailure();
                        GroundedAnswerMetrics.RecordOutcome(true);

                        // A failed configured generator must not silently
                        // produce an unverified answer.
                        return Results.Ok(new GroundedQuestionResponse(
                            question,
                            "Insufficient authorized evidence to answer.",
                            true,
                            []));
                    }
                }

                using var verificationActivity = NexusAgentTelemetry.StartActivity(
                    "grounded_answer.verification");

                var decision = GroundedAnswerComposer.Compose(
                    question,
                    safeEvidence,
                    generated);

                verificationActivity?.SetTag("answer.abstained", decision.Abstained);
                verificationActivity?.SetTag(
                    "answer.citation_count", decision.Citations.Count);

                verificationActivity?.Dispose();

                requestActivity?.SetTag("answer.abstained", decision.Abstained);

                GroundedAnswerMetrics.RecordOutcome(decision.Abstained);

                if (generated is not null)
                {
                    var estimate = GroundedAnswerMetrics.RecordGenerationEstimate(
                        question,
                        safeEvidence.Select(item => item.Excerpt),
                        generated.Answer);

                    requestActivity?.SetTag(
                        "generation.estimated_input_tokens",
                        estimate.EstimatedInputTokens);

                    requestActivity?.SetTag(
                        "generation.estimated_output_tokens",
                        estimate.EstimatedOutputTokens);
                }

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
                    citations));
            });

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