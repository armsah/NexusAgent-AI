namespace NexusAgent.Application.Approvals;

public enum ActionRisk
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}

public enum ApprovalStatus
{
    Pending = 0,
    Approved = 1,
    Denied = 2,
    Expired = 3,
    Consumed = 4
}

public sealed record ApprovalAction(
    string TenantId,
    string RequesterId,
    string ToolName,
    string PayloadDigest);

public sealed record ApprovalRequest(
    Guid ApprovalId,
    ApprovalAction Action,
    ActionRisk Risk,
    ApprovalStatus Status,
    string? ApproverId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public sealed record ApprovalDecision(
    Guid ApprovalId,
    string ApproverId,
    bool Approved);

public interface IApprovalStore
{
    Task<ApprovalRequest?> GetAsync(
        Guid approvalId,
        CancellationToken cancellationToken);

    Task CreateAsync(
        ApprovalRequest request,
        CancellationToken cancellationToken);

    Task<bool> DecideAsync(
        Guid approvalId,
        string approverId,
        bool approved,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<bool> ConsumeAsync(
        Guid approvalId,
        ApprovalAction expectedAction,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}