using NexusAgent.Application.Agents;
using Xunit;

namespace NexusAgent.UnitTests;

public sealed class AgentSafeguardTests
{
    private sealed class ProbeAgent : IAgent
    {
        public string Name => "probe";
        public int Calls { get; private set; }
        public bool Wait { get; init; }

        public async Task<AgentResult> ExecuteAsync(
            AgentRequest request, CancellationToken cancellationToken)
        {
            Calls++;

            if (Wait)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

            return new AgentResult(false, "Rejected", false);
        }
    }

    private static AgentRequest Request() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "tenant", "input");

    [Fact]
    public async Task CancellationPreventsExecution()
    {
        var agent = new ProbeAgent();
        var harness = new AgentHarness(new AgentRegistry(new[] { agent }));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            harness.ExecuteAsync("probe", Request(),
                new(TimeSpan.FromSeconds(5)), cancellation.Token));

        Assert.Equal(0, agent.Calls);
    }

    [Fact]
    public async Task TimeoutCancelsExecution()
    {
        var agent = new ProbeAgent { Wait = true };
        var harness = new AgentHarness(new AgentRegistry(new[] { agent }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            harness.ExecuteAsync("probe", Request(),
                new(TimeSpan.FromMilliseconds(200))));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task InvalidBudgetRejected(int attempts)
    {
        var agent = new ProbeAgent();
        var harness = new AgentHarness(new AgentRegistry(new[] { agent }));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            harness.ExecuteAsync("probe", Request(),
                new(TimeSpan.FromSeconds(5), attempts)));

        Assert.Equal(0, agent.Calls);
    }

    [Fact]
    public async Task NonRetryableFailureStopsExecution()
    {
        var agent = new ProbeAgent();
        var harness = new AgentHarness(new AgentRegistry(new[] { agent }));

        var result = await harness.ExecuteAsync(
            "probe", Request(), new(TimeSpan.FromSeconds(5), 5));

        Assert.False(result.Succeeded);
        Assert.Equal(1, agent.Calls);
    }
}