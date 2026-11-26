using Pgvector.EntityFrameworkCore;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using NexusAgent.Infrastructure.Messaging;
using NexusAgent.Infrastructure.Persistence;
using Xunit;

namespace NexusAgent.UnitTests;

public sealed class E7WorkflowDispatchTests
{
    [Fact]
    public async Task BrokerFailureRetainsEventsAndRecoveryPreservesOrder()
    {
        var database = $"nexus_e7_{Guid.NewGuid():N}";
        const string admin =
            "Host=127.0.0.1;Port=15432;Database=nexusagent;" +
            "Username=nexusagent;Password=nexusagent_dev_only";

        var connection = admin.Replace(
            "Database=nexusagent", $"Database={database}");

        var options = new DbContextOptionsBuilder<NexusAgentDbContext>()
            .UseNpgsql(connection, options => options.UseVector())
            .Options;

        await using var db = new NexusAgentDbContext(options);

        var workflowId = Guid.NewGuid();
        var tenantId = $"e7-{Guid.NewGuid():N}";
        var started = DateTimeOffset.UtcNow.AddSeconds(-2);
        var completed = started.AddSeconds(1);

        try
        {
            await using (var adminConnection =
                new Npgsql.NpgsqlConnection(admin))
            {
                await adminConnection.OpenAsync();

                await using var create = adminConnection.CreateCommand();
                create.CommandText = $"CREATE DATABASE \"{database}\"";
                await create.ExecuteNonQueryAsync();
            }

            await using (var extensionConnection =
                new Npgsql.NpgsqlConnection(connection))
            {
                await extensionConnection.OpenAsync();

                await using var extension =
                    extensionConnection.CreateCommand();

                extension.CommandText =
                    "CREATE EXTENSION IF NOT EXISTS vector";

                await extension.ExecuteNonQueryAsync();
            }

            await db.Database.MigrateAsync();

            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO workflows
                    (id, requester_id, tenant_id, request, status,
                     created_at_utc, updated_at_utc)
                VALUES
                    ({workflowId}, 'acceptance', {tenantId},
                     'E7 acceptance', 'Created', {started}, {started})
                """);

            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE workflows
                SET status = 'Running', updated_at_utc = {started}
                WHERE id = {workflowId}
                """);

            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE workflows
                SET status = 'Completed', updated_at_utc = {completed}
                WHERE id = {workflowId}
                """);

            using (var unavailable = new KafkaEventTransport("127.0.0.1:1"))
            {
                var failing = new PostgresWorkflowEventDispatcher(
                    db, unavailable);

                Assert.True(await failing.DispatchNextAsync());
            }

            var pending = await db.Database.SqlQueryRaw<int>(
                """
                SELECT count(*)::integer AS "Value"
                FROM kafka_workflow_events
                WHERE published_at IS NULL
                """).SingleAsync();

            Assert.Equal(2, pending);

            var failures = await db.Database.SqlQueryRaw<int>(
                """
                SELECT count(*)::integer AS "Value"
                FROM kafka_workflow_events
                WHERE attempts = 1 AND last_error IS NOT NULL
                  AND sequence = 1
                """).SingleAsync();

            Assert.Equal(1, failures);

            await db.Database.ExecuteSqlRawAsync(
                """
                UPDATE kafka_workflow_events
                SET next_attempt_at = clock_timestamp()
                WHERE published_at IS NULL
                """);

            using var transport =
                new KafkaEventTransport("127.0.0.1:19092");

            var dispatcher = new PostgresWorkflowEventDispatcher(
                db, transport);

            Assert.True(await dispatcher.DispatchNextAsync());
            Assert.True(await dispatcher.DispatchNextAsync());
            Assert.False(await dispatcher.DispatchNextAsync());

            var published = await db.Database.SqlQueryRaw<int>(
                """
                SELECT count(*)::integer AS "Value"
                FROM kafka_workflow_events
                WHERE published_at IS NOT NULL
                """).SingleAsync();

            Assert.Equal(2, published);

            using var consumer = new ConsumerBuilder<string, string>(
                new ConsumerConfig
                {
                    BootstrapServers = "127.0.0.1:19092",
                    GroupId = $"e7-dispatch-{Guid.NewGuid():N}",
                    EnableAutoCommit = false,
                    AutoOffsetReset = AutoOffsetReset.Earliest
                }).Build();

            consumer.Subscribe(KafkaEventTransport.WorkflowsTopic);

            var key = KafkaEventTransport.PartitionKey(
                tenantId, workflowId);

            using var timeout =
                new CancellationTokenSource(TimeSpan.FromSeconds(30));

            var sequences = new List<long>();

            while (sequences.Count < 2)
            {
                var message = consumer.Consume(timeout.Token).Message;

                if (message.Key != key)
                    continue;

                using var json =
                    System.Text.Json.JsonDocument.Parse(message.Value);

                sequences.Add(
                    json.RootElement.GetProperty("sequence").GetInt64());
            }

            Assert.Equal(new long[] { 1, 2 }, sequences);

            consumer.Close();
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}