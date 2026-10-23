using NexusAgent.Infrastructure.Persistence.Search;
using Microsoft.EntityFrameworkCore;
using NexusAgent.Domain.Workflows;
using NexusAgent.Domain.Documents;
using NexusAgent.Infrastructure.Persistence.Outbox;

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

    public DbSet<OutboxMessage> OutboxMessages =>
        Set<OutboxMessage>();

    public DbSet<PublishedDocumentEvent> PublishedDocumentEvents =>
        Set<PublishedDocumentEvent>();

    public DbSet<DocumentSearchIndex> DocumentSearchIndexes => Set<DocumentSearchIndex>();

    public DbSet<DocumentVectorIndex> DocumentVectorIndexes => Set<DocumentVectorIndex>();

    public DbSet<ProcessedDocumentIndexEvent> ProcessedDocumentIndexEvents => Set<ProcessedDocumentIndexEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(NexusAgentDbContext).Assembly);
    }
}
