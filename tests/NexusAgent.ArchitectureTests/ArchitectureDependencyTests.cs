using NexusAgent.Application.Workflows;
using NexusAgent.Domain.Workflows;
using NexusAgent.Infrastructure.Persistence;

namespace NexusAgent.ArchitectureTests;

public sealed class ArchitectureDependencyTests
{
    [Fact]
    public void Domain_DoesNotReference_OuterLayers()
    {
        var references = GetReferencedAssemblies(
            typeof(Workflow).Assembly);

        Assert.DoesNotContain(
            "NexusAgent.Application",
            references);

        Assert.DoesNotContain(
            "NexusAgent.Infrastructure",
            references);

        Assert.DoesNotContain(
            "NexusAgent.Api",
            references);

        Assert.DoesNotContain(
            "NexusAgent.Workers",
            references);
    }

    [Fact]
    public void Application_DoesNotReference_InfrastructureOrHosts()
    {
        var references = GetReferencedAssemblies(
            typeof(CreateWorkflowHandler).Assembly);

        Assert.DoesNotContain(
            "NexusAgent.Infrastructure",
            references);

        Assert.DoesNotContain(
            "NexusAgent.Api",
            references);

        Assert.DoesNotContain(
            "NexusAgent.Workers",
            references);
    }

    [Fact]
    public void Infrastructure_DoesNotReference_Hosts()
    {
        var references = GetReferencedAssemblies(
            typeof(NexusAgentDbContext).Assembly);

        Assert.DoesNotContain(
            "NexusAgent.Api",
            references);

        Assert.DoesNotContain(
            "NexusAgent.Workers",
            references);
    }

    private static HashSet<string> GetReferencedAssemblies(
        System.Reflection.Assembly assembly)
    {
        return assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null)
            .Select(name => name!)
            .ToHashSet(StringComparer.Ordinal);
    }
}