using Microsoft.EntityFrameworkCore;
using NexusAgent.Infrastructure.Persistence;
using NexusAgent.Infrastructure.Persistence.Outbox;
using Pgvector.EntityFrameworkCore;
using Xunit;

namespace NexusAgent.UnitTests;

public sealed class E7OutboxOrderingTests
{
    private const string Connection =
        "Host=127.0.0.1;Port=15432;Database=nexusagent;" +
        "Username=nexusagent;Password=nexusagent_dev_only";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PredecessorControlsSuccessorEligibility(bool quarantine)
    {
        var database = $"nexus_e7_order_{Guid.NewGuid():N}";
        var isolatedConnection = Connection.Replace(
            "Database=nexusagent", $"Database={database}");

        var options = new DbContextOptionsBuilder<NexusAgentDbContext>()
            .UseNpgsql(isolatedConnection, builder => builder.UseVector())
            .Options;

        await using var db = new NexusAgentDbContext(options);

        await using (var admin = new Npgsql.NpgsqlConnection(Connection))
        {
            await admin.OpenAsync();
            await using var create = admin.CreateCommand();
            create.CommandText = $"CREATE DATABASE \"{database}\"";
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            await using (var target =
                new Npgsql.NpgsqlConnection(isolatedConnection))
            {
                await target.OpenAsync();
                await using var extension = target.CreateCommand();
                extension.CommandText = "CREATE EXTENSION IF NOT EXISTS vector";
                await extension.ExecuteNonQueryAsync();
            }

            await db.Database.MigrateAsync();

        var tenant = $"e7-order-{Guid.NewGuid():N}";
        var document = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow.AddMinutes(-5);

        try
        {
            db.OutboxMessages.AddRange(
                OutboxMessage.Create(
                    first, tenant, "document.created",
                    document, 1, "{}", now),
                OutboxMessage.Create(
                    second, tenant, "document.updated",
                    document, 2, "{}", now.AddSeconds(1)));

            await db.SaveChangesAsync();

            var claimer = new PostgresOutboxClaimer(db);
            var batch = await claimer.ClaimAsync(
                1000, TimeSpan.FromMinutes(5));

            var predecessor = Assert.Single(batch, item => item.Id == first);

            Assert.DoesNotContain(batch, item => item.Id == second);

            await using var competing = new NexusAgentDbContext(options);
            var competingClaimer = new PostgresOutboxClaimer(competing);

            var competingBatch = await competingClaimer.ClaimAsync(
                1000, TimeSpan.FromMinutes(5));

            Assert.DoesNotContain(
                competingBatch,
                item => item.Id == first || item.Id == second);

            if (quarantine)
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE outbox_messages
                    SET failed_at = clock_timestamp(),
                        claim_token = NULL,
                        lease_expires_at = NULL
                    WHERE id = {first}
                    """);

                var blocked = await claimer.ClaimAsync(
                    1000, TimeSpan.FromMinutes(5));

                Assert.DoesNotContain(
                    blocked,
                    item => item.Id == first || item.Id == second);
            }
            else
            {
                var acknowledger = new PostgresOutboxAcknowledger(db);

                Assert.False(await acknowledger.AcknowledgeAsync(
                    first, Guid.NewGuid()));

                Assert.True(await acknowledger.AcknowledgeAsync(
                    first, predecessor.ClaimToken));

                var next = await claimer.ClaimAsync(
                    1000, TimeSpan.FromMinutes(5));

                Assert.Contains(next, item => item.Id == second);
            }
        }
        finally
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM outbox_messages WHERE tenant_id = {tenant}");
        }
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
