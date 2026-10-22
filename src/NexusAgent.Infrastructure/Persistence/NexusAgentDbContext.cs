using Microsoft.EntityFrameworkCore;
using NexusAgent.Domain.Workflows;
using NexusAgent.Domain.Documents;

namespace NexusAgent.Infrastructure.Persistence;

public sealed class NexusAgentDbContext(
    DbContextOptions<NexusAgentDbContext> options)
    : DbContext(options)
{
    public DbSet<Workflow> Workflows => Set<Workflow>();

    public DbSet<KnowledgeDocument> KnowledgeDocuments =>
        Set<KnowledgeDocument>();

    public DbSet<DocumentVersion> DocumentVersions =>
        Set<DocumentVersion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(NexusAgentDbContext).Assembly);
    }
}
