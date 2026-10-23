using NexusAgent.Infrastructure.Persistence.Outbox;
using Xunit;

namespace NexusAgent.UnitTests;

public sealed class OutboxMessageClaimTests
{
    private static OutboxMessage CreateMessage()
    {
        return OutboxMessage.Create(
            Guid.NewGuid(),
            "tenant-test",
            "document.created",
            Guid.NewGuid(),
            1,
            "{}",
            DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Claim_AssignsTokenAndLeaseExpiration()
    {
        var message = CreateMessage();
        var token = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        message.Claim(token, now, TimeSpan.FromMinutes(5));

        Assert.Equal(token, message.ClaimToken);
        Assert.Equal(now.AddMinutes(5), message.LeaseExpiresAt);
    }

    [Fact]
    public void Claim_RejectsCompetingWorkerWhileLeaseActive()
    {
        var message = CreateMessage();
        var firstToken = Guid.NewGuid();
        var secondToken = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        message.Claim(firstToken, now, TimeSpan.FromMinutes(5));

        Assert.Throws<InvalidOperationException>(() =>
            message.Claim(
                secondToken,
                now.AddMinutes(1),
                TimeSpan.FromMinutes(5)));

        Assert.Equal(firstToken, message.ClaimToken);
    }

    [Fact]
    public void Claim_AllowsRecoveryAfterLeaseExpiration()
    {
        var message = CreateMessage();
        var firstToken = Guid.NewGuid();
        var secondToken = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        message.Claim(firstToken, now, TimeSpan.FromMinutes(5));

        message.Claim(
            secondToken,
            now.AddMinutes(6),
            TimeSpan.FromMinutes(5));

        Assert.Equal(secondToken, message.ClaimToken);
        Assert.Equal(now.AddMinutes(11), message.LeaseExpiresAt);
    }

    [Fact]
    public void MarkProcessedWithClaim_RejectsStaleWorker()
    {
        var message = CreateMessage();
        var firstToken = Guid.NewGuid();
        var secondToken = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        message.Claim(firstToken, now, TimeSpan.FromMinutes(5));

        message.Claim(
            secondToken,
            now.AddMinutes(6),
            TimeSpan.FromMinutes(5));

        Assert.Throws<InvalidOperationException>(() =>
            message.MarkProcessedWithClaim(
                firstToken,
                now.AddMinutes(7)));

        Assert.Null(message.ProcessedAt);
        Assert.Equal(secondToken, message.ClaimToken);
    }

    [Fact]
    public void RenewReleaseAndAcknowledge_RequireValidClaim()
    {
        var message = CreateMessage();
        var firstToken = Guid.NewGuid();
        var secondToken = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        message.Claim(firstToken, now, TimeSpan.FromMinutes(5));

        message.RenewClaim(
            firstToken,
            now.AddMinutes(2),
            TimeSpan.FromMinutes(5));

        Assert.Equal(now.AddMinutes(7), message.LeaseExpiresAt);

        Assert.Throws<InvalidOperationException>(() =>
            message.ReleaseClaim(
                secondToken,
                now.AddMinutes(3)));

        message.ReleaseClaim(
            firstToken,
            now.AddMinutes(3));

        Assert.Null(message.ClaimToken);
        Assert.Null(message.LeaseExpiresAt);

        message.Claim(
            secondToken,
            now.AddMinutes(4),
            TimeSpan.FromMinutes(5));

        message.MarkProcessedWithClaim(
            secondToken,
            now.AddMinutes(5));

        Assert.Equal(now.AddMinutes(5), message.ProcessedAt);
        Assert.Null(message.ClaimToken);
        Assert.Null(message.LeaseExpiresAt);

        Assert.Throws<InvalidOperationException>(() =>
            message.Claim(
                Guid.NewGuid(),
                now.AddMinutes(6),
                TimeSpan.FromMinutes(5)));
    }
}