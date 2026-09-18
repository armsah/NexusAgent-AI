using Microsoft.EntityFrameworkCore;
using NexusAgent.Domain.Workflows;

namespace NexusAgent.Infrastructure.Persistence;

public sealed class NexusAgentDbContext(
    DbContextOptions<NexusAgentDbContext> options)
    : DbContext(options)
{
    public DbSet<Workflow> Workflows => Set<Workflow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(NexusAgentDbContext).Assembly);
    }
}