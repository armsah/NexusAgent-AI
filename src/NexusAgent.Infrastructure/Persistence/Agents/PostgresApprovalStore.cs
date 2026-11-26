using Microsoft.EntityFrameworkCore;
using NexusAgent.Application.Approvals;
using NexusAgent.Domain.Workflows;

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
            state.ExpiresAt,
            state.WorkflowId);
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
            WorkflowId = request.WorkflowId,
            TenantId = request.Action.TenantId,
            RequesterId = request.Action.RequesterId,
            ToolName = request.Action.ToolName,
            PayloadDigest = request.Action.PayloadDigest,
            Risk = (int)request.Risk,
            Status = (int)ApprovalStatus.Pending,
            CreatedAt = request.CreatedAt.ToUniversalTime(),
            ExpiresAt = request.ExpiresAt.ToUniversalTime()
        };

        await using var transaction =
            await db.Database.BeginTransactionAsync(cancellationToken);

        Workflow? workflow = null;

        try
        {
            if (request.WorkflowId is Guid workflowId)
            {
                workflow = await db.Workflows.Include(x => x.Steps)
                    .SingleOrDefaultAsync(x =>
                        x.Id == workflowId &&
                        x.TenantId == request.Action.TenantId &&
                        x.RequesterId == request.Action.RequesterId,
                        cancellationToken);

                if (workflow is null ||
                    workflow.Status != WorkflowStatus.Running)
                {
                    throw new InvalidOperationException(
                        "Workflow is not eligible for approval.");
                }

                workflow.WaitForApproval(request.CreatedAt);
            }

            db.AgentApprovals.Add(state);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            db.Entry(state).State = EntityState.Detached;
            if (workflow is not null)
                db.Entry(workflow).State = EntityState.Detached;
        }
    }

    public async Task<bool> DecideAsync(
        Guid approvalId,
        string approverId,
        bool approved,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        string? tenantId = null)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(approverId) ||
            approverId.Length > 256)
            return false;

        now = now.ToUniversalTime();

        await using var transaction =
            await db.Database.BeginTransactionAsync(cancellationToken);

        var pending = db.AgentApprovals.Where(x =>
            x.Id == approvalId &&
            x.Status == (int)ApprovalStatus.Pending &&
            x.RequesterId != approverId &&
            (tenantId == null
                ? x.WorkflowId == null
                : x.TenantId == tenantId));

        var state = await pending.AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

        if (state is null)
            return false;

        if (state.ExpiresAt <= now)
        {
            await pending.Where(x => x.ExpiresAt <= now)
                .ExecuteUpdateAsync(s => s.SetProperty(
                    x => x.Status, (int)ApprovalStatus.Expired),
                    cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        var affected = await pending.Where(x =>
                x.CreatedAt <= now && x.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.ApproverId, approverId)
                .SetProperty(x => x.Status, approved
                    ? (int)ApprovalStatus.Approved
                    : (int)ApprovalStatus.Denied),
                cancellationToken);

        if (affected != 1)
            return false;

        Workflow? workflow = null;

        try
        {
            if (state.WorkflowId is Guid workflowId)
            {
                workflow = await db.Workflows.Include(x => x.Steps)
                    .SingleOrDefaultAsync(x =>
                        x.Id == workflowId &&
                        x.TenantId == state.TenantId &&
                        x.RequesterId == state.RequesterId,
                        cancellationToken);

                if (workflow is null ||
                    workflow.Status != WorkflowStatus.WaitingForApproval)
                    return false;

                if (approved)
                    workflow.ResumeAfterApproval(now);
                else
                    workflow.Reject(now);

                await db.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        finally
        {
            if (workflow is not null)
                db.Entry(workflow).State = EntityState.Detached;
        }
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
            x.WorkflowId == null &&
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