using Confluent.Kafka;
using Npgsql;
using System.Text.Json;

const string topic = "nexusagent.documents.v1";
const string bootstrap = "localhost:19092";

if (args.Length != 1)
    throw new ArgumentException("Expected report output path.");

var connection =
    "Host=127.0.0.1;Port=15432;Database=nexusagent;" +
    "Username=nexusagent;Password=nexusagent_dev_only";

await using var db = new NpgsqlConnection(connection);
await db.OpenAsync();

await using var transaction = await db.BeginTransactionAsync(
    System.Data.IsolationLevel.RepeatableRead);

await using (var readOnly = new NpgsqlCommand(
    "SET TRANSACTION READ ONLY", db, transaction))
{
    await readOnly.ExecuteNonQueryAsync();
}

var legacy = new List<Legacy>();

await using (var command = new NpgsqlCommand("""
    SELECT event_id, tenant_id, document_id,
           document_version, event_type
    FROM public.published_document_events
    WHERE event_type IN (
        'document.created',
        'document.updated',
        'document.deleted'
    )
    AND NOT (payload ? 'schemaVersion')
    ORDER BY event_id
    """, db, transaction))
{
    await using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        legacy.Add(new Legacy(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetGuid(2),
            reader.GetInt32(3),
            reader.GetString(4)));
    }
}

using var admin = new AdminClientBuilder(
    new AdminClientConfig
    {
        BootstrapServers = bootstrap
    }).Build();

var metadata = admin.GetMetadata(
    topic, TimeSpan.FromSeconds(15));

var topicMetadata = metadata.Topics.Single(
    item => item.Topic == topic);

if (topicMetadata.Error.IsError)
    throw new InvalidOperationException(
        topicMetadata.Error.Reason);

using var consumer = new ConsumerBuilder<string, string>(
    new ConsumerConfig
    {
        BootstrapServers = bootstrap,
        GroupId = $"legacy-verification-{Guid.NewGuid():N}",
        EnableAutoCommit = false,
        EnableAutoOffsetStore = false,
        EnablePartitionEof = true,
        IsolationLevel = IsolationLevel.ReadCommitted,
        AutoOffsetReset = AutoOffsetReset.Error
    }).Build();

var partitions = topicMetadata.Partitions
    .Select(item => new TopicPartition(
        topic, new Partition(item.PartitionId)))
    .ToArray();

var bounds = partitions.ToDictionary(
    partition => partition,
    partition => consumer.QueryWatermarkOffsets(
        partition, TimeSpan.FromSeconds(15)));

var records = new List<BrokerRecord>();
var completed = new HashSet<TopicPartition>();
var malformed = 0;
var malformedInventory = new List<object>();

consumer.Assign(partitions.Select(partition =>
    new TopicPartitionOffset(
        partition, bounds[partition].Low)));

using var timeout = new CancellationTokenSource(
    TimeSpan.FromSeconds(90));

while (completed.Count < partitions.Length)
{
    var result = consumer.Consume(timeout.Token);

    if (result is null)
        continue;

    var partition = result.TopicPartition;
    var end = bounds[partition].High.Value;

    if (result.IsPartitionEOF)
    {
        if (result.Offset.Value >= end)
            completed.Add(partition);

        continue;
    }

    if (result.Offset.Value >= end)
    {
        completed.Add(partition);
        continue;
    }

    if (result.Offset.Value < bounds[partition].Low.Value)
        throw new InvalidOperationException(
            "Record outside captured bounds.");

    try
    {
        using var json = JsonDocument.Parse(
            result.Message.Value);

        var value = json.RootElement;

        records.Add(new BrokerRecord(
            value.GetProperty("eventId").GetGuid(),
            result.Message.Key,
            result.Message.Value,
            partition.Partition.Value,
            result.Offset.Value));
    }
    catch (Exception ex) when (
        ex is JsonException or
        KeyNotFoundException or
        FormatException or
        InvalidOperationException or
        ArgumentNullException)
    {
        malformed++;

        var classification = "UNPARSEABLE_EVENT_IDENTITY";

        if (result.Message.Value is null)
        {
            classification = "NULL_PAYLOAD";
        }
        else
        {
            try
            {
                using var invalid = JsonDocument.Parse(
                    result.Message.Value);

                var root = invalid.RootElement;

                classification =
                    root.ValueKind != JsonValueKind.Object
                        ? "NON_OBJECT_JSON"
                        : !root.TryGetProperty("eventId", out var id)
                            ? "MISSING_EVENT_ID"
                            : id.ValueKind != JsonValueKind.String
                                ? "INVALID_EVENT_ID_TYPE"
                                : "INVALID_EVENT_ID_VALUE";
            }
            catch (JsonException)
            {
                classification = "INVALID_JSON";
            }
        }

        malformedInventory.Add(new
        {
            partition = partition.Partition.Value,
            offset = result.Offset.Value,
            classification,
            payloadSha256 = result.Message.Value is null
                ? null
                : Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(
                            result.Message.Value)))
                    .ToLowerInvariant()
        });
    }

    if (consumer.Position(partition).Value >= end)
        completed.Add(partition);
}

