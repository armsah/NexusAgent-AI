namespace NexusAgent.Infrastructure.Persistence.Outbox;

public sealed record ClaimedOutboxMessage(
    Guid Id,
    Guid ClaimToken,
    string TenantId,
    string EventType,
    Guid DocumentId,
    int DocumentVersion,
    string Payload,
    DateTimeOffset LeaseExpiresAt);