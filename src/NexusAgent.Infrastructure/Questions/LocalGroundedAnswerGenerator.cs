using System.Net.Http.Json;
using System.Text.Json.Serialization;
using NexusAgent.Application.Questions;

namespace NexusAgent.Infrastructure.Questions;

public sealed class LocalGroundedAnswerGenerator(
    HttpClient client) : IGroundedAnswerGenerator
{
    public async Task<GroundedGenerationResult> GenerateAsync(
        string question,
        IReadOnlyList<GroundingEvidence> evidence,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentNullException.ThrowIfNull(evidence);

        if (evidence.Count == 0)
        {
            return new GroundedGenerationResult(
                "Insufficient evidence.", Array.Empty<string>());
        }

        var payload = new
        {
            question,
            evidence = evidence.Select(item => new
            {
                source_id = item.SourceId,
                excerpt = item.Excerpt
            }).ToArray()
        };

        using var response = await client.PostAsJsonAsync(
            "v1/grounded", payload, cancellationToken);

        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<LocalResponse>(
                cancellationToken: cancellationToken);

        if (result is null ||
            string.IsNullOrWhiteSpace(result.Answer) ||
            result.CitedSourceIds is null)
        {
            throw new InvalidOperationException(
                "Invalid local inference response.");
        }

        var allowed = evidence.Select(item => item.SourceId)
            .ToHashSet(StringComparer.Ordinal);

        if (result.CitedSourceIds.Any(id => !allowed.Contains(id)))
        {
            throw new InvalidOperationException(
                "Local inference returned an unknown citation.");
        }

        if (result.CitedSourceIds.Length == 0)
        {
            return new GroundedGenerationResult(
                "Insufficient evidence.", Array.Empty<string>());
        }

        var candidate = new GroundedGenerationResult(
            result.Answer, result.CitedSourceIds);

        if (!GroundedAnswerValidation.HasValidCitations(candidate, evidence))
            return new GroundedGenerationResult(
                "Insufficient evidence.", Array.Empty<string>());

        return new GroundedGenerationResult(result.Answer,
            result.CitedSourceIds.Distinct(StringComparer.Ordinal).ToArray());
    }

    private sealed record LocalResponse(
        [property: JsonPropertyName("answer")]
        string Answer,
        [property: JsonPropertyName("cited_source_ids")]
        string[] CitedSourceIds);
}