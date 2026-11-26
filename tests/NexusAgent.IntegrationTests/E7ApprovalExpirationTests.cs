using Microsoft.EntityFrameworkCore;
using NexusAgent.Application.Approvals;
using NexusAgent.Infrastructure.Persistence;
using NexusAgent.Infrastructure.Persistence.Agents;
using Xunit;

namespace NexusAgent.IntegrationTests;

public sealed class E7ApprovalExpirationTests
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
    public async Task ExpirationAndDecisionRaceProducesOneCancellation(
        bool approve)
    {
        var workflowId = Guid.NewGuid();
        var approvalId = Guid.NewGuid();
        var tenant = $"e7-expiry-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        var created = now.AddMinutes(-10);
        var expires = now.AddMinutes(-1);
        var ct = CancellationToken.None;

        await using var db = Open();

        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO workflows
                    (id, tenant_id, requester_id, request, status,
                     created_at_utc, updated_at_utc)
                VALUES
                    ({workflowId}, {tenant}, 'requester',
                     'expiration race', 'Created', {created}, {created})
                """);

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE workflows SET status = 'Running'
                WHERE id = {workflowId}
                """);

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE workflows SET status = 'WaitingForApproval'
                WHERE id = {workflowId}
                """);

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO agent_approvals
                    ("Id", "WorkflowId", "TenantId", "RequesterId",
                     "ToolName", "PayloadDigest", "Risk", "Status",
                     "CreatedAt", "ExpiresAt")
                VALUES
                    ({approvalId}, {workflowId}, {tenant}, 'requester',
                     'sensitive-action', repeat('A', 64), 2, 0,
                     {created}, {expires})
                """);

            await using var decisionDb = Open();
            await using var expirationDb = Open();

            var decisionStore = new PostgresApprovalStore(decisionDb);

            var decision = decisionStore.DecideAsync(
                approvalId, "approver", approve, now, ct, tenant);

            var expiration = expirationDb.Database.SqlQueryRaw<int>(
                """
                SELECT expire_workflow_approvals(1000) AS "Value"
                """).SingleAsync(ct);

            await Task.WhenAll(decision, expiration);

            Assert.False(await decision);

            var store = new PostgresApprovalStore(db);

            Assert.Equal(
                ApprovalStatus.Expired,
                (await store.GetAsync(approvalId, ct))!.Status);

            await AssertCancelled(db, workflowId);

            await db.Database.SqlQueryRaw<int>(
                """
                SELECT expire_workflow_approvals(1000) AS "Value"
                """).SingleAsync(ct);

            await AssertCancelled(db, workflowId);
        }
        finally
        {
            await db.AgentApprovals.Where(x => x.Id == approvalId)
                .ExecuteDeleteAsync();

            await db.Workflows.Where(x => x.Id == workflowId)
                .ExecuteDeleteAsync();

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM kafka_workflow_events
                WHERE workflow_id = {workflowId}
                """);
        }
    }

    private static async Task AssertCancelled(
        NexusAgentDbContext db, Guid workflowId)
    {
        var status = await db.Database.SqlQuery<string>($"""
            SELECT status AS "Value"
            FROM workflows WHERE id = {workflowId}
            """).SingleAsync();

        Assert.Equal("Cancelled", status);

        var events = await db.Database.SqlQuery<string>($"""
            SELECT payload->>'eventType' AS "Value"
            FROM kafka_workflow_events
            WHERE workflow_id = {workflowId}
            ORDER BY sequence
            """).ToListAsync();

        Assert.Equal(
            new[] { "STARTED", "APPROVAL_REQUIRED", "CANCELLED" },
            events);
    }
}
