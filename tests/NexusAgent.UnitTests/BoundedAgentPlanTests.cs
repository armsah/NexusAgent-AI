using NexusAgent.Application.Agents;
using Xunit;

namespace NexusAgent.UnitTests;

public sealed class BoundedAgentPlanTests
{
    [Theory]
    [InlineData(0, "supervisor")]
    [InlineData(1, "policy")]
    [InlineData(2, "knowledge")]
    [InlineData(3, "verifier")]
    public void PlanPreservesGovernedOrder(int stage, string expected)
    {
        Assert.Equal(expected, BoundedAgentPlan.GetAgentName(stage));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(100)]
    public void PlanRejectsUnregisteredStages(int stage)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BoundedAgentPlan.GetAgentName(stage));
    }

    [Fact]
    public void InvocationIdentifiersAreStableAcrossRetries()
    {
        var execution = Guid.NewGuid();

        var first = BoundedAgentPlan.GetInvocationId(execution, 2);
        var retry = BoundedAgentPlan.GetInvocationId(execution, 2);

        Assert.Equal(first, retry);
        Assert.NotEqual(first,
            BoundedAgentPlan.GetInvocationId(execution, 3));
        Assert.NotEqual(first,
            BoundedAgentPlan.GetInvocationId(Guid.NewGuid(), 2));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(301)]
    public void InvalidTimeoutsAreRejected(int seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => BoundedAgentPlan.ValidateTimeout(
                TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void MaximumTimeoutIsAccepted()
    {
        BoundedAgentPlan.ValidateTimeout(TimeSpan.FromMinutes(5));
    }
}