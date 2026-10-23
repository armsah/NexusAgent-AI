using System.Text.RegularExpressions;

namespace NexusAgent.Application.Questions;

public static class GroundedAnswerValidation
{
    private static readonly Regex CitationPattern = new(
        @"\[(S[1-9][0-9]*)\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool HasValidCitations(
        GroundedGenerationResult result,
        IReadOnlyList<GroundingEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(evidence);

        if (string.IsNullOrWhiteSpace(result.Answer) ||
            result.CitedSourceIds is null ||
            result.CitedSourceIds.Count == 0 ||
            evidence.Count == 0)
        {
            return false;
        }

        var allowed = evidence
            .Select(item => item.SourceId)
            .ToHashSet(StringComparer.Ordinal);

        var declared = result.CitedSourceIds
            .ToHashSet(StringComparer.Ordinal);

        if (declared.Count != result.CitedSourceIds.Count ||
            !declared.IsSubsetOf(allowed))
        {
            return false;
        }

        var referenced = CitationPattern
            .Matches(result.Answer)
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        return referenced.Count > 0 &&
            referenced.SetEquals(declared);
    }
}