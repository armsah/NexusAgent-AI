using NexusAgent.Application.Tools;
using Xunit;

namespace NexusAgent.UnitTests;

public sealed class TypedToolGatewayTests
{
    private static readonly ToolCaller ValidCaller =
        new("tenant-1", "requester-1");

    [Fact]
    public async Task EmptyAllowList_DeniesExecution()
    {
        var tool = new EchoTool();
        var (gateway, audit) = CreateGateway(tool, Array.Empty<string>(), true);

        var response = await gateway.ExecuteAsync<string, string>(
            new ToolRequest<string>("echo", "hello"),
            ValidCaller);

        Assert.False(response.Result.Succeeded);
        Assert.Equal("tool_access_denied", response.Result.ErrorCode);
        Assert.Equal(0, tool.Calls);
        Assert.Equal("tool_access_denied", Assert.Single(audit.Entries).Outcome);
    }

    [Fact]
    public async Task UnauthorizedCaller_DeniesExecution()
    {
        var tool = new EchoTool();
        var (gateway, audit) = CreateGateway(tool, new[] { "echo" }, false);

        var response = await gateway.ExecuteAsync<string, string>(
            new ToolRequest<string>("echo", "hello"),
            ValidCaller);

        Assert.Equal("tool_access_denied", response.Result.ErrorCode);
        Assert.Equal(0, tool.Calls);
        Assert.Equal("tool_access_denied", Assert.Single(audit.Entries).Outcome);
    }

    [Theory]
    [InlineData("", "requester-1")]
    [InlineData("tenant-1", "")]
    public async Task MissingIdentity_DeniesExecution(
        string tenantId,
        string requesterId)
    {
        var tool = new EchoTool();
        var (gateway, _) = CreateGateway(tool, new[] { "echo" }, true);

        var response = await gateway.ExecuteAsync<string, string>(
            new ToolRequest<string>("echo", "hello"),
            new ToolCaller(tenantId, requesterId));

        Assert.Equal("tool_access_denied", response.Result.ErrorCode);
        Assert.Equal(0, tool.Calls);
    }

    [Fact]
    public async Task RegisteredAuthorizedTool_ExecutesAndAudits()
    {
        var tool = new EchoTool();
        var (gateway, audit) = CreateGateway(tool, new[] { "echo" }, true);

        var response = await gateway.ExecuteAsync<string, string>(
            new ToolRequest<string>("echo", "hello"),
            ValidCaller);

        Assert.True(response.Result.Succeeded);
        Assert.Equal("hello", response.Result.Output);
        Assert.Equal(1, tool.Calls);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(response.CorrelationId, entry.CorrelationId);
        Assert.Equal("tenant-1", entry.TenantId);
        Assert.Equal("requester-1", entry.RequesterId);
        Assert.Equal("echo", entry.ToolName);
        Assert.Equal("succeeded", entry.Outcome);
        Assert.True(entry.Duration >= TimeSpan.Zero);
    }

    [Fact]
    public async Task TypeMismatch_DoesNotInvokeTool()
    {
        var tool = new EchoTool();
        var (gateway, audit) = CreateGateway(tool, new[] { "echo" }, true);

        var response = await gateway.ExecuteAsync<int, int>(
            new ToolRequest<int>("echo", 42),
            ValidCaller);

        Assert.Equal(
            "tool_not_registered_or_type_mismatch",
            response.Result.ErrorCode);
        Assert.Equal(0, tool.Calls);
        Assert.Equal(
            "tool_not_registered_or_type_mismatch",
            Assert.Single(audit.Entries).Outcome);
    }

    [Fact]
    public async Task UnknownTool_DoesNotInvokeRegisteredTool()
    {
        var tool = new EchoTool();
        var (gateway, _) = CreateGateway(
            tool,
            new[] { "echo", "unknown" },
            true);

        var response = await gateway.ExecuteAsync<string, string>(
            new ToolRequest<string>("unknown", "hello"),
            ValidCaller);

        Assert.Equal(
            "tool_not_registered_or_type_mismatch",
            response.Result.ErrorCode);
        Assert.Equal(0, tool.Calls);
    }

