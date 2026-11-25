using System.Net.Http.Json;
using NexusAgent.Application.Search;

namespace NexusAgent.Infrastructure.Search;

public sealed class LocalPythonEmbeddingProvider(
    HttpClient client) : ITextEmbeddingProvider
{
    public string ModelId => "local:qwen3-embedding:4b:1536";

    public int Dimensions => EmbeddingValidation.RequiredDimensions;

    public async Task<float[]> EmbedAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        using var response = await client.PostAsJsonAsync(
            "v1/embed",
            new { text },
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<EmbeddingResponse>(
                cancellationToken: cancellationToken);

        if (result is null ||
            result.Model != "qwen3-embedding:4b" ||
            result.Embedding is null)
        {
            throw new InvalidOperationException(
                "Invalid local embedding response.");
        }

        EmbeddingValidation.Validate(this, result.Embedding);

        return result.Embedding;
    }

    private sealed record EmbeddingResponse(
        string Model,
        float[] Embedding);
}