using Microsoft.EntityFrameworkCore;
using NexusAgent.Domain.Workflows;
using NexusAgent.Infrastructure.Persistence;

namespace NexusAgent.IntegrationTests;

public sealed class WorkflowPersistenceTests
{
    private const string ConnectionString =
        "Host=127.0.0.1;Port=15432;Database=nexusagent;Username=nexusagent;Password=nexusagent_dev_only";

    [Fact]
    public async Task WorkflowLifecycle_IsPersistedToPostgreSql()
    {
        var options = new DbContextOptionsBuilder<NexusAgentDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        var workflowId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;

        await using (var dbContext = new NexusAgentDbContext(options))
        {
            await dbContext.Database.MigrateAsync();

            var workflow = Workflow.Create(
                workflowId,
                "integration-test",
                "integration-test",
                "Verify PostgreSQL workflow persistence.",
                createdAt);

            dbContext.Workflows.Add(workflow);
            await dbContext.SaveChangesAsync();

            workflow.Start(createdAt.AddSeconds(1));
            await dbContext.SaveChangesAsync();

            workflow.Complete(createdAt.AddSeconds(2));
            await dbContext.SaveChangesAsync();
        }

        await using (var verificationContext =
                     new NexusAgentDbContext(options))
        {
            var persisted = await verificationContext.Workflows
                .SingleAsync(workflow => workflow.Id == workflowId);

            Assert.Equal(
                WorkflowStatus.Completed,
                persisted.Status);

            Assert.Equal(
                "integration-test",
                persisted.RequesterId);

            Assert.Equal(
                "integration-test",
                persisted.TenantId);

            Assert.Equal(
                "Verify PostgreSQL workflow persistence.",
                persisted.Request);
        }
    }
}