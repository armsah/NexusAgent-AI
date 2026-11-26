using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NexusAgent.Application.Agents;
using NexusAgent.Application.Workflows;

namespace NexusAgent.Infrastructure.Persistence.Agents;

public sealed class PostgresAgentExecutionStore(
    NexusAgentDbContext db) : IDurableAgentExecutionStore
{
    private IQueryable<AgentExecutionState> Owned(WorkflowAccess caller) =>
        db.AgentExecutions.Where(x =>
            x.TenantId == caller.TenantId &&
            x.RequesterId == caller.RequesterId &&
            db.Workflows.Any(w =>
                w.Id == x.WorkflowId &&
                w.TenantId == caller.TenantId &&
                w.RequesterId == caller.RequesterId));

    private static DurableAgentExecution Snapshot(AgentExecutionState x) =>
        new(x.Id, x.WorkflowId, x.Status, x.NextStage,
            x.AttemptCount, x.DeadlineAt, x.FinalAnswer, x.FailureCode);

    public async Task<DurableAgentExecution> CreateOrGetAsync(
        Guid executionId, Guid workflowId, WorkflowAccess caller,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        BoundedAgentPlan.ValidateTimeout(timeout);

        if (executionId == Guid.Empty || workflowId == Guid.Empty)
            throw new ArgumentException("Execution and workflow IDs are required.");

        var workflow = await db.Workflows.AsNoTracking()
            .SingleOrDefaultAsync(w => w.Id == workflowId, cancellationToken);

        if (workflow is null || !caller.CanAccess(workflow))
            throw new UnauthorizedAccessException();

        if (string.IsNullOrWhiteSpace(workflow.Request) ||
            workflow.Request.Length > 2000)
            throw new ArgumentException("Invalid workflow request.");

        var digest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(workflow.Request)));

        var now = DateTimeOffset.UtcNow;
        var deadline = now.Add(timeout);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO agent_executions
            ("Id","WorkflowId","TenantId","RequesterId","RequestDigest",
             "Status","PlanVersion","NextStage","AttemptCount",
             "CreatedAt","UpdatedAt","DeadlineAt")
            VALUES
            ({executionId},{workflowId},{caller.TenantId},{caller.RequesterId},
             {digest},'Pending',{BoundedAgentPlan.Version},0,0,
             {now},{now},{deadline})
            ON CONFLICT ("Id") DO NOTHING
            """, cancellationToken);

        var state = await Owned(caller).AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == executionId, cancellationToken);

        if (state is null ||
            state.WorkflowId != workflowId ||
            state.RequestDigest != digest ||
            state.PlanVersion != BoundedAgentPlan.Version)
            throw new UnauthorizedAccessException("Execution binding mismatch.");

        return Snapshot(state);
    }

    public async Task<DurableAgentExecution?> GetAsync(
        Guid executionId, WorkflowAccess caller,
        CancellationToken cancellationToken)
    {
        var state = await Owned(caller).AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == executionId, cancellationToken);

        return state is null ? null : Snapshot(state);
    }

    public async Task<AgentExecutionLease?> TryClaimAsync(
        Guid executionId, WorkflowAccess caller,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var token = Guid.NewGuid();

        var query = Owned(caller).Where(x =>
            x.Id == executionId &&
            (x.Status == "Pending" ||
             (x.Status == "Running" && x.LeaseExpiresAt <= DateTimeOffset.UtcNow)));

        await query.Where(x =>
                x.DeadlineAt <= DateTimeOffset.UtcNow ||
                x.AttemptCount >= BoundedAgentPlan.MaxAttemptsPerStage)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, "Failed")
                .SetProperty(x => x.FailureCode, "ExecutionBudgetExhausted")
                .SetProperty(x => x.LeaseToken, (Guid?)null)
                .SetProperty(x => x.LeaseExpiresAt, (DateTimeOffset?)null)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken);

        var expires = now.Add(BoundedAgentPlan.MaximumLease);

        var count = await query.Where(x =>
                x.DeadlineAt > DateTimeOffset.UtcNow &&
                x.NextStage < BoundedAgentPlan.StageCount &&
                x.AttemptCount < BoundedAgentPlan.MaxAttemptsPerStage &&
                x.PlanVersion == BoundedAgentPlan.Version)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, "Running")
                .SetProperty(x => x.LeaseToken, (Guid?)token)
                .SetProperty(x => x.LeaseExpiresAt,
                    x => x.DeadlineAt < expires ? x.DeadlineAt : expires)
                .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken);

        if (count != 1)
            return null;

        var state = await Owned(caller).AsNoTracking()
            .SingleOrDefaultAsync(x =>
                x.Id == executionId && x.LeaseToken == token,
                cancellationToken);

        return state is null ? null : new AgentExecutionLease(
            state.Id, token, state.NextStage, state.AttemptCount,
            state.LeaseExpiresAt!.Value, state.DeadlineAt);
    }

    public async Task<IReadOnlyList<AgentMemoryCheckpoint>> ReadMemoryAsync(
        Guid executionId, WorkflowAccess caller,
        CancellationToken cancellationToken)
    {
        return await db.AgentExecutionCheckpoints.AsNoTracking()
            .Where(x => x.ExecutionId == executionId &&
                Owned(caller).Any(e => e.Id == x.ExecutionId))
            .OrderBy(x => x.StageNumber)
            .Select(x => new AgentMemoryCheckpoint(
                x.StageNumber, x.InvocationId, x.AgentName,
                x.Succeeded, x.Output))
            .ToArrayAsync(cancellationToken);
    }

    private IQueryable<AgentExecutionState> Fenced(
        AgentExecutionLease lease, WorkflowAccess caller) =>
        Owned(caller).Where(x =>
            x.Id == lease.ExecutionId &&
            x.Status == "Running" &&
            x.LeaseToken == lease.LeaseToken &&
            x.NextStage == lease.Stage &&
            x.AttemptCount == lease.Attempt &&
            x.LeaseExpiresAt > DateTimeOffset.UtcNow &&
            x.DeadlineAt > DateTimeOffset.UtcNow);

    public async Task<bool> CheckpointAsync(
        AgentExecutionLease lease, WorkflowAccess caller,
        AgentResult result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);

        var name = BoundedAgentPlan.GetAgentName(lease.Stage);
        var output = result.Output ?? "";

        if (output.Length > 32000 ||
            (lease.Stage == 3 && output.Length > 8000))
            throw new ArgumentException("Checkpoint exceeds output limits.");

        var now = DateTimeOffset.UtcNow;
        var completed = result.Succeeded &&
            lease.Stage == BoundedAgentPlan.StageCount - 1;

        var status = !result.Succeeded ? "Failed" :
            completed ? "Completed" : "Pending";

        await using var transaction =
            await db.Database.BeginTransactionAsync(cancellationToken);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT "Id"
            FROM agent_executions
            WHERE "Id" = {lease.ExecutionId}
              AND "TenantId" = {caller.TenantId}
              AND "RequesterId" = {caller.RequesterId}
            FOR UPDATE
            """, cancellationToken);

        var checkedAt = DateTimeOffset.UtcNow;

        var count = await Fenced(lease, caller)
            .Where(x =>
                x.LeaseExpiresAt > checkedAt &&
                x.DeadlineAt > checkedAt)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, status)
                .SetProperty(x => x.NextStage, lease.Stage + 1)
                .SetProperty(x => x.AttemptCount, 0)
                .SetProperty(x => x.LeaseToken, (Guid?)null)
                .SetProperty(x => x.LeaseExpiresAt, (DateTimeOffset?)null)
                .SetProperty(x => x.FinalAnswer, completed ? output : null)
                .SetProperty(x => x.FailureCode,
                    result.Succeeded ? null : "AgentRejected")
                .SetProperty(x => x.UpdatedAt, now), cancellationToken);

        if (count != 1)
            return false;

        var invocationId = BoundedAgentPlan.GetInvocationId(
            lease.ExecutionId, lease.Stage);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO agent_execution_checkpoints
            ("ExecutionId","StageNumber","InvocationId","AgentName",
             "Succeeded","Output","CompletedAt")
            VALUES
            ({lease.ExecutionId},{lease.Stage},{invocationId},{name},
             {result.Succeeded},{output},{now})
            """, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ReleaseForRetryAsync(
        AgentExecutionLease lease, WorkflowAccess caller,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var exhausted =
            lease.Attempt >= BoundedAgentPlan.MaxAttemptsPerStage;

        await using var transaction =
            await db.Database.BeginTransactionAsync(cancellationToken);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT "Id" FROM agent_executions
            WHERE "Id" = {lease.ExecutionId}
              AND "TenantId" = {caller.TenantId}
              AND "RequesterId" = {caller.RequesterId}
            FOR UPDATE
            """, cancellationToken);

        var checkedAt = DateTimeOffset.UtcNow;

        var released = await Fenced(lease, caller)
            .Where(x =>
                x.LeaseExpiresAt > checkedAt &&
                x.DeadlineAt > checkedAt)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status,
                    exhausted ? "Failed" : "Pending")
                .SetProperty(x => x.FailureCode,
                    exhausted ? "RetryBudgetExhausted" : null)
                .SetProperty(x => x.LeaseToken, (Guid?)null)
                .SetProperty(x => x.LeaseExpiresAt, (DateTimeOffset?)null)
                .SetProperty(x => x.UpdatedAt, now),
                cancellationToken) == 1;

        await transaction.CommitAsync(cancellationToken);
        return released;
    }

    public async Task<bool> CancelAsync(
        Guid executionId, WorkflowAccess caller,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        return await Owned(caller).Where(x =>
                x.Id == executionId &&
                (x.Status == "Pending" || x.Status == "Running"))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, "Cancelled")
                .SetProperty(x => x.FailureCode, "Cancelled")
                .SetProperty(x => x.LeaseToken, (Guid?)null)
                .SetProperty(x => x.LeaseExpiresAt, (DateTimeOffset?)null)
                .SetProperty(x => x.UpdatedAt, now),
                cancellationToken) == 1;
    }
}