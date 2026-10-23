using System.Text.RegularExpressions;

namespace NexusAgent.Application.Questions;

public static class GroundedAnswerComposer
{
    private const string Abstention =
        "Insufficient authorized evidence to answer.";

    public static GroundedAnswerDecision Compose(
        string question,
        IReadOnlyList<GroundingEvidence> evidence,
        GroundedGenerationResult? generated = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentNullException.ThrowIfNull(evidence);

        if (evidence.Count == 0)
        {
            return new GroundedAnswerDecision(
                Abstention,
                true,
                []);
        }

        if (generated is not null)
        {
            if (!GroundedAnswerValidation.HasValidCitations(
                generated,
                evidence))
            {
                return new GroundedAnswerDecision(
                    Abstention,
                    true,
                    []);
            }

            // Citation references are validated, but this does not
            // independently prove factual entailment.
            return new GroundedAnswerDecision(
                generated.Answer,
                false,
                evidence.Where(item =>
                    generated.CitedSourceIds.Contains(
                        item.SourceId,
                        StringComparer.Ordinal)).ToArray());
        }

        var answer = string.Join(
            "\n\n",
            evidence.Select(item =>
                $"[{item.SourceId}] {item.Excerpt}"));

        return new GroundedAnswerDecision(
            answer,
            false,
            evidence);
    }
}

public sealed record GroundedAnswerDecision(
    string Answer,
    bool Abstained,
    IReadOnlyList<GroundingEvidence> Citations);