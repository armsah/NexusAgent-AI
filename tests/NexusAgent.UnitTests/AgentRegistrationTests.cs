using Microsoft.Extensions.DependencyInjection;
using NexusAgent.Application;
using NexusAgent.Application.Agents;
using NexusAgent.Application.Workflows;

namespace NexusAgent.UnitTests;

public sealed class AgentRegistrationTests
{
    [Fact]
    public void DefaultPolicyDeniesExecution()
    {
        var services = new ServiceCollection();
        services.AddApplication();

        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true });

        using var scope = provider.CreateScope();

        var policy = scope.ServiceProvider
            .GetRequiredService<IAgentAuthorizationPolicy>();

        Assert.IsType<DenyAllAgentAuthorizationPolicy>(policy);

        Assert.False(policy.IsAuthorized(
            new WorkflowAccess("tenant", "owner"), "agent"));
    }

    [Fact]
    public void HarnessUsesScopedLifetime()
    {
        var services = new ServiceCollection();
        services.AddApplication();

        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true });

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var harness = first.ServiceProvider
            .GetRequiredService<AgentHarness>();

        Assert.Same(harness, first.ServiceProvider
            .GetRequiredService<AgentHarness>());

        Assert.NotSame(harness, second.ServiceProvider
            .GetRequiredService<AgentHarness>());
    }
}