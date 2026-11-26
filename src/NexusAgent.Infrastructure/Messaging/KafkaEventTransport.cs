using Confluent.Kafka;
using System.Text;
using System.Text.Json;

namespace NexusAgent.Infrastructure.Messaging;

public sealed class KafkaEventTransport : IDisposable
{
    public const string DocumentsTopic = "nexusagent.documents.v1";
    public const string WorkflowsTopic = "nexusagent.workflows.v1";

    private readonly IProducer<string, string> producer;

    public KafkaEventTransport(string bootstrapServers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bootstrapServers);

        producer = new ProducerBuilder<string, string>(
            new ProducerConfig
            {
                BootstrapServers = bootstrapServers,
                EnableIdempotence = true,
                Acks = Acks.All,
                MaxInFlight = 5,
                MessageTimeoutMs = 10000,
                RequestTimeoutMs = 5000,
                AllowAutoCreateTopics = false,
                Partitioner = Partitioner.Murmur2Random,
                ClientId = "nexusagent-dotnet"
            }).Build();
    }

    public static string PartitionKey(string tenantId, Guid aggregateId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        if (tenantId.Length > 256 ||
            tenantId != tenantId.Trim() ||
            tenantId.Any(char.IsControl))
            throw new ArgumentException("Invalid tenant identifier.");

        if (aggregateId == Guid.Empty)
            throw new ArgumentException("Invalid aggregate identifier.");

        return $"{tenantId.Length}:{tenantId}:{aggregateId:D}";
    }

    public async Task PublishAsync(
        string topic,
        string tenantId,
        Guid aggregateId,
        string payload,
        CancellationToken cancellationToken = default)
    {
        if (topic is not (DocumentsTopic or WorkflowsTopic))
            throw new ArgumentException("Unsupported event topic.");

        ArgumentException.ThrowIfNullOrWhiteSpace(payload);

        if (Encoding.UTF8.GetByteCount(payload) > 16384)
            throw new ArgumentException("Event payload exceeds limit.");

        using var json = JsonDocument.Parse(payload);
        var root = json.RootElement;

        if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
            root.GetProperty("eventId").GetGuid() == Guid.Empty ||
            root.GetProperty("tenantId").GetString() != tenantId ||
            root.GetProperty(
                topic == DocumentsTopic ? "documentId" : "workflowId")
                .GetGuid() != aggregateId)
            throw new ArgumentException("Event identity mismatch.");

        cancellationToken.ThrowIfCancellationRequested();

        var result = await producer.ProduceAsync(
            topic,
            new Message<string, string>
            {
                Key = PartitionKey(tenantId, aggregateId),
                Value = payload
            },
            cancellationToken);

        if (result.Status != PersistenceStatus.Persisted)
            throw new InvalidOperationException(
                "Kafka did not confirm persistent delivery.");
    }

    public void Dispose() => producer.Dispose();
}