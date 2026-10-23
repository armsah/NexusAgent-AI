using System.Diagnostics.Metrics;

namespace NexusAgent.Application.Observability;

/// <summary>
/// Dependency-free operational metrics. Token counts and costs are estimates,
/// not provider-reported usage or actual billing.
/// </summary>
public static class GroundedAnswerMetrics
{
    public const string MeterName = "NexusAgent.GroundedAnswers";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    private static readonly Counter<long> Requests =
        Meter.CreateCounter<long>("grounded_answer.requests");

    private static readonly Counter<long> Abstentions =
        Meter.CreateCounter<long>("grounded_answer.abstentions");

    private static readonly Counter<long> GenerationFailures =
        Meter.CreateCounter<long>("grounded_answer.generation_failures");

    private static readonly Histogram<long> RetrievedCandidates =
        Meter.CreateHistogram<long>("grounded_answer.retrieved_candidates");

    private static readonly Histogram<long> SafeCandidates =
        Meter.CreateHistogram<long>("grounded_answer.safe_candidates");

    private static readonly Histogram<long> RejectedCandidates =
        Meter.CreateHistogram<long>("grounded_answer.rejected_candidates");

    private static readonly Histogram<double> EvidenceRetention =
        Meter.CreateHistogram<double>("grounded_answer.evidence_retention_ratio");

    private static readonly Histogram<long> EstimatedInputTokens =
        Meter.CreateHistogram<long>("grounded_answer.estimated_input_tokens");

    private static readonly Histogram<long> EstimatedOutputTokens =
        Meter.CreateHistogram<long>("grounded_answer.estimated_output_tokens");

    private static readonly Histogram<double> EstimatedCostUsd =
        Meter.CreateHistogram<double>("grounded_answer.estimated_cost_usd");

    public static void RecordRequest() => Requests.Add(1);

    public static void RecordRetrieval(int retrieved, int safe)
    {
        if (retrieved < 0 || safe < 0 || safe > retrieved)
        {
            throw new ArgumentOutOfRangeException(
                nameof(safe),
                "Safe evidence must be between zero and retrieved evidence.");
        }

        RetrievedCandidates.Record(retrieved);
        SafeCandidates.Record(safe);
        RejectedCandidates.Record(retrieved - safe);

        EvidenceRetention.Record(
            retrieved == 0 ? 0d : (double)safe / retrieved);
    }

    public static void RecordOutcome(bool abstained)
    {
        if (abstained)
        {
            Abstentions.Add(1);
        }
    }

    public static void RecordGenerationFailure() =>
        GenerationFailures.Add(1);

    public static GenerationEstimate RecordGenerationEstimate(
        string question,
        IEnumerable<string> evidenceExcerpts,
        string answer,
        decimal inputUsdPerMillion = 0m,
        decimal outputUsdPerMillion = 0m)
    {
        var inputCharacters = question.Length;

        foreach (var excerpt in evidenceExcerpts)
        {
            inputCharacters = checked(inputCharacters + excerpt.Length);
        }

        var inputTokens = EstimateTokens(inputCharacters);
        var outputTokens = EstimateTokens(answer.Length);

        if (inputUsdPerMillion < 0 || outputUsdPerMillion < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(inputUsdPerMillion),
                "Token prices cannot be negative.");
        }

        var estimatedUsd =
            (inputTokens * inputUsdPerMillion +
             outputTokens * outputUsdPerMillion) / 1_000_000m;

        EstimatedInputTokens.Record(inputTokens);
        EstimatedOutputTokens.Record(outputTokens);
        EstimatedCostUsd.Record((double)estimatedUsd);

        return new GenerationEstimate(
            inputTokens,
            outputTokens,
            estimatedUsd);
    }

    public static long EstimateTokens(int characters)
    {
        if (characters < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(characters));
        }

        // Approximation only: four characters per token, rounded up.
        return ((long)characters + 3) / 4;
    }
}

public sealed record GenerationEstimate(
    long EstimatedInputTokens,
    long EstimatedOutputTokens,
    decimal EstimatedCostUsd);