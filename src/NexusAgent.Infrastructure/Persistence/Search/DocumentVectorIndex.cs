using Pgvector;

namespace NexusAgent.Infrastructure.Persistence.Search;

public sealed class DocumentVectorIndex
{
    public Guid DocumentId { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public int ContentVersion { get; set; }
    public string EmbeddingModel { get; set; } = string.Empty;
    public Vector Embedding { get; set; } = null!;
    public DateTimeOffset IndexedAt { get; set; }
}