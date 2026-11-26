using Microsoft.EntityFrameworkCore;
using NexusAgent.Application.Approvals;
using NexusAgent.Application.Tools;
using NexusAgent.Infrastructure.Persistence;
using NexusAgent.Infrastructure.Persistence.Agents;

namespace NexusAgent.IntegrationTests;

public sealed class DurableApprovalGatewayTests
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
        public string Name => "durable-sensitive-action";
        public int Calls { get; private set; }

        public Task<ToolResult<string>> ExecuteAsync(
            string input, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(
                new ToolResult<string>(true, input));
        }
    }

    private sealed class PermitPolicy : IToolAuthorizationPolicy
    {
        public bool IsAuthorized(
            ToolCaller caller, string toolName) => true;
    }

    [Fact]
    public async Task PersistedApprovalAuthorizesExactlyOneGatewayInvocation()
    {
        var tool = new TestTool();

        var caller = new ToolCaller(
            "e4-gateway-tenant", "requester");

        var request = new ToolRequest<string>(
            tool.Name, "approved-payload");

        var risk = new ExplicitActionRiskPolicy(
            new Dictionary<string, ActionRisk>
            {
                [tool.Name] = ActionRisk.High
            });

        Guid approvalId = Guid.Empty;

        try
        {
            await using (var first = Open())
            {
                var service = new ApprovalService(
                    new PostgresApprovalStore(first), risk);

                var approval = await service.RequestAsync(
                    request, caller, TimeSpan.FromMinutes(5));

                approvalId = approval.ApprovalId;

                Assert.True(await service.DecideAsync(
                    new ApprovalDecision(
                        approvalId, "independent-approver", true)));
            }

            await using var restarted = Open();

            var gateway = new TypedToolGateway(
                new ToolRegistry(new IToolRegistration[]
                {
                    new ToolRegistration<string, string>(tool)
                }),
                new ToolAllowList(new[] { tool.Name }),
                new PermitPolicy(),
                new NullToolAuditSink(),
                risk,
                new PostgresApprovalStore(restarted));

            var missing = await gateway.ExecuteAsync<string, string>(
                request, caller);

            Assert.False(missing.Result.Succeeded);

            var modified = await gateway.ExecuteAsync<string, string>(
                new ToolRequest<string>(tool.Name, "modified"),
                caller, approvalId: approvalId);

            Assert.False(modified.Result.Succeeded);
            Assert.Equal(0, tool.Calls);

            var accepted = await gateway.ExecuteAsync<string, string>(
                request, caller, approvalId: approvalId);

            var replay = await gateway.ExecuteAsync<string, string>(
                request, caller, approvalId: approvalId);

            Assert.True(accepted.Result.Succeeded);
            Assert.False(replay.Result.Succeeded);
            Assert.Equal(1, tool.Calls);
        }
        finally
        {
            if (approvalId != Guid.Empty)
            {
                await using var cleanup = Open();

                await cleanup.AgentApprovals
                    .Where(x => x.Id == approvalId)
                    .ExecuteDeleteAsync();
            }
        }
    }
}