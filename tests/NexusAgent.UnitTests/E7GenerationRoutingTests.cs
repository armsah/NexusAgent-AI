using NexusAgent.Infrastructure.Messaging;
using Xunit;

namespace NexusAgent.UnitTests;

public sealed class E7GenerationRoutingTests
{
    [Theory]
    [InlineData(null, "v1")]
    [InlineData("v1", "v1")]
    [InlineData("v2", "v2")]
    public void RoutesBothEventFamilies(
        string? configured, string expected)
    {
        var generation = KafkaStreamGeneration.Resolve(configured);

        Assert.Equal(
            $"nexusagent.documents.{expected}",
            generation.Route(KafkaEventTransport.DocumentsTopic));

        Assert.Equal(
            $"nexusagent.workflows.{expected}",
            generation.Route(KafkaEventTransport.WorkflowsTopic));
    }

    [Theory]
    [InlineData("")]
    [InlineData("v3")]
    [InlineData("V2")]
    [InlineData("v2 ")]
    [InlineData("production")]
    public void RejectsUnknownGenerations(string generation)
    {
        Assert.Throws<ArgumentException>(
            () => KafkaStreamGeneration.Resolve(generation));
    }

    [Theory]
    [InlineData("nexusagent.documents.v2")]
    [InlineData("nexusagent.workflows.v2")]
    [InlineData("nexusagent.analytics.v1")]
    [InlineData("unrelated-topic")]
    public void RejectsPhysicalTopicOverrides(string topic)
    {
        Assert.Throws<ArgumentException>(
            () => KafkaStreamGeneration.Resolve("v2").Route(topic));
    }
}
