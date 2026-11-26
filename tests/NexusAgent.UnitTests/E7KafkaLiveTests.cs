using Confluent.Kafka;
using NexusAgent.Application.Documents;
using NexusAgent.Infrastructure.Messaging;
using Xunit;

namespace NexusAgent.UnitTests;

public sealed class E7KafkaLiveTests
{
    [Fact]
    public async Task DocumentEventReachesKafka()
    {
        const string bootstrap = "127.0.0.1:19092";

        var id = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        const string tenant = "e7-acceptance";

        var payload = DocumentEventWireCodec.Serialize(
            new DocumentIngestionEvent(
                EventId: eventId,
                EventType: "document.created",
                TenantId: tenant,
                DocumentId: id,
                DocumentVersion: 1,
                ContentVersion: 1,
                OccurredAt: DateTimeOffset.UtcNow));

        using var consumer = new ConsumerBuilder<string, string>(
            new ConsumerConfig
            {
                BootstrapServers = bootstrap,
                GroupId = $"e7-acceptance-{Guid.NewGuid():N}",
                EnableAutoCommit = false,
                AutoOffsetReset = AutoOffsetReset.Earliest,
                AllowAutoCreateTopics = false
            }).Build();

        consumer.Subscribe(KafkaEventTransport.DocumentsTopic);

        using var transport = new KafkaEventTransport(bootstrap);
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(30));

        await transport.PublishAsync(
            KafkaEventTransport.DocumentsTopic,
            tenant,
            id,
            payload,
            timeout.Token);

        var expectedKey = KafkaEventTransport.PartitionKey(tenant, id);

        while (true)
        {
            var result = consumer.Consume(timeout.Token);

            if (result.Message.Key != expectedKey)
                continue;

            Assert.Equal(payload, result.Message.Value);
            break;
        }

        consumer.Close();
    }
}