using System.Net;
using System.Text;
using System.Text.Json;
using NexusAgent.Application.Questions;
using NexusAgent.Infrastructure.Questions;
using NexusAgent.Infrastructure.Search;

namespace NexusAgent.UnitTests;

public sealed class LocalInferenceAdapterTests
{
    private sealed class ResponseHandler(string json)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        json, Encoding.UTF8, "application/json")
                });
        }
    }

    private static HttpClient Client(object payload) =>
        new(new ResponseHandler(JsonSerializer.Serialize(payload)))
        {
            BaseAddress = new Uri("http://127.0.0.1:8001/")
        };

    private static GroundingEvidence[] Evidence() =>
        [new("S1", Guid.NewGuid(), "Architecture", 1, 1,
            "NexusAgent uses PostgreSQL.")];

    [Theory]
    [InlineData("NexusAgent uses PostgreSQL [S1].", true)]
    [InlineData("NexusAgent uses PostgreSQL.", false)]
    [InlineData("NexusAgent uses PostgreSQL [S2].", false)]
    public async Task GroundedAdapterEnforcesInlineCitations(
        string answer, bool accepted)
    {
        using var client = Client(new
        {
            answer,
            cited_source_ids = new[] { "S1" }
        });

        var result = await new LocalGroundedAnswerGenerator(client)
            .GenerateAsync("Which database?", Evidence());

        Assert.Equal(accepted, result.CitedSourceIds.Count > 0);
    }

    [Fact]
    public async Task GroundedAdapterRejectsUnknownSource()
    {
        using var client = Client(new
        {
            answer = "Unsupported [S9].",
            cited_source_ids = new[] { "S9" }
        });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new LocalGroundedAnswerGenerator(client)
                .GenerateAsync("Which database?", Evidence()));
    }

    [Fact]
    public async Task GroundedAdapterAbstainsWithoutEvidence()
    {
        using var client = Client(new { });

        var result = await new LocalGroundedAnswerGenerator(client)
            .GenerateAsync("Which database?", []);

        Assert.Empty(result.CitedSourceIds);
    }

    [Fact]
    public async Task EmbeddingAdapterAcceptsValidVector()
    {
        using var client = Client(new
        {
            model = "qwen3-embedding:4b",
            embedding = Enumerable.Repeat(0.1f, 1536).ToArray()
        });

        var result = await new LocalPythonEmbeddingProvider(client)
            .EmbedAsync("PostgreSQL");

        Assert.Equal(1536, result.Length);
    }

    [Fact]
    public async Task EmbeddingAdapterRejectsWrongModel()
    {
        using var client = Client(new
        {
            model = "unexpected-model",
            embedding = Enumerable.Repeat(0.1f, 1536).ToArray()
        });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new LocalPythonEmbeddingProvider(client)
                .EmbedAsync("PostgreSQL"));
    }

    [Fact]
    public async Task EmbeddingAdapterRejectsInvalidDimensions()
    {
        using var client = Client(new
        {
            model = "qwen3-embedding:4b",
            embedding = new[] { 0.1f }
        });

        var exception = await Record.ExceptionAsync(
            () => new LocalPythonEmbeddingProvider(client)
                .EmbedAsync("PostgreSQL"));

        Assert.NotNull(exception);
    }
}