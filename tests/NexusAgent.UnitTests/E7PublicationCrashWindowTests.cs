using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using NexusAgent.Domain.Workflows;
using NexusAgent.Infrastructure.Messaging;
using NexusAgent.Infrastructure.Persistence;
using Npgsql;
using Xunit;

namespace NexusAgent.UnitTests;

public sealed class E7PublicationCrashWindowTests
{
    [Fact]
    public async Task BrokerAcknowledgementBeforeDatabaseCommitPreservesReplayIdentity()
    {
        const string adminConnection =
            "Host=127.0.0.1;Port=15432;Database=nexusagent;" +
            "Username=nexusagent;Password=nexusagent_dev_only";

        var database = $"nexus_e7_crash_{Guid.NewGuid():N}";
        var connection = adminConnection.Replace(
            "Database=nexusagent",
            $"Database={database}");

        var workflowId = Guid.NewGuid();
        var tenant = $"e7-publication-crash-{Guid.NewGuid():N}";

        var options =
            new DbContextOptionsBuilder<NexusAgentDbContext>()
                .UseNpgsql(connection, builder => builder.UseVector())
                .Options;

        await using var db = new NexusAgentDbContext(options);

        try
        {
            await using (var admin = new NpgsqlConnection(adminConnection))
            {
                await admin.OpenAsync();

                await using var create = new NpgsqlCommand(
                    $"CREATE DATABASE \"{database}\"", admin);

                await create.ExecuteNonQueryAsync();
            }

            await using (var target = new NpgsqlConnection(connection))
            {
                await target.OpenAsync();

                await using var extension = new NpgsqlCommand(
                    "CREATE EXTENSION IF NOT EXISTS vector", target);

                await extension.ExecuteNonQueryAsync();
            }

            await db.Database.MigrateAsync();

            var started = DateTimeOffset.UtcNow.AddSeconds(-5);

            var workflow = Workflow.Create(
                workflowId,
                "requester",
                tenant,
                "Publication crash-window acceptance",
                started.AddSeconds(-1));

            db.Workflows.Add(workflow);
            await db.SaveChangesAsync();

            workflow.Start(started);
            await db.SaveChangesAsync();

            workflow.Complete(started.AddSeconds(1));
            await db.SaveChangesAsync();

            var eventId = await db.Database.SqlQuery<Guid>($"""
                SELECT event_id AS "Value"
                FROM kafka_workflow_events
                WHERE workflow_id = {workflowId}
                  AND sequence = 1
                """).SingleAsync();

            var originalPayload =
                await db.Database.SqlQuery<string>($"""
                    SELECT payload::text AS "Value"
                    FROM kafka_workflow_events
                    WHERE event_id = {eventId}
                    """).SingleAsync();

            var expectedKey = KafkaEventTransport.PartitionKey(
                tenant, workflowId);

            using var consumer =
                new ConsumerBuilder<string, string>(
                    new ConsumerConfig
                    {
                        BootstrapServers = "localhost:19092",
                        GroupId = $"e7-crash-{Guid.NewGuid():N}",
                        AutoOffsetReset = AutoOffsetReset.Earliest,
                        EnableAutoCommit = false
                    }).Build();

            consumer.Subscribe(KafkaEventTransport.WorkflowsTopic);

            using var transport =
                new KafkaEventTransport("localhost:19092");

            // Reproduce the failure boundary: Kafka has acknowledged
            // publication, but PostgreSQL acknowledgement is rolled back.
            await using (var transaction =
                await db.Database.BeginTransactionAsync())
            {
                await transport.PublishAsync(
                    KafkaEventTransport.WorkflowsTopic,
                    tenant,
                    workflowId,
                    originalPayload);

                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE kafka_workflow_events
                    SET published_at = clock_timestamp()
                    WHERE event_id = {eventId}
                    """);

                await transaction.RollbackAsync();
            }

            await using var restarted =
                new NexusAgentDbContext(options);

            var pending = await restarted.Database.SqlQuery<int>($"""
                SELECT count(*)::integer AS "Value"
                FROM kafka_workflow_events
                WHERE workflow_id = {workflowId}
                  AND published_at IS NULL
                """).SingleAsync();

            Assert.Equal(2, pending);

            using var restartedTransport =
                new KafkaEventTransport("localhost:19092");

            var dispatcher = new PostgresWorkflowEventDispatcher(
                restarted, restartedTransport);

            Assert.True(await dispatcher.DispatchNextAsync());
            Assert.True(await dispatcher.DispatchNextAsync());
            Assert.False(await dispatcher.DispatchNextAsync());

            var published = await restarted.Database.SqlQuery<int>($"""
                SELECT count(*)::integer AS "Value"
                FROM kafka_workflow_events
                WHERE workflow_id = {workflowId}
                  AND published_at IS NOT NULL
                """).SingleAsync();

            Assert.Equal(2, published);

            var received =
                new List<ConsumeResult<string, string>>();

            using var timeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(45));

            while (received.Count < 3)
            {
                var message = consumer.Consume(timeout.Token);

                if (message.Message.Key == expectedKey)
                    received.Add(message);
            }

            Assert.Equal(
                received[0].Partition,
                received[1].Partition);

            Assert.Equal(
                received[1].Partition,
                received[2].Partition);

            Assert.True(received[0].Offset.Value <
                        received[1].Offset.Value);

            Assert.True(received[1].Offset.Value <
                        received[2].Offset.Value);

            Assert.Equal(originalPayload, received[0].Message.Value);
            Assert.Equal(originalPayload, received[1].Message.Value);

            var sequences = new List<long>();
            var identities = new List<Guid>();

            foreach (var message in received)
            {
                using var json = System.Text.Json.JsonDocument.Parse(
                    message.Message.Value);

                var payload = json.RootElement;

                sequences.Add(
                    payload.GetProperty("sequence").GetInt64());

                identities.Add(
                    payload.GetProperty("eventId").GetGuid());

                Assert.Equal(
                    workflowId,
                    payload.GetProperty("workflowId").GetGuid());

                Assert.Equal(
                    tenant,
                    payload.GetProperty("tenantId").GetString());
            }

            Assert.Equal(new long[] { 1, 1, 2 }, sequences);
            Assert.Equal(eventId, identities[0]);
            Assert.Equal(identities[0], identities[1]);
            Assert.NotEqual(identities[1], identities[2]);

            consumer.Close();
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
