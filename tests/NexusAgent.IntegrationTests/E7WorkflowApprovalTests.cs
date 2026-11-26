using Microsoft.EntityFrameworkCore;
using NexusAgent.Application.Approvals;
using NexusAgent.Application.Tools;
using NexusAgent.Domain.Workflows;
using NexusAgent.Infrastructure.Persistence;
using NexusAgent.Infrastructure.Persistence.Agents;
using Xunit;

namespace NexusAgent.IntegrationTests;

public sealed class E7WorkflowApprovalTests
{
    private static NexusAgentDbContext Open() =>
        new(new DbContextOptionsBuilder<NexusAgentDbContext>()
            .UseNpgsql(
                "Host=127.0.0.1;Port=15432;Database=nexusagent;" +
                "Username=nexusagent;Password=nexusagent_dev_only",
                options => options.UseVector())
            .Options);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AssociatedDecisionChangesWorkflowAndJournalAtomically(
        bool approved)
    {
        var workflowId = Guid.NewGuid();
        var tenant = $"e7-approval-{Guid.NewGuid():N}";
        var requester = "requester";
        var now = DateTimeOffset.UtcNow.AddMinutes(-2);
        var ct = CancellationToken.None;

        await using var db = Open();

        var store = new PostgresApprovalStore(db);
        var policy = new ExplicitActionRiskPolicy(
            new Dictionary<string, ActionRisk>
            {
                ["sensitive-action"] = ActionRisk.High
            });

        var service = new ApprovalService(store, policy);
        var caller = new ToolCaller(tenant, requester);
        var request = new ToolRequest<string>(
            "sensitive-action", "payload");

        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO workflows
                    (id, tenant_id, requester_id, request, status,
                     created_at_utc, updated_at_utc)
                VALUES
                    ({workflowId}, {tenant}, {requester},
                     'approval acceptance', 'Created', {now}, {now})
                """);

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE workflows
                SET status = 'Running', updated_at_utc = {now.AddSeconds(1)}
                WHERE id = {workflowId}
                """);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.RequestAsync(
                    request,
                    new ToolCaller("wrong-tenant", requester),
                    TimeSpan.FromMinutes(5),
                    ct,
                    workflowId));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.RequestAsync(
                    request,
                    new ToolCaller(tenant, "wrong-requester"),
                    TimeSpan.FromMinutes(5),
                    ct,
                    workflowId));

            var approval = await service.RequestAsync(
                request, caller, TimeSpan.FromMinutes(5),
                ct, workflowId);

            Assert.Equal(workflowId, approval.WorkflowId);

            Assert.Equal(
                WorkflowStatus.WaitingForApproval,
                await Status());

            Assert.False(await service.DecideAsync(
                new ApprovalDecision(
                    approval.ApprovalId, "approver", approved)));

            Assert.False(await service.DecideAsync(
                new ApprovalDecision(
                    approval.ApprovalId, "approver", approved,
                    "wrong-tenant")));

            Assert.False(await service.DecideAsync(
                new ApprovalDecision(
                    approval.ApprovalId, requester, approved, tenant)));

            Assert.Equal(
                ApprovalStatus.Pending,
                (await store.GetAsync(approval.ApprovalId, ct))!.Status);

            var standalone = await service.RequestAsync(
                request, caller, TimeSpan.FromMinutes(5));

            Assert.True(await service.DecideAsync(
                new ApprovalDecision(
                    standalone.ApprovalId, "approver", false)));

            Assert.Equal(
                WorkflowStatus.WaitingForApproval,
                await Status());

            // Force an incompatible workflow state to verify that
            // the approval update is rolled back when transition fails.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE workflows
                SET status = 'Running'
                WHERE id = {workflowId}
                """);

            Assert.False(await service.DecideAsync(
                new ApprovalDecision(
                    approval.ApprovalId, "approver", approved, tenant)));

            Assert.Equal(
                ApprovalStatus.Pending,
                (await store.GetAsync(approval.ApprovalId, ct))!.Status);

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE workflows
                SET status = 'WaitingForApproval'
                WHERE id = {workflowId}
                """);

            var decision = new ApprovalDecision(
                approval.ApprovalId, "approver", approved, tenant);

            Assert.True(await service.DecideAsync(decision));
            Assert.False(await service.DecideAsync(decision));

            Assert.Equal(
                approved ? WorkflowStatus.Running : WorkflowStatus.Rejected,
                await Status());

            Assert.Equal(
                approved ? ApprovalStatus.Approved : ApprovalStatus.Denied,
                (await store.GetAsync(approval.ApprovalId, ct))!.Status);

            Assert.False(await store.ConsumeAsync(
                approval.ApprovalId, approval.Action,
                DateTimeOffset.UtcNow, ct));

            var events = await db.Database.SqlQuery<string>($"""
                SELECT payload->>'eventType' AS "Value"
                FROM kafka_workflow_events
                WHERE workflow_id = {workflowId}
                ORDER BY sequence
                """).ToListAsync();

            Assert.Equal(
                new[]
                {
                    "STARTED",
                    "APPROVAL_REQUIRED",
                    "APPROVED",
                    "APPROVAL_REQUIRED",
                    approved ? "APPROVED" : "REJECTED"
                },
                events);
        }
        finally
        {
            db.ChangeTracker.Clear();

            await db.AgentApprovals
                .Where(x => x.TenantId == tenant)
                .ExecuteDeleteAsync();

            await db.Workflows
                .Where(x => x.Id == workflowId)
                .ExecuteDeleteAsync();

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM kafka_workflow_events
                WHERE workflow_id = {workflowId}
                """);
        }

        async Task<WorkflowStatus> Status() =>
            await db.Workflows.AsNoTracking()
                .Where(x => x.Id == workflowId)
                .Select(x => x.Status)
                .SingleAsync();
    }
}
