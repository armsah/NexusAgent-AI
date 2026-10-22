using NexusAgent.Domain.Common;

namespace NexusAgent.Domain.Documents;

public sealed class KnowledgeDocument
{
    private KnowledgeDocument() { }

    public Guid Id { get; private set; }
    public string TenantId { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Classification { get; private set; } = string.Empty;
    public string OwnerId { get; private set; } = string.Empty;
    public string[] AllowedPrincipalIds { get; private set; } = [];
    public int Version { get; private set; }
    public DocumentStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static KnowledgeDocument Create(
        Guid id,
        string tenantId,
        string title,
        string classification,
        string ownerId,
        IEnumerable<string> allowedPrincipalIds,
        DateTimeOffset now)
    {
        if (id == Guid.Empty)
            throw new DomainException("Document ID is required.");

        ValidateRequired(tenantId, nameof(tenantId));
        ValidateRequired(title, nameof(title));
        ValidateRequired(classification, nameof(classification));
        ValidateRequired(ownerId, nameof(ownerId));

        var principals = NormalizePrincipals(allowedPrincipalIds);

        if (principals.Length == 0)
            throw new DomainException("At least one allowed principal is required.");

        return new KnowledgeDocument
        {
            Id = id,
            TenantId = tenantId.Trim(),
            Title = title.Trim(),
            Classification = classification.Trim(),
            OwnerId = ownerId.Trim(),
            AllowedPrincipalIds = principals,
            Version = 1,
            Status = DocumentStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Update(
        string tenantId,
        string title,
        string classification,
        IEnumerable<string> allowedPrincipalIds,
        DateTimeOffset now)
    {
        EnsureTenant(tenantId);
        EnsureActive();

        ValidateRequired(title, nameof(title));
        ValidateRequired(classification, nameof(classification));

        var principals = NormalizePrincipals(allowedPrincipalIds);

        if (principals.Length == 0)
            throw new DomainException("At least one allowed principal is required.");

        Title = title.Trim();
        Classification = classification.Trim();
        AllowedPrincipalIds = principals;
        Version++;
        UpdatedAt = now;
    }

    public void Delete(string tenantId, DateTimeOffset now)
    {
        EnsureTenant(tenantId);
        EnsureActive();

        Status = DocumentStatus.Deleted;
        Version++;
        UpdatedAt = now;
    }

    public bool IsAccessibleBy(string tenantId, string principalId)
    {
        return Status == DocumentStatus.Active
            && string.Equals(TenantId, tenantId, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(principalId)
            && AllowedPrincipalIds.Contains(
                principalId.Trim(),
                StringComparer.Ordinal);
    }

    private void EnsureTenant(string tenantId)
    {
        if (!string.Equals(TenantId, tenantId, StringComparison.Ordinal))
            throw new DomainException("Cross-tenant document mutation is forbidden.");
    }

    private void EnsureActive()
    {
        if (Status != DocumentStatus.Active)
            throw new DomainException("Deleted documents cannot be modified.");
    }

    private static void ValidateRequired(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException($"{field} is required.");
    }

    private static string[] NormalizePrincipals(
        IEnumerable<string> principalIds)
    {
        return (principalIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
    }
}
