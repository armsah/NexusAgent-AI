using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using NexusAgent.Infrastructure.Persistence;
using NexusAgent.Infrastructure.Persistence.Outbox;
using NexusAgent.Infrastructure.Messaging;
using System.Text.Json;

if (args.Length != 7)
    throw new ArgumentException("Invalid probe arguments.");

var mode = args[0];
var database = args[1];
var topic = args[2];
var eventId = Guid.Parse(args[3]);
var documentId = Guid.Parse(args[4]);
var tenant = args[5];
var occurredAt = DateTimeOffset.Parse(args[6]);

if (!database.StartsWith("nexus_doc_crash_", StringComparison.Ordinal))
    throw new InvalidOperationException("Unsafe database.");

if (!topic.StartsWith("nexus-acceptance-", StringComparison.Ordinal))
    throw new InvalidOperationException("Unsafe topic.");

const string bootstrap = "localhost:19092";

Environment.SetEnvironmentVariable(
    "NEXUS_KAFKA_BOOTSTRAP_SERVERS", bootstrap);

var connection =
    $"Host=127.0.0.1;Port=15432;Database={database};" +
    "Username=nexusagent;Password=nexusagent_dev_only";

var options = new DbContextOptionsBuilder<NexusAgentDbContext>()
    .UseNpgsql(connection, builder => builder.UseVector())
    .Options;

await using var db = new NexusAgentDbContext(options);

if (mode == "initialize")
{
    await db.Database.MigrateAsync();
    return;
}

using var producer = new ProducerBuilder<string, string>(
    new ProducerConfig
    {
        BootstrapServers = bootstrap,
        EnableIdempotence = true,
        Acks = Acks.All,
        MessageTimeoutMs = 10000
    }).Build();

var publisher = new PostgresDocumentIngestionEventPublisher(
    db,
    async (_, tenantId, aggregateId, payload, ct) =>
    {
        var delivery = await producer.ProduceAsync(
            topic,
            new Message<string, string>
            {
                Key = KafkaEventTransport.PartitionKey(
                    tenantId, aggregateId),
                Value = payload
            },
            ct);

        if (delivery.Status != PersistenceStatus.Persisted)
            throw new InvalidOperationException(
                "Broker did not persist the event.");
    },
    async ct =>
    {
        if (mode == "crash")
        {
            Console.WriteLine("BROKER_ACK_BEFORE_JOURNAL_ACK");
            Console.Out.Flush();

            await Task.Delay(Timeout.Infinite, ct);
        }
    });

if (mode is not ("crash" or "recover"))
    throw new ArgumentException("Unsupported probe mode.");

var payload = JsonSerializer.Serialize(new
{
    contentVersion = 1,
    occurredAt
});

await publisher.PublishAsync(
    eventId,
    "document.created",
    tenant,
    documentId,
    1,
    payload);

Console.WriteLine("PUBLICATION_COMPLETED");
