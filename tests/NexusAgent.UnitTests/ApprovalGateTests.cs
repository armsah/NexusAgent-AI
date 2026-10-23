using NexusAgent.Application.Approvals;
using NexusAgent.Application.Tools;
using Xunit;

namespace NexusAgent.UnitTests;

public sealed class ApprovalGateTests
{
    private sealed record Input(string Value);

    private sealed class TestTool : ITypedTool<Input, string>
    {
        public string Name => "sensitive-action";

        public int Executions { get; private set; }

        public Task<ToolResult<string>> ExecuteAsync(
            Input input,
            CancellationToken cancellationToken)
        {
            Executions++;

            return Task.FromResult(
                new ToolResult<string>(true, input.Value));
        }
    }

    private sealed class PermitPolicy : IToolAuthorizationPolicy
    {
        public bool IsAuthorized(
            ToolCaller caller,
            string toolName) => true;
    }

    private static readonly ToolCaller Caller =
        new("tenant-a", "requester-a");

    private static readonly ToolRequest<Input> Request =
        new("sensitive-action", new Input("original"));

    private static (
        TypedToolGateway Gateway,
        ApprovalService Service,
        InMemoryApprovalStore Store,
        TestTool Tool) Create()
    {
        var tool = new TestTool();

        var registry = new ToolRegistry(
            new IToolRegistration[]
            {
                new ToolRegistration<Input, string>(tool)
            });

        var allowList = new ToolAllowList(
            new[] { tool.Name });

        var risk = new ExplicitActionRiskPolicy(
            new[]
            {
                new KeyValuePair<string, ActionRisk>(
                    tool.Name,
                    ActionRisk.High)
            });

        var store = new InMemoryApprovalStore();

        var gateway = new TypedToolGateway(
            registry,
            allowList,
            new PermitPolicy(),
            new NullToolAuditSink(),
            risk,
            store);

        return (
            gateway,
            new ApprovalService(store, risk),
            store,
            tool);
    }

    [Fact]
    public async Task MissingApproval_DeniesExecution()
    {
        var setup = Create();

        var result = await setup.Gateway.ExecuteAsync<Input, string>(
            Request,
            Caller);

        Assert.False(result.Result.Succeeded);
        Assert.Equal("approval_required", result.Result.ErrorCode);
        Assert.Equal(0, setup.Tool.Executions);
    }

    [Fact]
    public async Task ApprovedAction_ExecutesOnlyOnce()
    {
        var setup = Create();

        var approval = await setup.Service.RequestAsync(
            Request,
            Caller,
            TimeSpan.FromMinutes(5));

        Assert.True(await setup.Service.DecideAsync(
            new ApprovalDecision(
                approval.ApprovalId,
                "independent-approver",
                true)));

        var first = await setup.Gateway.ExecuteAsync<Input, string>(
            Request,
            Caller,
            approvalId: approval.ApprovalId);

        var replay = await setup.Gateway.ExecuteAsync<Input, string>(
            Request,
            Caller,
            approvalId: approval.ApprovalId);

        Assert.True(first.Result.Succeeded);
        Assert.False(replay.Result.Succeeded);
        Assert.Equal(1, setup.Tool.Executions);
    }

    [Fact]
    public async Task SelfApproval_IsRejected()
    {
        var setup = Create();

        var approval = await setup.Service.RequestAsync(
            Request,
            Caller,
            TimeSpan.FromMinutes(5));

        Assert.False(await setup.Service.DecideAsync(
            new ApprovalDecision(
                approval.ApprovalId,
                Caller.RequesterId,
                true)));
    }

    [Fact]
    public async Task CrossTenantApproval_IsRejected()
    {
        var setup = Create();

        var approval = await setup.Service.RequestAsync(
            Request,
            Caller,
            TimeSpan.FromMinutes(5));

        Assert.True(await setup.Service.DecideAsync(
            new ApprovalDecision(
                approval.ApprovalId,
                "independent-approver",
                true)));

        var result = await setup.Gateway.ExecuteAsync<Input, string>(
            Request,
            new ToolCaller("tenant-b", Caller.RequesterId),
            approvalId: approval.ApprovalId);

        Assert.False(result.Result.Succeeded);
        Assert.Equal(0, setup.Tool.Executions);
    }

    [Fact]
    public async Task CrossRequesterApproval_IsRejected()
    {
        var setup = Create();

        var approval = await setup.Service.RequestAsync(
            Request,
            Caller,
            TimeSpan.FromMinutes(5));

        Assert.True(await setup.Service.DecideAsync(
            new ApprovalDecision(
                approval.ApprovalId,
                "independent-approver",
                true)));

        var result = await setup.Gateway.ExecuteAsync<Input, string>(
            Request,
            new ToolCaller(Caller.TenantId, "requester-b"),
            approvalId: approval.ApprovalId);

        Assert.False(result.Result.Succeeded);
        Assert.Equal(0, setup.Tool.Executions);
    }

    [Fact]
    public async Task ModifiedPayload_IsRejected()
    {
        var setup = Create();

        var approval = await setup.Service.RequestAsync(
            Request,
            Caller,
            TimeSpan.FromMinutes(5));

        Assert.True(await setup.Service.DecideAsync(
            new ApprovalDecision(
                approval.ApprovalId,
                "independent-approver",
                true)));

        var modified = new ToolRequest<Input>(
            Request.ToolName,
            new Input("modified"));

        var result = await setup.Gateway.ExecuteAsync<Input, string>(
            modified,
            Caller,
            approvalId: approval.ApprovalId);

        Assert.False(result.Result.Succeeded);
        Assert.Equal(0, setup.Tool.Executions);
    }

    [Fact]
    public async Task DeniedApproval_CannotExecute()
    {
        var setup = Create();

        var approval = await setup.Service.RequestAsync(
            Request,
            Caller,
            TimeSpan.FromMinutes(5));

        Assert.True(await setup.Service.DecideAsync(
            new ApprovalDecision(
                approval.ApprovalId,
                "independent-approver",
                false)));

        var result = await setup.Gateway.ExecuteAsync<Input, string>(
            Request,
            Caller,
            approvalId: approval.ApprovalId);

        Assert.False(result.Result.Succeeded);
        Assert.Equal(0, setup.Tool.Executions);
    }

    [Fact]
    public async Task ExpiredApproval_CannotExecute()
    {
        var setup = Create();

        var approval = await setup.Service.RequestAsync(
            Request,
            Caller,
            TimeSpan.FromMinutes(5));

        Assert.True(await setup.Service.DecideAsync(
            new ApprovalDecision(
                approval.ApprovalId,
                "independent-approver",
                true)));

        var action = ApprovalActionFactory.Create(Request, Caller);

        Assert.False(await setup.Store.ConsumeAsync(
            approval.ApprovalId,
            action,
            approval.ExpiresAt,
            CancellationToken.None));

        var result = await setup.Gateway.ExecuteAsync<Input, string>(
            Request,
            Caller,
            approvalId: approval.ApprovalId);

        Assert.False(result.Result.Succeeded);
        Assert.Equal(0, setup.Tool.Executions);
    }

    [Fact]
    public void UnknownToolRisk_DefaultsToCritical()
    {
        var policy = new ExplicitActionRiskPolicy(
            Array.Empty<KeyValuePair<string, ActionRisk>>());

        Assert.Equal(
            ActionRisk.Critical,
            policy.Classify("unknown-tool"));

        Assert.True(
            policy.RequiresApproval(
                policy.Classify("unknown-tool")));
    }
}