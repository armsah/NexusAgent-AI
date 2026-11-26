using System.Text.RegularExpressions;

namespace NexusAgent.Application.Questions;

public static class ExtractiveGroundingVerifier
{
    public static bool IsSupported(
        GroundedGenerationResult generated,
        IReadOnlyList<GroundingEvidence> evidence)
    {
        if (!GroundedAnswerValidation.HasValidCitations(
                generated, evidence))
            return false;

        if (PromptInjectionDefense.IsSuspicious(generated.Answer))
            return false;

        var claims = generated.Answer.Split(
            ['\n', '\r'],
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);

        if (claims.Length == 0)
            return false;

        foreach (var claim in claims)
        {
            var match = Regex.Match(
                claim,
                @"^(?<text>.+?)\s+\[(?<id>S[1-9][0-9]*)\][.!?]?$");

            if (!match.Success)
                return false;

            var text = Normalize(match.Groups["text"].Value);
            var id = match.Groups["id"].Value;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            var matches = evidence.Where(
                item => item.SourceId == id).ToArray();

            if (matches.Length != 1 ||
                PromptInjectionDefense.IsSuspicious(matches[0].Excerpt))
                return false;

            var sentences = Regex.Split(
                matches[0].Excerpt, @"(?<=[.!?])\s+|\r?\n");

            if (!sentences.Any(sentence =>
                string.Equals(
                    Normalize(sentence),
                    text,
                    StringComparison.Ordinal)))
                return false;
        }

        return true;
    }

    private static string Normalize(string text) =>
        Regex.Replace(text.Trim(), @"\s+", " ")
            .TrimEnd('.', '!', '?');
}