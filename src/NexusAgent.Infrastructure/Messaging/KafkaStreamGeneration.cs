namespace NexusAgent.Infrastructure.Messaging;

public sealed class KafkaStreamGeneration
{
    public string Name { get; }

    private KafkaStreamGeneration(string name)
    {
        Name = name;
    }

    public static KafkaStreamGeneration Resolve(string? name)
        => name switch
        {
            null or "v1" => new("v1"),
            "v2" => new("v2"),
            _ => throw new ArgumentException(
                "Unsupported Kafka stream generation.", nameof(name))
        };

    public static KafkaStreamGeneration FromEnvironment()
        => Resolve(Environment.GetEnvironmentVariable(
            "NEXUS_STREAM_GENERATION"));

    public string Route(string logicalTopic)
        => logicalTopic switch
        {
            KafkaEventTransport.DocumentsTopic =>
                $"nexusagent.documents.{Name}",
            KafkaEventTransport.WorkflowsTopic =>
                $"nexusagent.workflows.{Name}",
            _ => throw new ArgumentException(
                "Unsupported logical event topic.", nameof(logicalTopic))
        };
}
