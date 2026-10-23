namespace NexusAgent.Infrastructure.Persistence.Outbox;

public sealed class OutboxMessage
{
    private OutboxMessage()
    {
    }

    public Guid Id { get; private set; }

    public string TenantId { get; private set; } = string.Empty;

    public string EventType { get; private set; } = string.Empty;

    public Guid DocumentId { get; private set; }

    public int DocumentVersion { get; private set; }

    public string Payload { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public Guid? ClaimToken { get; private set; }

    public DateTimeOffset? LeaseExpiresAt { get; private set; }

    public DateTimeOffset? NextAttemptAt { get; private set; }
    public DateTimeOffset? FailedAt { get; private set; }
    public int RetryCount { get; private set; }

    public string? LastError { get; private set; }

    public static OutboxMessage Create(
        Guid id,
        string tenantId,
        string eventType,
        Guid documentId,
        int documentVersion,
        string payload,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Event ID is required.", nameof(id));

        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("Tenant ID is required.", nameof(tenantId));

        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("Event type is required.", nameof(eventType));

        if (documentId == Guid.Empty)
            throw new ArgumentException("Document ID is required.", nameof(documentId));

        if (documentVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(documentVersion));

        if (string.IsNullOrWhiteSpace(payload))
            throw new ArgumentException("Event payload is required.", nameof(payload));

        return new OutboxMessage
        {
            Id = id,
            TenantId = tenantId,
            EventType = eventType,
            DocumentId = documentId,
            DocumentVersion = documentVersion,
            Payload = payload,
            CreatedAt = createdAt
        };
    }

    public void Claim(
        Guid claimToken,
        DateTimeOffset now,
        TimeSpan leaseDuration)
    {
        if (claimToken == Guid.Empty)
            throw new ArgumentException(
                "Claim token is required.", nameof(claimToken));

        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));

        if (ProcessedAt is not null)
            throw new InvalidOperationException(
                "A processed event cannot be claimed.");

        if (ClaimToken is not null &&
            LeaseExpiresAt is not null &&
            LeaseExpiresAt > now)
        {
            throw new InvalidOperationException(
                "The event already has an active claim.");
        }

        ClaimToken = claimToken;
        LeaseExpiresAt = now.Add(leaseDuration);
    }

    public void RenewClaim(
        Guid claimToken,
        DateTimeOffset now,
        TimeSpan leaseDuration)
    {
        EnsureActiveClaim(claimToken, now);

        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));

        LeaseExpiresAt = now.Add(leaseDuration);
    }

    public void ReleaseClaim(
        Guid claimToken,
        DateTimeOffset now)
    {
        EnsureActiveClaim(claimToken, now);

        ClaimToken = null;
        LeaseExpiresAt = null;
    }

    public void MarkProcessedWithClaim(
        Guid claimToken,
        DateTimeOffset processedAt)
    {
        EnsureActiveClaim(claimToken, processedAt);

        ProcessedAt = processedAt;
        ClaimToken = null;
        LeaseExpiresAt = null;
        LastError = null;
    }

    private void EnsureActiveClaim(
        Guid claimToken,
        DateTimeOffset now)
    {
        if (claimToken == Guid.Empty ||
            ProcessedAt is not null ||
            ClaimToken != claimToken ||
            LeaseExpiresAt is null ||
            LeaseExpiresAt <= now)
        {
            throw new InvalidOperationException(
                "The event does not have a valid active claim.");
        }
    }
    public void MarkProcessed(DateTimeOffset processedAt)
    {
        if (ProcessedAt is not null)
            return;

        ProcessedAt = processedAt;
        LastError = null;
    }

    public void RecordFailure(string error)
    {
        if (ProcessedAt is not null)
            throw new InvalidOperationException(
                "A processed event cannot be marked as failed.");

        if (string.IsNullOrWhiteSpace(error))
            throw new ArgumentException("Error is required.", nameof(error));

        RetryCount++;
        LastError = error;
    }
}