    [Fact]
    public async Task ToolFailure_IsAudited()
    {
        var tool = new EchoTool
        {
            Behavior = _ => Task.FromResult(
                new ToolResult<string>(false, null, "business_failure"))
        };

        var (gateway, audit) = CreateGateway(tool, new[] { "echo" }, true);

        var response = await gateway.ExecuteAsync<string, string>(
            new ToolRequest<string>("echo", "hello"),
            ValidCaller);

        Assert.Equal("business_failure", response.Result.ErrorCode);
        Assert.Equal("tool_failed", Assert.Single(audit.Entries).Outcome);
    }

    [Fact]
    public async Task ToolException_IsContainedAndAudited()
    {
        var tool = new EchoTool
        {
            Behavior = _ => throw new InvalidOperationException("secret")
        };

        var (gateway, audit) = CreateGateway(tool, new[] { "echo" }, true);

        var response = await gateway.ExecuteAsync<string, string>(
            new ToolRequest<string>("echo", "hello"),
            ValidCaller);

        Assert.Equal("tool_execution_failed", response.Result.ErrorCode);
        Assert.Equal("tool_execution_failed", Assert.Single(audit.Entries).Outcome);
    }

    [Fact]
    public async Task Timeout_IsContainedAndAudited()
    {
        var tool = new EchoTool
        {
            Behavior = async token =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new ToolResult<string>(true, "unreachable");
            }
        };

        var (gateway, audit) = CreateGateway(tool, new[] { "echo" }, true);

        var response = await gateway.ExecuteAsync<string, string>(
            new ToolRequest<string>("echo", "hello"),
            ValidCaller,
            new ToolExecutionLimits(TimeSpan.FromMilliseconds(30)));

        Assert.Equal("tool_timeout", response.Result.ErrorCode);
        Assert.Equal("tool_timeout", Assert.Single(audit.Entries).Outcome);
    }

    [Fact]
    public async Task Cancellation_IsContainedAndAudited()
    {
        var tool = new EchoTool
        {
            Behavior = async token =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new ToolResult<string>(true, "unreachable");
            }
        };

        var (gateway, audit) = CreateGateway(tool, new[] { "echo" }, true);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var response = await gateway.ExecuteAsync<string, string>(
            new ToolRequest<string>("echo", "hello"),
            ValidCaller,
            cancellationToken: cancellation.Token);

        Assert.Equal("tool_cancelled", response.Result.ErrorCode);
        Assert.Equal("tool_cancelled", Assert.Single(audit.Entries).Outcome);
    }

    [Fact]
    public void DuplicateRegistration_IsRejected()
    {
        var first = new EchoTool();
        var second = new EchoTool();

        Assert.Throws<InvalidOperationException>(() =>
            new ToolRegistry(new IToolRegistration[]
            {
                new ToolRegistration<string, string>(first),
                new ToolRegistration<string, string>(second)
            }));
    }

    [Fact]
    public void InvalidExecutionLimits_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ToolExecutionLimits(TimeSpan.Zero).Validate());

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ToolExecutionLimits(TimeSpan.FromMinutes(3)).Validate());
    }

    private static (TypedToolGateway Gateway, RecordingAuditSink Audit)
        CreateGateway(
            EchoTool tool,
            IEnumerable<string> allowed,
            bool authorized)
    {
        var registry = new ToolRegistry(new IToolRegistration[]
        {
            new ToolRegistration<string, string>(tool)
        });

        var audit = new RecordingAuditSink();

        return (
            new TypedToolGateway(
                registry,
                new ToolAllowList(allowed),
                new TestAuthorizationPolicy(authorized),
                audit),
            audit);
    }

    private sealed class EchoTool : ITypedTool<string, string>
    {
        public string Name => "echo";

        public int Calls { get; private set; }

        public Func<CancellationToken, Task<ToolResult<string>>>? Behavior
        {
            get;
            init;
        }

        public async Task<ToolResult<string>> ExecuteAsync(
            string input,
            CancellationToken cancellationToken)
        {
            Calls++;

            if (Behavior is not null)
            {
                return await Behavior(cancellationToken);
            }

            return new ToolResult<string>(true, input);
        }
    }

    private sealed class TestAuthorizationPolicy(bool authorized)
        : IToolAuthorizationPolicy
    {
        public bool IsAuthorized(
            ToolCaller caller,
            string toolName) => authorized;
    }

    private sealed class RecordingAuditSink : IToolAuditSink
    {
        public List<ToolExecutionAudit> Entries { get; } = new();

        public Task RecordAsync(
            ToolExecutionAudit audit,
            CancellationToken cancellationToken)
        {
            Entries.Add(audit);
            return Task.CompletedTask;
        }
    }
}