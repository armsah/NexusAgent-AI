using Microsoft.Extensions.DependencyInjection;
using NexusAgent.Application.Agents;
using NexusAgent.Application.Workflows;

namespace NexusAgent.UnitTests;

public sealed class GovernedInvocationBoundaryTests
{
    [Theory]
    [InlineData(AgentNames.Supervisor)]
    [InlineData(AgentNames.Policy)]
    [InlineData(AgentNames.Knowledge)]
    [InlineData(AgentNames.Verifier)]
    public async Task BuiltInAgentsRejectDirectExecution(string name)
    {
        var executor = new AuthorizedAgentExecutor(
            null!, null!, null!);

        var request = new AgentRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "tenant",
            "Database question");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            async () => await executor.ExecuteAsync(
                name,
                request,
                new WorkflowAccess("tenant", "owner"),
                new AgentExecutionLimits(TimeSpan.FromSeconds(10)),
                default));
    }
}