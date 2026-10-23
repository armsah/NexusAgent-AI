namespace NexusAgent.Infrastructure.Persistence.Search;

public sealed class DocumentSearchIndex
{
    public Guid DocumentId { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public int ContentVersion { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset IndexedAt { get; set; }
}