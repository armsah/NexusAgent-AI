using Microsoft.EntityFrameworkCore;
using NexusAgent.Domain.Documents;
using NexusAgent.Infrastructure.Persistence;
using Xunit;

namespace NexusAgent.IntegrationTests;

public sealed class DocumentSearchSecurityTests
{
    private const string ConnectionString =
        "Host=127.0.0.1;Port=15432;Database=nexusagent;" +
        "Username=nexusagent;Password=nexusagent_dev_only";

    [Fact]
    public async Task SearchFiltersTenantAclAndDeletedDocuments()
    {
        var options = new DbContextOptionsBuilder<NexusAgentDbContext>()
            .UseNpgsql(ConnectionString, npgsql => npgsql.UseVector())
            .Options;

        await using var db = new NexusAgentDbContext(options);

        var suffix = Guid.NewGuid().ToString("N");
        var tenant = "search-tenant-" + suffix;
        var principal = "search-user-" + suffix;
        var keyword = "search" + suffix;
        var now = DateTimeOffset.UtcNow;

        var visible = KnowledgeDocument.Create(
            Guid.NewGuid(), tenant, keyword + " visible",
            "internal", principal, [principal], now);

        var forbidden = KnowledgeDocument.Create(
            Guid.NewGuid(), tenant, keyword + " forbidden",
            "internal", "other-owner", ["other-user"], now);

        var crossTenant = KnowledgeDocument.Create(
            Guid.NewGuid(), "other-" + tenant,
            keyword + " cross-tenant",
            "internal", principal, [principal], now);

        var deleted = KnowledgeDocument.Create(
            Guid.NewGuid(), tenant, keyword + " deleted",
            "internal", principal, [principal], now);

        deleted.Delete(tenant, now.AddSeconds(1));

        db.KnowledgeDocuments.AddRange(
            visible, forbidden, crossTenant, deleted);

        await db.SaveChangesAsync();

        try
        {
            var results = await db.KnowledgeDocuments
                .AsNoTracking()
                .Where(document =>
                    document.TenantId == tenant &&
                    document.Status == DocumentStatus.Active &&
                    document.AllowedPrincipalIds.Contains(principal) &&
                    EF.Functions.ToTsVector(
                        "english", document.Title)
                        .Matches(EF.Functions.PlainToTsQuery(
                            "english", keyword)))
                .Select(document => document.Id)
                .ToListAsync();

            Assert.Single(results);
            Assert.Equal(visible.Id, results[0]);
        }
        finally
        {
            await db.KnowledgeDocuments
                .Where(document =>
                    document.Id == visible.Id ||
                    document.Id == forbidden.Id ||
                    document.Id == crossTenant.Id ||
                    document.Id == deleted.Id)
                .ExecuteDeleteAsync();
        }
    }
}