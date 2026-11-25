using NexusAgent.Application.Abstractions;
using NexusAgent.Application.Agents;
using NexusAgent.Application.Workflows;
using NexusAgent.Domain.Workflows;
using Xunit;

namespace NexusAgent.UnitTests;

public sealed class AgentAuthorizationTests
{
    private sealed class ProbeAgent : IAgent
    {
        public string Name => "probe";
        public int Calls { get; private set; }

        public Task<AgentResult> ExecuteAsync(
            AgentRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new AgentResult(true, "Accepted"));
        }
    }

    private sealed class AllowPolicy : IAgentAuthorizationPolicy
    {
        public bool IsAuthorized(WorkflowAccess caller, string agentName)
            => agentName == "probe";
    }

    private sealed class FakeRepository(Workflow workflow)
        : IWorkflowRepository
    {
        public Task<Workflow?> GetByIdAsync(
            Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<Workflow?>(
                id == workflow.Id ? workflow : null);

        public Task AddAsync(
            Workflow workflow, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task SaveChangesAsync(
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    [Theory]
    [InlineData("tenant", "owner", "tenant", true, true, "probe", true)]
    [InlineData("other", "owner", "tenant", true, true, "probe", false)]
    [InlineData("other", "owner", "other", true, true, "probe", false)]
    [InlineData("tenant", "other", "tenant", true, true, "probe", false)]
    [InlineData("tenant", "owner", "tenant", false, true, "probe", false)]
    [InlineData("tenant", "owner", "tenant", true, false, "probe", false)]
    [InlineData("tenant", "owner", "tenant", true, true, "unknown", false)]
    public async Task ExecutionRequiresAuthorization(
        string callerTenant,
        string requester,
        string requestTenant,
        bool workflowExists,
        bool policyAllows,
        string agentName,
        bool expected)
    {
        var workflow = Workflow.Create(
            Guid.NewGuid(), "owner", "tenant",
            "Test request", DateTimeOffset.UtcNow);

        var agent = new ProbeAgent();

        IAgentAuthorizationPolicy policy = policyAllows
            ? new AllowPolicy()
            : new DenyAllAgentAuthorizationPolicy();

        var executor = new AuthorizedAgentExecutor(
            new AgentHarness(new AgentRegistry(new[] { agent })),
            new FakeRepository(workflow),
            policy);

        var request = new AgentRequest(
            Guid.NewGuid(),
            workflowExists ? workflow.Id : Guid.NewGuid(),
            requestTenant,
            "input");

        var caller = new WorkflowAccess(callerTenant, requester);
        var limits = new AgentExecutionLimits(TimeSpan.FromSeconds(5));

        if (expected)
        {
            var result = await executor.ExecuteAsync(
                agentName, request, caller, limits);

            Assert.True(result.Succeeded);
        }
        else
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                executor.ExecuteAsync(agentName, request, caller, limits));
        }

        Assert.Equal(expected ? 1 : 0, agent.Calls);
    }

    [Fact]
    public async Task CancelledRequestCannotExecuteAgent()
    {
        var workflow = Workflow.Create(
            Guid.NewGuid(), "owner", "tenant",
            "Test request", DateTimeOffset.UtcNow);

        var agent = new ProbeAgent();

        var executor = new AuthorizedAgentExecutor(
            new AgentHarness(new AgentRegistry(new[] { agent })),
            new FakeRepository(workflow),
            new AllowPolicy());

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            executor.ExecuteAsync(
                "probe",
                new AgentRequest(Guid.NewGuid(), workflow.Id, "tenant", "input"),
                new WorkflowAccess("tenant", "owner"),
                new AgentExecutionLimits(TimeSpan.FromSeconds(5)),
                cancellation.Token));

        Assert.Equal(0, agent.Calls);
    }
}