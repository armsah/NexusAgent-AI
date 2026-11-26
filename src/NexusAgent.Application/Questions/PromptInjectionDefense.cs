using System.Text;
using System.Globalization;
using System.Text.RegularExpressions;

namespace NexusAgent.Application.Questions;

public static class PromptInjectionDefense
{
    private static readonly Regex[] Patterns =
    [
        new(
            @"\b(ignore|disregard|override|forget|bypass)\b.{0,100}\b(previous|prior|system|developer|instructions|rules|policy)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase |
            RegexOptions.CultureInvariant | RegexOptions.Singleline,
            TimeSpan.FromMilliseconds(100)),

        new(
            @"\b(system|developer)\s*(message|prompt|instructions)\s*:",
            RegexOptions.Compiled | RegexOptions.IgnoreCase |
            RegexOptions.CultureInvariant | RegexOptions.Singleline,
            TimeSpan.FromMilliseconds(100)),

        new(
            @"\b(reveal|print|expose|leak|disclose)\b.{0,100}\b(system prompt|api key|secrets?|credentials)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase |
            RegexOptions.CultureInvariant | RegexOptions.Singleline,
            TimeSpan.FromMilliseconds(100)),

        new(
            @"\b(execute|run)\b.{0,60}\b(shell|command|powershell|bash|script)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase |
            RegexOptions.CultureInvariant | RegexOptions.Singleline,
            TimeSpan.FromMilliseconds(100)),

        new(
            @"\b(send|upload|exfiltrate)\b.{0,100}\b(data|documents|secrets|credentials)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase |
            RegexOptions.CultureInvariant | RegexOptions.Singleline,
            TimeSpan.FromMilliseconds(100)),

        new(
            @"\b(you are now|act as|pretend to be)\b.{0,80}\b(system|developer|administrator|assistant)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase |
            RegexOptions.CultureInvariant | RegexOptions.Singleline,
            TimeSpan.FromMilliseconds(100)),

        new(
            @"\[\s*(im_start|im_end|/?inst)\s*\]|<\s*\|(im_start|im_end|system|assistant|developer)\|\s*>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase |
            RegexOptions.CultureInvariant | RegexOptions.Singleline,
            TimeSpan.FromMilliseconds(100))
    ];

    public static bool IsSuspicious(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        try
        {
            var normalized = content.Normalize(NormalizationForm.FormKC);
            var cleaned = new StringBuilder(normalized.Length);

            foreach (var rune in normalized.EnumerateRunes())
            {
                if (Rune.GetUnicodeCategory(rune) != UnicodeCategory.Format)
                    cleaned.Append(rune.ToString());
            }

            return Patterns.Any(pattern => pattern.IsMatch(cleaned.ToString()));
        }
        catch (Exception exception) when (exception is RegexMatchTimeoutException or ArgumentException)
        {
            // Fail closed when inspection cannot finish.
            return true;
        }
    }

    public static IReadOnlyList<GroundingEvidence> FilterEvidence(
        IReadOnlyList<GroundingEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        return evidence
            .Where(item =>
                !IsSuspicious(item.Title) &&
                !IsSuspicious(item.Excerpt))
            .ToArray();
    }
}