foreach (var partition in partitions)
{
    var current = consumer.QueryWatermarkOffsets(
        partition, TimeSpan.FromSeconds(15));

    if (current.Low != bounds[partition].Low ||
        current.High != bounds[partition].High)
    {
        throw new InvalidOperationException(
            "Kafka history changed during verification.");
    }
}

var index = records.ToLookup(record => record.EventId);

var matched = new List<object>();
var missing = new List<Legacy>();
var conflicting = new List<Legacy>();

foreach (var row in legacy)
{
    var candidates = index[row.EventId].ToArray();

    if (candidates.Length == 0)
    {
        missing.Add(row);
        continue;
    }

    var expectedKey =
        $"{row.TenantId.Length}:{row.TenantId}:{row.DocumentId:D}";

    var valid = true;

    foreach (var candidate in candidates)
    {
        try
        {
            using var json = JsonDocument.Parse(candidate.Payload);
            var value = json.RootElement;

            var contentVersion =
                value.GetProperty("contentVersion").GetInt32();

            _ = value.GetProperty("occurredAt").GetDateTimeOffset();

            valid &=
                value.GetProperty("schemaVersion").GetInt32() == 1 &&
                value.GetProperty("eventId").GetGuid() == row.EventId &&
                value.GetProperty("tenantId").GetString() == row.TenantId &&
                value.GetProperty("documentId").GetGuid() == row.DocumentId &&
                value.GetProperty("documentVersion").GetInt32() == row.Version &&
                value.GetProperty("eventType").GetString() == row.EventType &&
                contentVersion >= 1 &&
                contentVersion <= row.Version &&
                candidate.Key == expectedKey &&
                candidate.Payload == candidates[0].Payload;
        }
        catch (Exception ex) when (
            ex is JsonException or
            KeyNotFoundException or
            FormatException or
            InvalidOperationException)
        {
            valid = false;
        }
    }

    if (valid)
    {
        matched.Add(new
        {
            legacy = row,
            key = expectedKey,
            payload = candidates[0].Payload,
            brokerCopies = candidates.Length,
            locations = candidates.Select(candidate => new
            {
                candidate.Partition,
                candidate.Offset
            })
        });
    }
    else
    {
        conflicting.Add(row);
    }
}

var report = new
{
    legacyPublications = legacy.Count,
    readableKafkaRecords = records.Count + malformed,
    parsedKafkaRecords = records.Count,
    malformedKafkaRecords = malformed,
    malformedInventory,
    matchingCandidates = matched.Count,
    missingPublications = missing.Count,
    conflictingPublications = conflicting.Count,
    boundedReadComplete = true,
    historicalMigrationAuthorized = false,
    partitions = bounds.Select(item => new
    {
        partition = item.Key.Partition.Value,
        beginning = item.Value.Low.Value,
        end = item.Value.High.Value
    }),
    matched,
    missing,
    conflicting
};

await File.WriteAllTextAsync(
    args[0],
    JsonSerializer.Serialize(report,
        new JsonSerializerOptions
        {
            WriteIndented = true
        }));

await transaction.RollbackAsync();

Console.WriteLine($"Legacy publications: {legacy.Count}");
Console.WriteLine($"Readable Kafka records: {records.Count + malformed}");
Console.WriteLine($"Malformed Kafka records: {malformed}");
Console.WriteLine($"Matching candidates: {matched.Count}");
Console.WriteLine($"Missing publications: {missing.Count}");
Console.WriteLine($"Conflicting publications: {conflicting.Count}");
Console.WriteLine("BOUNDED_HISTORY_COMPARISON_PASSED");

consumer.Close();

record Legacy(
    Guid EventId,
    string TenantId,
    Guid DocumentId,
    int Version,
    string EventType);

record BrokerRecord(
    Guid EventId,
    string Key,
    string Payload,
    int Partition,
    long Offset);
