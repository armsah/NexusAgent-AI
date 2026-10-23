using NexusAgent.Application.Tools;

namespace NexusAgent.Application.Approvals;

public sealed class ApprovalService(
    IApprovalStore store,
    IActionRiskPolicy riskPolicy)
{
    public async Task<ApprovalRequest> RequestAsync<TInput>(
        ToolRequest<TInput> request,
        ToolCaller caller,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        if (lifetime <= TimeSpan.Zero ||
            lifetime > TimeSpan.FromHours(24))
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        }

        var action = ApprovalActionFactory.Create(request, caller);
        var risk = riskPolicy.Classify(request.ToolName);

        if (!riskPolicy.RequiresApproval(risk))
        {
            throw new InvalidOperationException(
                "This tool does not require approval.");
        }

        var now = DateTimeOffset.UtcNow;

        var approval = new ApprovalRequest(
            Guid.NewGuid(),
            action,
            risk,
            ApprovalStatus.Pending,
            null,
            now,
            now.Add(lifetime));

        await store.CreateAsync(approval, cancellationToken);

        return approval;
    }

    public Task<bool> DecideAsync(
        ApprovalDecision decision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(decision);

        return store.DecideAsync(
            decision.ApprovalId,
            decision.ApproverId,
            decision.Approved,
            DateTimeOffset.UtcNow,
            cancellationToken);
    }
}