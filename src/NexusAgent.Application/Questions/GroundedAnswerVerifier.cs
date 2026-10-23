using System.Text.RegularExpressions;

namespace NexusAgent.Application.Questions;

public static class GroundedAnswerVerifier
{
    private static readonly Regex CitationPattern = new(
        @"\[S[1-9][0-9]*\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WordPattern = new(
        @"[\p{L}\p{N}]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> StopWords = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "are", "as", "at", "be", "by",
        "for", "from", "in", "is", "it", "of", "on",
        "or", "that", "the", "their", "these", "this",
        "to", "was", "were", "with"
    };

    public static bool IsSupported(
        GroundedGenerationResult generated,
        IReadOnlyList<GroundingEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(generated);
        ArgumentNullException.ThrowIfNull(evidence);

        if (!GroundedAnswerValidation.HasValidCitations(generated, evidence))
        {
            return false;
        }

        var citedEvidence = evidence
            .Where(item => generated.CitedSourceIds.Contains(
                item.SourceId,
                StringComparer.Ordinal))
            .ToArray();

        var evidenceWords = citedEvidence
            .SelectMany(item => Words(item.Excerpt))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Narrow obligation normalization:
        // "must use approved devices" supports
        // "approved devices are required".
        // This is not general semantic entailment.
        if (citedEvidence.Any(item =>
            Regex.IsMatch(
                item.Excerpt,
                @"\bmust\s+use\b",
                RegexOptions.IgnoreCase |
                RegexOptions.CultureInvariant)))
        {
            evidenceWords.Add("required");
        }
        if (evidenceWords.Count == 0)
        {
            return false;
        }

        var claims = generated.Answer
            .Split(
                ['.', '!', '?', '\n', '\r'],
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);

        if (claims.Length == 0)
        {
            return false;
        }

        foreach (var claim in claims)
        {
            if (!CitationPattern.IsMatch(claim))
            {
                return false;
            }

            var claimWords = Words(CitationPattern.Replace(claim, ""))
                .ToArray();

            if (claimWords.Length == 0 ||
                claimWords.Any(word => !evidenceWords.Contains(word)))
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<string> Words(string value)
    {
        return WordPattern.Matches(value)
            .Select(match => match.Value)
            .Where(word =>
                word.Length > 1 &&
                !StopWords.Contains(word));
    }
}