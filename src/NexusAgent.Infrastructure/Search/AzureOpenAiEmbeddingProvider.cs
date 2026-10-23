using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using NexusAgent.Application.Search;

namespace NexusAgent.Infrastructure.Search;

public sealed class AzureOpenAiEmbeddingProvider : ITextEmbeddingProvider
{
    private readonly HttpClient _httpClient;
    private readonly Uri _endpoint;
    private readonly string _apiKey;

    public AzureOpenAiEmbeddingProvider(
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _httpClient = httpClient;

        var endpoint = configuration["Embeddings:AzureOpenAI:Endpoint"];
        var deployment = configuration["Embeddings:AzureOpenAI:Deployment"];
        var apiKey = configuration["Embeddings:AzureOpenAI:ApiKey"];

        if (string.IsNullOrWhiteSpace(endpoint) ||
            string.IsNullOrWhiteSpace(deployment) ||
            string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Azure OpenAI embedding endpoint, deployment, and API key must be configured.");
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(baseUri.UserInfo) ||
            !string.IsNullOrEmpty(baseUri.Query) ||
            !string.IsNullOrEmpty(baseUri.Fragment))
        {
            throw new InvalidOperationException(
                "Azure OpenAI embedding endpoint must be an HTTPS base URL without credentials, query, or fragment.");
        }

        if (deployment.Any(character =>
                !char.IsAsciiLetterOrDigit(character) &&
                character is not '-' and not '_'))
        {
            throw new InvalidOperationException(
                "Azure OpenAI deployment name contains unsupported characters.");
        }

        _endpoint = new Uri(
            baseUri.ToString().TrimEnd('/') +
            "/openai/deployments/" +
            Uri.EscapeDataString(deployment) +
            "/embeddings?api-version=2024-10-21");

        _apiKey = apiKey;
        ModelId = deployment;
    }

    public string ModelId { get; }

    public int Dimensions => EmbeddingValidation.RequiredDimensions;

    public async Task<float[]> EmbedAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            _endpoint);

        request.Headers.Add("api-key", _apiKey);

        request.Content = JsonContent.Create(new
        {
            input = text,
            dimensions = Dimensions
        });

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var responseStream = await response.Content
            .ReadAsStreamAsync(cancellationToken);

        using var json = await JsonDocument.ParseAsync(
            responseStream,
            cancellationToken: cancellationToken);

        if (!json.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array ||
            data.GetArrayLength() != 1 ||
            !data[0].TryGetProperty("embedding", out var values) ||
            values.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "Azure OpenAI returned an invalid embedding response.");
        }

        var embedding = values.EnumerateArray()
            .Select(value => value.GetSingle())
            .ToArray();

        EmbeddingValidation.Validate(this, embedding);

        return embedding;
    }
}