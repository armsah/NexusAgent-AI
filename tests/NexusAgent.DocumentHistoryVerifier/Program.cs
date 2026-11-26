using Confluent.Kafka;
using Npgsql;
using System.Text.Json;

if (args.Length is not (3 or 4))
    throw new ArgumentException("Expected database, topic and count.");

var database = args[0];
var topic = args[1];
var expectedCount = int.Parse(args[2]);

if (!database.StartsWith("nexus_doc_crash_") ||
    !topic.StartsWith("nexus-acceptance-"))
    throw new InvalidOperationException("Unsafe acceptance target.");

var connection =
    $"Host=127.0.0.1;Port=15432;Database={database};" +
    "Username=nexusagent;Password=nexusagent_dev_only";

await using var db = new NpgsqlConnection(connection);
await db.OpenAsync();

await using var command = new NpgsqlCommand("""
    SELECT event_id, partition_key, wire_payload,
           acknowledged_at IS NOT NULL
    FROM kafka_document_publications
    """, db);

await using var reader = await command.ExecuteReaderAsync();

if (!await reader.ReadAsync())
    throw new InvalidOperationException("Missing durable intent.");

var eventId = reader.GetGuid(0);
var key = reader.GetString(1);
var payload = reader.GetString(2);
var acknowledged = reader.GetBoolean(3);

if (await reader.ReadAsync())
    throw new InvalidOperationException("Unexpected journal records.");

using var consumer = new ConsumerBuilder<string, string>(
    new ConsumerConfig
    {
        BootstrapServers = "localhost:19092",
        GroupId = $"document-history-{Guid.NewGuid():N}",
        EnableAutoCommit = false,
        IsolationLevel = IsolationLevel.ReadCommitted
    }).Build();

var partition = new TopicPartition(topic, new Partition(0));

var bounds = consumer.QueryWatermarkOffsets(
    partition, TimeSpan.FromSeconds(15));

if (bounds.Low.Value != 0)
    throw new InvalidOperationException("Incomplete source history.");

consumer.Assign(new TopicPartitionOffset(
    partition, bounds.Low));

var records = 0;

using var timeout = new CancellationTokenSource(
    TimeSpan.FromSeconds(30));

while (consumer.Position(partition).Value < bounds.High.Value)
{
    var record = consumer.Consume(timeout.Token);

    if (record.Offset.Value >= bounds.High.Value)
        break;

    if (record.Message.Key != key ||
        record.Message.Value != payload)
        throw new InvalidOperationException(
            "Kafka differs from authoritative journal.");

    using var json = JsonDocument.Parse(record.Message.Value);

    if (json.RootElement.GetProperty("eventId").GetGuid() != eventId)
        throw new InvalidOperationException("Event identity changed.");

    records++;
}

if (records != expectedCount)
    throw new InvalidOperationException(
        $"Expected {expectedCount} records; found {records}.");

if (acknowledged != (expectedCount == 2))
    throw new InvalidOperationException(
        "Unexpected acknowledgement state.");

Console.WriteLine(JsonSerializer.Serialize(new
{
    eventId,
    key,
    records,
    acknowledged,
    exactPayloadMatch = true,
    boundedHistoryComplete = true
}));

if (args.Length == 4)
{
    if (!acknowledged || records != 2)
        throw new InvalidOperationException(
            "Only verified acknowledged history can be exported.");

    var inventory = new
    {
        eventId,
        key,
        payload,
        records,
        acknowledged,
        exactPayloadMatch = true,
        boundedHistoryComplete = true
    };

    await File.WriteAllTextAsync(
        args[3],
        JsonSerializer.Serialize(inventory));
}

consumer.Close();
