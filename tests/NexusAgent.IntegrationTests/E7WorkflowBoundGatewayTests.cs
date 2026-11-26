using Microsoft.EntityFrameworkCore;
using NexusAgent.Application.Approvals;
using NexusAgent.Application.Tools;
using NexusAgent.Domain.Workflows;
using NexusAgent.Infrastructure.Persistence;
using NexusAgent.Infrastructure.Persistence.Agents;
using Xunit;

namespace NexusAgent.IntegrationTests;

public sealed class E7WorkflowBoundGatewayTests
{
    private static NexusAgentDbContext Open() =>
        new(new DbContextOptionsBuilder<NexusAgentDbContext>()
            .UseNpgsql(
                "Host=127.0.0.1;Port=15432;Database=nexusagent;" +
                "Username=nexusagent;Password=nexusagent_dev_only",
                options => options.UseVector())
            .Options);

    private sealed class TestTool : ITypedTool<string, string>
    {
        private int calls;
        public int Calls => calls;
        public string Name => "workflow-sensitive-action";

        public Task<ToolResult<string>> ExecuteAsync(
            string input, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref calls);
            return Task.FromResult(new ToolResult<string>(true, input));
        }
    }

    private sealed class PermitPolicy : IToolAuthorizationPolicy
    {
        public bool IsAuthorized(ToolCaller caller, string toolName) => true;
    }

    [Fact]
    public async Task WorkflowApprovalAllowsOnlyOneBoundInvocation()
    {
        var workflowId = Guid.NewGuid();
        var otherWorkflowId = Guid.NewGuid();
        var tenant = $"e7-bound-{Guid.NewGuid():N}";
        var caller = new ToolCaller(tenant, "requester");
        var tool = new TestTool();
        var request = new ToolRequest<string>(tool.Name, "approved-payload");

        var risk = new ExplicitActionRiskPolicy(
            new Dictionary<string, ActionRisk>
            {
                [tool.Name] = ActionRisk.High
            });

        await using var db = Open();

        try
        {
            foreach (var id in new[] { workflowId, otherWorkflowId })
            {
                var workflow = Workflow.Create(
                    id, caller.RequesterId, tenant,
                    "workflow-bound acceptance",
                    DateTimeOffset.UtcNow.AddMinutes(-1));

                db.Workflows.Add(workflow);
                await db.SaveChangesAsync();

                workflow.Start(DateTimeOffset.UtcNow);
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();
            }

            var service = new ApprovalService(
                new PostgresApprovalStore(db), risk);

            var approval = await service.RequestAsync(
                request, caller, TimeSpan.FromMinutes(5),
                workflowId: workflowId);

            await using var firstDb = Open();
            await using var secondDb = Open();

            TypedToolGateway Gateway(NexusAgentDbContext context) =>
                new(
                    new ToolRegistry(new IToolRegistration[]
                    {
                        new ToolRegistration<string, string>(tool)
                    }),
                    new ToolAllowList(new[] { tool.Name }),
                    new PermitPolicy(),
                    new NullToolAuditSink(),
                    risk,
                    new PostgresApprovalStore(context));

            var first = Gateway(firstDb);
            var second = Gateway(secondDb);

            var pending = await first.ExecuteAsync<string, string>(
                request, caller,
                approvalId: approval.ApprovalId,
                workflowId: workflowId);

            Assert.False(pending.Result.Succeeded);

            Assert.True(await service.DecideAsync(
                new ApprovalDecision(
                    approval.ApprovalId, "approver", true, tenant)));

            var standalone = await first.ExecuteAsync<string, string>(
                request, caller, approvalId: approval.ApprovalId);

            Assert.False(standalone.Result.Succeeded);

            var wrongWorkflow = await first.ExecuteAsync<string, string>(
                request, caller,
                approvalId: approval.ApprovalId,
                workflowId: otherWorkflowId);

            Assert.False(wrongWorkflow.Result.Succeeded);

            var wrongTenant = await first.ExecuteAsync<string, string>(
                request, new ToolCaller("wrong-tenant", caller.RequesterId),
                approvalId: approval.ApprovalId,
                workflowId: workflowId);

            Assert.False(wrongTenant.Result.Succeeded);

            var wrongRequester = await first.ExecuteAsync<string, string>(
                request, new ToolCaller(tenant, "wrong-requester"),
                approvalId: approval.ApprovalId,
                workflowId: workflowId);

            Assert.False(wrongRequester.Result.Succeeded);

            var modified = await first.ExecuteAsync<string, string>(
                new ToolRequest<string>(tool.Name, "modified"),
                caller,
                approvalId: approval.ApprovalId,
                workflowId: workflowId);

            Assert.False(modified.Result.Succeeded);
            Assert.Equal(0, tool.Calls);

            var responses = await Task.WhenAll(
                first.ExecuteAsync<string, string>(
                    request, caller,
                    approvalId: approval.ApprovalId,
                    workflowId: workflowId),
                second.ExecuteAsync<string, string>(
                    request, caller,
                    approvalId: approval.ApprovalId,
                    workflowId: workflowId));

            Assert.Single(responses, x => x.Result.Succeeded);
            Assert.Equal(1, tool.Calls);

            await using var restarted = Open();

            var replay = await Gateway(restarted)
                .ExecuteAsync<string, string>(
                    request, caller,
                    approvalId: approval.ApprovalId,
                    workflowId: workflowId);

            Assert.False(replay.Result.Succeeded);
            Assert.Equal(1, tool.Calls);

            var persisted = await new PostgresApprovalStore(restarted)
                .GetAsync(approval.ApprovalId, CancellationToken.None);

            Assert.Equal(ApprovalStatus.Consumed, persisted!.Status);
        }
        finally
        {
            db.ChangeTracker.Clear();

            await db.AgentApprovals
                .Where(x => x.TenantId == tenant)
                .ExecuteDeleteAsync();

            await db.Workflows
                .Where(x => x.TenantId == tenant)
                .ExecuteDeleteAsync();

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM kafka_workflow_events
                WHERE tenant_id = {tenant}
                """);
        }
    }
}
