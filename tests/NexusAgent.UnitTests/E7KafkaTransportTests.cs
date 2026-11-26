using NexusAgent.Infrastructure.Messaging;
using Xunit;

namespace NexusAgent.UnitTests;

public sealed class E7KafkaTransportTests
{
    [Fact]
    public void PartitionKeyIsDeterministic()
    {
        var id = Guid.NewGuid();

        Assert.Equal(
            KafkaEventTransport.PartitionKey("tenant-a", id),
            KafkaEventTransport.PartitionKey("tenant-a", id));
    }

    [Fact]
    public void PartitionKeySeparatesTenants()
    {
        var id = Guid.NewGuid();

        Assert.NotEqual(
            KafkaEventTransport.PartitionKey("tenant-a", id),
            KafkaEventTransport.PartitionKey("tenant-b", id));
    }

    [Fact]
    public void PartitionKeySeparatesAggregates()
    {
        Assert.NotEqual(
            KafkaEventTransport.PartitionKey("tenant-a", Guid.NewGuid()),
            KafkaEventTransport.PartitionKey("tenant-a", Guid.NewGuid()));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" tenant")]
    [InlineData("tenant ")]
    [InlineData("tenant\ninvalid")]
    public void InvalidTenantIsRejected(string tenant)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            KafkaEventTransport.PartitionKey(tenant, Guid.NewGuid()));
    }

    [Fact]
    public void EmptyAggregateIsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            KafkaEventTransport.PartitionKey("tenant-a", Guid.Empty));
    }

    [Fact]
    public void OversizedTenantIsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            KafkaEventTransport.PartitionKey(
                new string('a', 201), Guid.NewGuid()));
    }

    [Fact]
    public async Task UnsupportedTopicIsRejected()
    {
        using var transport = new KafkaEventTransport("127.0.0.1:19092");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            transport.PublishAsync(
                "unauthorized-topic",
                "tenant-a",
                Guid.NewGuid(),
                "{}"));
    }

    [Fact]
    public async Task MismatchedIdentityIsRejected()
    {
        using var transport = new KafkaEventTransport("127.0.0.1:19092");

        var id = Guid.NewGuid();

        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            eventId = Guid.NewGuid(),
            tenantId = "tenant-b",
            documentId = id
        });

        await Assert.ThrowsAsync<ArgumentException>(() =>
            transport.PublishAsync(
                KafkaEventTransport.DocumentsTopic,
                "tenant-a",
                id,
                payload));
    }
}