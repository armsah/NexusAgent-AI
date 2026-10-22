using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexusAgent.Application.Documents;
using NexusAgent.Infrastructure;
using NexusAgent.Infrastructure.Storage;

namespace NexusAgent.IntegrationTests;

public sealed class DocumentStorageDependencyInjectionTests
{
    [Fact]
    public void AddInfrastructure_RegistersLocalDocumentContentStore()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "nexusagent-di-tests",
            Guid.NewGuid().ToString("N"));

        var configuration = CreateConfiguration(root);
        var services = new ServiceCollection();

        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();

        var store = provider.GetRequiredService<IDocumentContentStore>();

        Assert.IsType<LocalDocumentContentStore>(store);
    }

    [Fact]
    public void AddInfrastructure_RejectsMissingStoragePath()
    {
        var configuration = CreateConfiguration(null);
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.AddInfrastructure(configuration));

        Assert.Contains("DocumentStorage:RootPath", exception.Message);
    }

    [Fact]
    public void AddInfrastructure_RejectsRelativeStoragePath()
    {
        var configuration = CreateConfiguration("relative/storage");
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.AddInfrastructure(configuration));

        Assert.Contains("absolute path", exception.Message);
    }

    private static IConfiguration CreateConfiguration(string? storageRoot)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:NexusAgent"] =
                    "Host=localhost;Database=nexusagent;Username=test;Password=test",
                ["DocumentStorage:RootPath"] = storageRoot
            })
            .Build();
    }
}
