using NexusAgent.Domain.Common;

namespace NexusAgent.Domain.Documents;

public sealed class DocumentVersion
{
    private DocumentVersion() { }

    public Guid Id { get; private set; }
    public Guid DocumentId { get; private set; }
    public string TenantId { get; private set; } = string.Empty;
    public int Version { get; private set; }
    public int ContentVersion { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Classification { get; private set; } = string.Empty;
    public string OwnerId { get; private set; } = string.Empty;
    public string[] AllowedPrincipalIds { get; private set; } = [];
    public bool IsDeleted { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static DocumentVersion Capture(
        Guid id,
        KnowledgeDocument document)
    {
        if (id == Guid.Empty)
            throw new DomainException("Version ID is required.");

        ArgumentNullException.ThrowIfNull(document);

        return new DocumentVersion
        {
            Id = id,
            DocumentId = document.Id,
            TenantId = document.TenantId,
            Version = document.Version,
            ContentVersion = document.ContentVersion,
            Title = document.Title,
            Classification = document.Classification,
            OwnerId = document.OwnerId,
            AllowedPrincipalIds = [.. document.AllowedPrincipalIds],
            IsDeleted = document.Status == DocumentStatus.Deleted,
            CreatedAt = document.UpdatedAt
        };
    }
}
