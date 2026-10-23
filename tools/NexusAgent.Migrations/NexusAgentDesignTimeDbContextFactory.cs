using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using NexusAgent.Infrastructure.Persistence;

namespace NexusAgent.Migrations;

public sealed class NexusAgentDesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<NexusAgentDbContext>
{
    public NexusAgentDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable(
                "NEXUSAGENT_MIGRATIONS_CONNECTION")
            ?? throw new InvalidOperationException(
                "NEXUSAGENT_MIGRATIONS_CONNECTION is required.");

        var options = new DbContextOptionsBuilder<NexusAgentDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.UseVector())
            .Options;

        return new NexusAgentDbContext(options);
    }
}
