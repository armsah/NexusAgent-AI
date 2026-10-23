using Microsoft.EntityFrameworkCore;
using NexusAgent.Infrastructure.Persistence;
using NexusAgent.Infrastructure.Persistence.Outbox;
using Xunit;

namespace NexusAgent.IntegrationTests;

public sealed class PostgresOutboxClaimerTests
{
    private const string ConnectionString =
        "Host=127.0.0.1;Port=15432;Database=nexusagent;" +
        "Username=nexusagent;Password=nexusagent_dev_only";

    private static NexusAgentDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<NexusAgentDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new NexusAgentDbContext(options);
    }

    [Fact]
    public async Task RecordFailureAsync_StaleTokenCannotModifyReclaimedMessage()
    {
        var messageId = Guid.NewGuid();

        var message = OutboxMessage.Create(
            messageId,
            "tenant-stale-failure-" + Guid.NewGuid().ToString("N"),
            "document.created",
            Guid.NewGuid(),
            1,
            "{}",
            DateTimeOffset.UtcNow.AddDays(-7));

        await using (var setup = CreateContext())
        {
            await setup.Database.MigrateAsync();

            setup.OutboxMessages.Add(message);
            await setup.SaveChangesAsync();
        }

        ClaimedOutboxMessage firstClaim;

        await using (var db = CreateContext())
        {
            var claimer = new PostgresOutboxClaimer(db);

            var results = await claimer.ClaimAsync(
                1000,
                TimeSpan.FromSeconds(5));

            firstClaim = Assert.Single(
                results,
                result => result.Id == messageId);
        }

        var wait = firstClaim.LeaseExpiresAt
            - DateTimeOffset.UtcNow
            + TimeSpan.FromSeconds(1);

        if (wait > TimeSpan.Zero)
            await Task.Delay(wait);

        ClaimedOutboxMessage secondClaim;

        await using (var db = CreateContext())
        {
            var claimer = new PostgresOutboxClaimer(db);

            var results = await claimer.ClaimAsync(
                1000,
                TimeSpan.FromMinutes(5));

            secondClaim = Assert.Single(
                results,
                result => result.Id == messageId);
        }

        Assert.NotEqual(
            firstClaim.ClaimToken,
            secondClaim.ClaimToken);

        await using (var db = CreateContext())
        {
            var recorder = new PostgresOutboxFailureRecorder(db);

            var staleResult = await recorder.RecordFailureAsync(
                messageId,
                firstClaim.ClaimToken,
                "Stale worker failure");

            Assert.False(staleResult);
        }

        await using (var db = CreateContext())
        {
            var persisted = await db.OutboxMessages
                .AsNoTracking()
                .SingleAsync(item => item.Id == messageId);

            Assert.Equal(0, persisted.RetryCount);
            Assert.Null(persisted.LastError);
            Assert.Null(persisted.ProcessedAt);

            Assert.Equal(
                secondClaim.ClaimToken,
                persisted.ClaimToken);

            Assert.Equal(
                secondClaim.LeaseExpiresAt,
                persisted.LeaseExpiresAt);
        }

        await using (var db = CreateContext())
        {
            var recorder = new PostgresOutboxFailureRecorder(db);

            var currentResult = await recorder.RecordFailureAsync(
                messageId,
                secondClaim.ClaimToken,
                "Current worker failure");

            Assert.True(currentResult);
        }

        await using (var db = CreateContext())
        {
            var persisted = await db.OutboxMessages
                .AsNoTracking()
                .SingleAsync(item => item.Id == messageId);

            Assert.Equal(1, persisted.RetryCount);
            Assert.Equal(
                "Current worker failure",
                persisted.LastError);

            Assert.Null(persisted.ClaimToken);
            Assert.Null(persisted.LeaseExpiresAt);
        }
    }
    [Fact]
    public async Task RecordFailureAsync_SchedulesRetryAndBlocksEarlyClaim()
    {
        var messageId = Guid.NewGuid();

        var message = OutboxMessage.Create(
            messageId,
            "tenant-backoff-" + Guid.NewGuid().ToString("N"),
            "document.created",
            Guid.NewGuid(),
            1,
            "{}",
            DateTimeOffset.UtcNow.AddDays(-8));

        await using (var setup = CreateContext())
        {
            await setup.Database.MigrateAsync();

            setup.OutboxMessages.Add(message);
            await setup.SaveChangesAsync();
        }

        ClaimedOutboxMessage firstClaim;

        await using (var db = CreateContext())
        {
            var results = await new PostgresOutboxClaimer(db)
                .ClaimAsync(1000, TimeSpan.FromMinutes(5));

            firstClaim = Assert.Single(
                results,
                result => result.Id == messageId);
        }

        var beforeFailure = DateTimeOffset.UtcNow;

        await using (var db = CreateContext())
        {
            var recorded = await new PostgresOutboxFailureRecorder(db)
                .RecordFailureAsync(
                    messageId,
                    firstClaim.ClaimToken,
                    "Simulated retryable failure");

            Assert.True(recorded);
        }

        DateTimeOffset nextAttemptAt;

        await using (var db = CreateContext())
        {
            var persisted = await db.OutboxMessages
                .AsNoTracking()
                .SingleAsync(item => item.Id == messageId);

            Assert.Equal(1, persisted.RetryCount);
            Assert.NotNull(persisted.NextAttemptAt);
            Assert.Null(persisted.ClaimToken);
            Assert.Null(persisted.LeaseExpiresAt);

            nextAttemptAt = persisted.NextAttemptAt.Value;

            var delay = nextAttemptAt - beforeFailure;

            Assert.InRange(
                delay.TotalSeconds,
                4.5,
                7.0);
        }

        await using (var db = CreateContext())
        {
            var results = await new PostgresOutboxClaimer(db)
                .ClaimAsync(1000, TimeSpan.FromMinutes(5));

            Assert.DoesNotContain(
                results,
                result => result.Id == messageId);
        }

        var wait = nextAttemptAt
            - DateTimeOffset.UtcNow
            + TimeSpan.FromSeconds(1);

        if (wait > TimeSpan.Zero)
            await Task.Delay(wait);

        await using (var db = CreateContext())
        {
            var results = await new PostgresOutboxClaimer(db)
                .ClaimAsync(1000, TimeSpan.FromMinutes(5));

            var secondClaim = Assert.Single(
                results,
                result => result.Id == messageId);

            Assert.NotEqual(
                firstClaim.ClaimToken,
                secondClaim.ClaimToken);
        }
    }
    [Fact]
    public async Task RecordFailureAsync_ReleasesClaimAndIncrementsRetryCount()
    {
        var messageId = Guid.NewGuid();

        var message = OutboxMessage.Create(
            messageId,
            "tenant-failure-" + Guid.NewGuid().ToString("N"),
            "document.created",
            Guid.NewGuid(),
            1,
            "{}",
            DateTimeOffset.UtcNow.AddDays(-6));

        await using (var setup = CreateContext())
        {
            await setup.Database.MigrateAsync();

            setup.OutboxMessages.Add(message);
            await setup.SaveChangesAsync();
        }

        ClaimedOutboxMessage firstClaim;

        await using (var db = CreateContext())
        {
            var claimer = new PostgresOutboxClaimer(db);

            var results = await claimer.ClaimAsync(
                1000,
                TimeSpan.FromMinutes(5));

            firstClaim = Assert.Single(
                results,
                result => result.Id == messageId);
        }

        await using (var db = CreateContext())
        {
            var recorder = new PostgresOutboxFailureRecorder(db);

            var recorded = await recorder.RecordFailureAsync(
                messageId,
                firstClaim.ClaimToken,
                "Simulated processing failure");

            Assert.True(recorded);

            var duplicate = await recorder.RecordFailureAsync(
                messageId,
                firstClaim.ClaimToken,
                "Duplicate failure report");

            Assert.False(duplicate);
        }

        await using (var db = CreateContext())
        {
            var persisted = await db.OutboxMessages
                .AsNoTracking()
                .SingleAsync(item => item.Id == messageId);

            Assert.Equal(1, persisted.RetryCount);
            Assert.Equal(
                "Simulated processing failure",
                persisted.LastError);

            Assert.Null(persisted.ClaimToken);
            Assert.Null(persisted.LeaseExpiresAt);
            Assert.Null(persisted.ProcessedAt);
        }

        DateTimeOffset nextAttemptAt;

        await using (var db = CreateContext())
        {
            var persisted = await db.OutboxMessages
                .AsNoTracking()
                .SingleAsync(item => item.Id == messageId);

            Assert.NotNull(persisted.NextAttemptAt);
            nextAttemptAt = persisted.NextAttemptAt.Value;
        }

        var wait = nextAttemptAt
            - DateTimeOffset.UtcNow
            + TimeSpan.FromSeconds(1);

        if (wait > TimeSpan.Zero)
            await Task.Delay(wait);

        ClaimedOutboxMessage secondClaim;

        await using (var db = CreateContext())
        {
            var claimer = new PostgresOutboxClaimer(db);

            var results = await claimer.ClaimAsync(
                1000,
                TimeSpan.FromMinutes(5));

            secondClaim = Assert.Single(
                results,
                result => result.Id == messageId);
        }

        Assert.NotEqual(
            firstClaim.ClaimToken,
            secondClaim.ClaimToken);

        await using (var db = CreateContext())
        {
            var persisted = await db.OutboxMessages
                .AsNoTracking()
                .SingleAsync(item => item.Id == messageId);

            Assert.Equal(
                secondClaim.ClaimToken,
                persisted.ClaimToken);

            Assert.Equal(1, persisted.RetryCount);
            Assert.Equal(
                "Simulated processing failure",
                persisted.LastError);
        }
    }
    [Fact]
    public async Task AcknowledgeAsync_StaleTokenCannotAcknowledgeReclaimedMessage()
    {
        var messageId = Guid.NewGuid();

        var message = OutboxMessage.Create(
            messageId,
            "tenant-stale-ack-" + Guid.NewGuid().ToString("N"),
            "document.created",
            Guid.NewGuid(),
            1,
            "{}",
            DateTimeOffset.UtcNow.AddDays(-5));

        await using (var setup = CreateContext())
        {
            await setup.Database.MigrateAsync();

            setup.OutboxMessages.Add(message);
            await setup.SaveChangesAsync();
        }

        ClaimedOutboxMessage firstClaim;

        await using (var db = CreateContext())
        {
            var claimer = new PostgresOutboxClaimer(db);

            var results = await claimer.ClaimAsync(
                1000,
                TimeSpan.FromSeconds(5));

            firstClaim = Assert.Single(
                results,
                result => result.Id == messageId);
        }

        var wait = firstClaim.LeaseExpiresAt -
            DateTimeOffset.UtcNow +
            TimeSpan.FromSeconds(1);

        if (wait > TimeSpan.Zero)
            await Task.Delay(wait);

        ClaimedOutboxMessage secondClaim;

        await using (var db = CreateContext())
        {
            var claimer = new PostgresOutboxClaimer(db);

            var results = await claimer.ClaimAsync(
                1000,
                TimeSpan.FromMinutes(5));

            secondClaim = Assert.Single(
                results,
                result => result.Id == messageId);
        }

        Assert.NotEqual(
            firstClaim.ClaimToken,
            secondClaim.ClaimToken);

        await using (var db = CreateContext())
        {
            var acknowledger = new PostgresOutboxAcknowledger(db);

            var staleAcknowledgment =
                await acknowledger.AcknowledgeAsync(
                    messageId,
                    firstClaim.ClaimToken);

            Assert.False(staleAcknowledgment);
        }

        await using (var db = CreateContext())
        {
            var persisted = await db.OutboxMessages
                .AsNoTracking()
                .SingleAsync(item => item.Id == messageId);

            Assert.Null(persisted.ProcessedAt);

            Assert.Equal(
                secondClaim.ClaimToken,
                persisted.ClaimToken);

            Assert.Equal(
                secondClaim.LeaseExpiresAt,
                persisted.LeaseExpiresAt);
        }

        await using (var db = CreateContext())
        {
            var acknowledger = new PostgresOutboxAcknowledger(db);

            var currentAcknowledgment =
                await acknowledger.AcknowledgeAsync(
                    messageId,
                    secondClaim.ClaimToken);

            Assert.True(currentAcknowledgment);
        }

        await using (var db = CreateContext())
        {
            var persisted = await db.OutboxMessages
                .AsNoTracking()
                .SingleAsync(item => item.Id == messageId);

            Assert.NotNull(persisted.ProcessedAt);
            Assert.Null(persisted.ClaimToken);
            Assert.Null(persisted.LeaseExpiresAt);
        }
    }
    [Fact]
    public async Task AcknowledgeAsync_ProcessesOnceAndClearsClaim()
    {
        var messageId = Guid.NewGuid();

        var message = OutboxMessage.Create(
            messageId,
            "tenant-ack-" + Guid.NewGuid().ToString("N"),
            "document.created",
            Guid.NewGuid(),
            1,
            "{}",
            DateTimeOffset.UtcNow.AddDays(-4));

        await using (var setup = CreateContext())
        {
            await setup.Database.MigrateAsync();

            setup.OutboxMessages.Add(message);
            await setup.SaveChangesAsync();
        }

        ClaimedOutboxMessage claim;

        await using (var db = CreateContext())
        {
            var claimer = new PostgresOutboxClaimer(db);

            var results = await claimer.ClaimAsync(
                1000,
                TimeSpan.FromMinutes(5));

            claim = Assert.Single(
                results,
                result => result.Id == messageId);
        }

        await using (var db = CreateContext())
        {
            var acknowledger = new PostgresOutboxAcknowledger(db);

            var acknowledged = await acknowledger.AcknowledgeAsync(
                messageId,
                claim.ClaimToken);

            Assert.True(acknowledged);

            var duplicate = await acknowledger.AcknowledgeAsync(
                messageId,
                claim.ClaimToken);

            Assert.False(duplicate);
        }

        await using (var db = CreateContext())
        {
            var persisted = await db.OutboxMessages
                .AsNoTracking()
                .SingleAsync(item => item.Id == messageId);

            Assert.NotNull(persisted.ProcessedAt);
            Assert.Null(persisted.ClaimToken);
            Assert.Null(persisted.LeaseExpiresAt);
            Assert.Null(persisted.LastError);
        }

        await using (var db = CreateContext())
        {
            var claimer = new PostgresOutboxClaimer(db);

            var results = await claimer.ClaimAsync(
                1000,
                TimeSpan.FromMinutes(5));

            Assert.DoesNotContain(
                results,
                result => result.Id == messageId);
        }
    }
    [Fact]
    public async Task ClaimAsync_ExpiredLease_CanBeReclaimed()
    {
        var messageId = Guid.NewGuid();

        var message = OutboxMessage.Create(
            messageId,
            "tenant-expired-lease-" + Guid.NewGuid().ToString("N"),
            "document.created",
            Guid.NewGuid(),
            1,
            "{}",
            DateTimeOffset.UtcNow.AddDays(-3));

        await using (var setup = CreateContext())
        {
            await setup.Database.MigrateAsync();

            setup.OutboxMessages.Add(message);
            await setup.SaveChangesAsync();
        }

        ClaimedOutboxMessage firstClaim;

        await using (var db = CreateContext())
        {
            var claimer = new PostgresOutboxClaimer(db);

            var results = await claimer.ClaimAsync(
                1000,
                TimeSpan.FromSeconds(5));

            firstClaim = Assert.Single(
                results,
                result => result.Id == messageId);
        }

        await using (var db = CreateContext())
        {
            var persisted = await db.OutboxMessages
                .AsNoTracking()
                .SingleAsync(item => item.Id == messageId);

            Assert.Equal(firstClaim.ClaimToken, persisted.ClaimToken);
            Assert.NotNull(persisted.LeaseExpiresAt);
        }

        // Confirm the original claim is still active.
        await using (var db = CreateContext())
        {
            var claimer = new PostgresOutboxClaimer(db);

            var results = await claimer.ClaimAsync(
                1000,
                TimeSpan.FromMinutes(5));

            Assert.DoesNotContain(
                results,
                result => result.Id == messageId);
        }

        var wait = firstClaim.LeaseExpiresAt -
            DateTimeOffset.UtcNow +
            TimeSpan.FromSeconds(1);

        if (wait > TimeSpan.Zero)
            await Task.Delay(wait);

        ClaimedOutboxMessage secondClaim;

        await using (var db = CreateContext())
        {
            var claimer = new PostgresOutboxClaimer(db);

            var results = await claimer.ClaimAsync(
                1000,
                TimeSpan.FromMinutes(5));

            secondClaim = Assert.Single(
                results,
                result => result.Id == messageId);
        }

        Assert.NotEqual(
            firstClaim.ClaimToken,
            secondClaim.ClaimToken);

        Assert.True(
            secondClaim.LeaseExpiresAt >
            firstClaim.LeaseExpiresAt);

        await using (var db = CreateContext())
        {
            var persisted = await db.OutboxMessages
                .AsNoTracking()
                .SingleAsync(item => item.Id == messageId);

            Assert.Equal(
                secondClaim.ClaimToken,
                persisted.ClaimToken);

            Assert.Equal(
                secondClaim.LeaseExpiresAt,
                persisted.LeaseExpiresAt);
        }
    }
    [Fact]
    public async Task ClaimAsync_ConcurrentWorkers_DoNotClaimSameMessage()
    {
        var messageId = Guid.NewGuid();

        var message = OutboxMessage.Create(
            messageId,
            "tenant-concurrent-claim-" + Guid.NewGuid().ToString("N"),
            "document.created",
            Guid.NewGuid(),
            1,
            "{}",
            DateTimeOffset.UtcNow.AddDays(-2));

        await using (var setup = CreateContext())
        {
            await setup.Database.MigrateAsync();

            setup.OutboxMessages.Add(message);
            await setup.SaveChangesAsync();
        }

        await using var firstDb = CreateContext();
        await using var secondDb = CreateContext();

        var firstClaimer = new PostgresOutboxClaimer(firstDb);
        var secondClaimer = new PostgresOutboxClaimer(secondDb);

        var firstTask = firstClaimer.ClaimAsync(
            1000,
            TimeSpan.FromMinutes(5));

        var secondTask = secondClaimer.ClaimAsync(
            1000,
            TimeSpan.FromMinutes(5));

        await Task.WhenAll(firstTask, secondTask);

        var firstResults = await firstTask;
        var secondResults = await secondTask;

        var firstClaim = firstResults
            .Where(result => result.Id == messageId)
            .ToList();

        var secondClaim = secondResults
            .Where(result => result.Id == messageId)
            .ToList();

        var totalClaims = firstClaim.Count + secondClaim.Count;

        Assert.Equal(1, totalClaims);

        await using var verificationDb = CreateContext();

        var persisted = await verificationDb.OutboxMessages
            .AsNoTracking()
            .SingleAsync(item => item.Id == messageId);

        Assert.NotNull(persisted.ClaimToken);
        Assert.NotNull(persisted.LeaseExpiresAt);

        var winningClaim = firstClaim
            .Concat(secondClaim)
            .Single();

        Assert.Equal(
            winningClaim.ClaimToken,
            persisted.ClaimToken);
    }
    [Fact]
    public async Task ClaimAsync_PersistsClaimAndPreventsImmediateReclaim()
    {
        var messageId = Guid.NewGuid();

        var message = OutboxMessage.Create(
            messageId,
            "tenant-claim-" + Guid.NewGuid().ToString("N"),
            "document.created",
            Guid.NewGuid(),
            1,
            "{}",
            DateTimeOffset.UtcNow.AddDays(-1));

        await using (var db = CreateContext())
        {
            await db.Database.MigrateAsync();

            db.OutboxMessages.Add(message);
            await db.SaveChangesAsync();
        }

        ClaimedOutboxMessage claimed;

        await using (var db = CreateContext())
        {
            var claimer = new PostgresOutboxClaimer(db);

            var results = await claimer.ClaimAsync(
                1000,
                TimeSpan.FromMinutes(5));

            claimed = Assert.Single(
                results,
                result => result.Id == messageId);

            Assert.NotEqual(Guid.Empty, claimed.ClaimToken);
            Assert.True(claimed.LeaseExpiresAt > DateTimeOffset.UtcNow);
        }

        await using (var db = CreateContext())
        {
            var persisted = await db.OutboxMessages
                .AsNoTracking()
                .SingleAsync(item => item.Id == messageId);

            Assert.Equal(claimed.ClaimToken, persisted.ClaimToken);
            Assert.Equal(
                claimed.LeaseExpiresAt,
                persisted.LeaseExpiresAt);
            Assert.Null(persisted.ProcessedAt);
        }

        await using (var db = CreateContext())
        {
            var claimer = new PostgresOutboxClaimer(db);

            var results = await claimer.ClaimAsync(
                1000,
                TimeSpan.FromMinutes(5));

            Assert.DoesNotContain(
                results,
                result => result.Id == messageId);
        }
    }

    [Fact]
    public async Task RecordFailureAsync_FifthFailureMarksMessageTerminal()
    {
        var messageId = Guid.NewGuid();

        var message = OutboxMessage.Create(
            messageId,
            "tenant-terminal-" + Guid.NewGuid().ToString("N"),
            "document.created",
            Guid.NewGuid(),
            1,
            "{}",
            DateTimeOffset.UtcNow.AddDays(-1));

        await using (var setup = CreateContext())
        {
            await setup.Database.MigrateAsync();
            setup.OutboxMessages.Add(message);
            await setup.SaveChangesAsync();
        }

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            ClaimedOutboxMessage claim;

            await using (var db = CreateContext())
            {
                var results = await new PostgresOutboxClaimer(db)
                    .ClaimAsync(1000, TimeSpan.FromMinutes(5));

                claim = Assert.Single(
                    results,
                    item => item.Id == messageId);
            }

            await using (var db = CreateContext())
            {
                var recorder = new PostgresOutboxFailureRecorder(db);

                Assert.True(await recorder.RecordFailureAsync(
                    messageId,
                    claim.ClaimToken,
                    $"Test failure {attempt}"));
            }

            await using (var db = CreateContext())
            {
                if (attempt < 5)
                {
                    await db.Database.ExecuteSqlInterpolatedAsync(
                        $"""
                        UPDATE outbox_messages
                        SET next_attempt_at =
                            clock_timestamp() - interval '1 second'
                        WHERE id = {messageId}
                        """);
                }
            }
        }

        await using (var db = CreateContext())
        {
            var persisted = await db.OutboxMessages
                .AsNoTracking()
                .SingleAsync(item => item.Id == messageId);

            Assert.Equal(5, persisted.RetryCount);
            Assert.NotNull(persisted.FailedAt);
            Assert.Null(persisted.NextAttemptAt);
            Assert.Null(persisted.ClaimToken);
            Assert.Null(persisted.LeaseExpiresAt);
            Assert.Equal("Test failure 5", persisted.LastError);

            var results = await new PostgresOutboxClaimer(db)
                .ClaimAsync(1000, TimeSpan.FromMinutes(5));

            Assert.DoesNotContain(results, item => item.Id == messageId);
        }
    }

    [Fact]
    public async Task PublishAsync_PersistsEventAndIgnoresDuplicateEventId()
    {
        var eventId = Guid.NewGuid();
        var tenantId = "tenant-publisher-" + Guid.NewGuid().ToString("N");
        var documentId = Guid.NewGuid();

        await using (var db = CreateContext())
        {
            await db.Database.MigrateAsync();

            var publisher = new PostgresDocumentIngestionEventPublisher(db);

            await publisher.PublishAsync(
                eventId,
                "document.ingestion.requested",
                tenantId,
                documentId,
                1,
                """{"source":"integration-test"}""");

            await publisher.PublishAsync(
                eventId,
                "document.ingestion.requested",
                tenantId,
                documentId,
                1,
                """{"source":"duplicate-test"}""");
        }

        await using (var verification = CreateContext())
        {
            var count = await verification.Database
                .SqlQuery<int>($"""
                    SELECT COUNT(*)::integer AS "Value"
                    FROM published_document_events
                    WHERE event_id = {eventId}
                    """)
                .SingleAsync();

            Assert.Equal(1, count);

            var persistedPayload = await verification.Database
                .SqlQuery<string>($"""
                    SELECT payload::text AS "Value"
                    FROM published_document_events
                    WHERE event_id = {eventId}
                    """)
                .SingleAsync();

            Assert.Contains("integration-test", persistedPayload);
            Assert.DoesNotContain("duplicate-test", persistedPayload);
        }
    }}