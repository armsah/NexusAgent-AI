using Microsoft.EntityFrameworkCore;
using NexusAgent.Application.Approvals;
using NexusAgent.Application.Tools;
using NexusAgent.Infrastructure.Persistence;
using NexusAgent.Infrastructure.Persistence.Agents;

if (args.Length != 4)
    return 10;

var workflowId = Guid.Parse(args[0]);
var approvalId = Guid.Parse(args[1]);
var caller = new ToolCaller(args[2], "requester");
var marker = args[3];

await using var db = new NexusAgentDbContext(
    new DbContextOptionsBuilder<NexusAgentDbContext>()
        .UseNpgsql(
            "Host=127.0.0.1;Port=15432;Database=nexusagent;" +
            "Username=nexusagent;Password=nexusagent_dev_only",
            options => options.UseVector())
        .Options);

var tool = new BlockingTool(marker);

var gateway = new TypedToolGateway(
    new ToolRegistry(new IToolRegistration[]
    {
        new ToolRegistration<string, string>(tool)
    }),
    new ToolAllowList(new[] { tool.Name }),
    new PermitPolicy(),
    new NullToolAuditSink(),
    new ExplicitActionRiskPolicy(
        new Dictionary<string, ActionRisk>
        {
            [tool.Name] = ActionRisk.High
        }),
    new PostgresApprovalStore(db));

var result = await gateway.ExecuteAsync<string, string>(
    new ToolRequest<string>(tool.Name, "approved-payload"),
    caller,
    approvalId: approvalId,
    workflowId: workflowId);

return result.Result.Succeeded ? 0 : 20;

sealed class PermitPolicy : IToolAuthorizationPolicy
{
    public bool IsAuthorized(ToolCaller caller, string toolName) => true;
}

sealed class BlockingTool(string marker) : ITypedTool<string, string>
{
    public string Name => "workflow-sensitive-action";

    public async Task<ToolResult<string>> ExecuteAsync(
        string input, CancellationToken cancellationToken)
    {
        File.AppendAllText(marker, "invoked" + Environment.NewLine);

        await Task.Delay(
            Timeout.InfiniteTimeSpan,
            CancellationToken.None);

        return new ToolResult<string>(true, input);
    }
}
