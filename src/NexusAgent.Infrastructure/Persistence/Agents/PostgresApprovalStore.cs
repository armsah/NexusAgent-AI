using Microsoft.EntityFrameworkCore;
using NexusAgent.Application.Approvals;

namespace NexusAgent.Infrastructure.Persistence.Agents;

public sealed class PostgresApprovalStore(
    NexusAgentDbContext db) : IApprovalStore
{
    public async Task<ApprovalRequest?> GetAsync(
        Guid approvalId,
        CancellationToken cancellationToken)
    {
        var state = await db.AgentApprovals.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == approvalId, cancellationToken);

        return state is null ? null : new ApprovalRequest(
            state.Id,
            new ApprovalAction(
                state.TenantId,
                state.RequesterId,
                state.ToolName,
                state.PayloadDigest),
            (ActionRisk)state.Risk,
            (ApprovalStatus)state.Status,
            state.ApproverId,
            state.CreatedAt,
            state.ExpiresAt);
    }

    public async Task CreateAsync(
        ApprovalRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Action);

        if (request.ApprovalId == Guid.Empty ||
            request.Status != ApprovalStatus.Pending ||
            request.Risk is not (ActionRisk.High or ActionRisk.Critical) ||
            request.ApproverId is not null ||
            request.CreatedAt >= request.ExpiresAt ||
            request.ExpiresAt - request.CreatedAt > TimeSpan.FromHours(24) ||
            string.IsNullOrWhiteSpace(request.Action.TenantId) ||
            string.IsNullOrWhiteSpace(request.Action.RequesterId) ||
            string.IsNullOrWhiteSpace(request.Action.ToolName) ||
            string.IsNullOrWhiteSpace(request.Action.PayloadDigest) ||
            request.Action.TenantId.Length > 256 ||
            request.Action.RequesterId.Length > 256 ||
            request.Action.ToolName.Length > 256 ||
            request.Action.PayloadDigest.Length > 64)
        {
            throw new ArgumentException("Invalid approval request.");
        }

        var state = new AgentApprovalState
        {
            Id = request.ApprovalId,
            TenantId = request.Action.TenantId,
            RequesterId = request.Action.RequesterId,
            ToolName = request.Action.ToolName,
            PayloadDigest = request.Action.PayloadDigest,
            Risk = (int)request.Risk,
            Status = (int)ApprovalStatus.Pending,
            CreatedAt = request.CreatedAt.ToUniversalTime(),
            ExpiresAt = request.ExpiresAt.ToUniversalTime()
        };

        db.AgentApprovals.Add(state);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            db.Entry(state).State = EntityState.Detached;
        }
    }

    public async Task<bool> DecideAsync(
        Guid approvalId,
        string approverId,
        bool approved,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(approverId) ||
            approverId.Length > 256)
            return false;

        now = now.ToUniversalTime();

        var pending = db.AgentApprovals.Where(x =>
            x.Id == approvalId &&
            x.Status == (int)ApprovalStatus.Pending &&
            x.RequesterId != approverId);

        await pending.Where(x => x.ExpiresAt <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(
                x => x.Status, (int)ApprovalStatus.Expired),
                cancellationToken);

        return await pending.Where(x =>
                x.CreatedAt <= now && x.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.ApproverId, approverId)
                .SetProperty(x => x.Status, approved
                    ? (int)ApprovalStatus.Approved
                    : (int)ApprovalStatus.Denied),
                cancellationToken) == 1;
    }

    public async Task<bool> ConsumeAsync(
        Guid approvalId,
        ApprovalAction expectedAction,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expectedAction);

        now = now.ToUniversalTime();

        var approved = db.AgentApprovals.Where(x =>
            x.Id == approvalId &&
            x.Status == (int)ApprovalStatus.Approved &&
            x.TenantId == expectedAction.TenantId &&
            x.RequesterId == expectedAction.RequesterId &&
            x.ToolName == expectedAction.ToolName &&
            x.PayloadDigest == expectedAction.PayloadDigest);

        await approved.Where(x => x.ExpiresAt <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(
                x => x.Status, (int)ApprovalStatus.Expired),
                cancellationToken);

        return await approved.Where(x =>
                x.CreatedAt <= now && x.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(
                x => x.Status, (int)ApprovalStatus.Consumed),
                cancellationToken) == 1;
    }
}