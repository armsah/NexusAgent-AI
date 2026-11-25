using NexusAgent.Application.Agents;
using Xunit;

namespace NexusAgent.UnitTests;

public sealed class AgentHarnessTests
{
    private sealed class TestAgent : IAgent
    {
        public string Name => "test";
        public int Calls { get; private set; }

        public Task<AgentResult> ExecuteAsync(
            AgentRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new AgentResult(
                Calls >= 2, "result", Calls < 2));
        }
    }

    [Fact]
    public void RegistryRejectsDuplicates()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new AgentRegistry(new[] { new TestAgent(), new TestAgent() }));
    }

    [Fact]
    public void RegistryRejectsUnknownAgent()
    {
        var registry = new AgentRegistry(new[] { new TestAgent() });
        Assert.Throws<KeyNotFoundException>(() => registry.Resolve("unknown"));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public async Task HarnessEnforcesAttemptBudget(int attempts, bool expected)
    {
        var agent = new TestAgent();
        var harness = new AgentHarness(new AgentRegistry(new[] { agent }));

        var result = await harness.ExecuteAsync(
            "test",
            new AgentRequest(Guid.NewGuid(), Guid.NewGuid(), "tenant", "input"),
            new AgentExecutionLimits(TimeSpan.FromSeconds(5), attempts));

        Assert.Equal(expected, result.Succeeded);
        Assert.Equal(attempts, agent.Calls);
    }
}