using Microsoft.EntityFrameworkCore;
using NexusAgent.Domain.Workflows;
using NexusAgent.Infrastructure.Persistence;

namespace NexusAgent.IntegrationTests;

public sealed class WorkflowConcurrencyTests
{
    private const string ConnectionString =
        "Host=127.0.0.1;Port=15432;Database=nexusagent;Username=nexusagent;Password=nexusagent_dev_only";

    [Fact]
    public async Task CompetingWorkers_OnlyFirstCanClaimWorkflow()
    {
        var options = new DbContextOptionsBuilder<NexusAgentDbContext>()
            .UseNpgsql(ConnectionString, npgsql => npgsql.UseVector())
            .Options;

        var workflowId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;

        await using (var setup = new NexusAgentDbContext(options))
        {
            await setup.Database.MigrateAsync();

            setup.Workflows.Add(Workflow.Create(
                workflowId,
                "tenant-concurrency",
                "requester-concurrency",
                "Verify exclusive workflow execution.",
                createdAt));

            await setup.SaveChangesAsync();
        }

        await using var firstWorker = new NexusAgentDbContext(options);
        await using var secondWorker = new NexusAgentDbContext(options);

        // Both workers observe the same initial database version.
        var firstWorkflow = await firstWorker.Workflows
            .SingleAsync(workflow => workflow.Id == workflowId);

        var secondWorkflow = await secondWorker.Workflows
            .SingleAsync(workflow => workflow.Id == workflowId);

        Assert.Equal(WorkflowStatus.Created, firstWorkflow.Status);
        Assert.Equal(WorkflowStatus.Created, secondWorkflow.Status);

        // The first worker claims execution.
        firstWorkflow.Start(createdAt.AddSeconds(1));
        await firstWorker.SaveChangesAsync();

        // The competing worker attempts to claim its stale copy.
        secondWorkflow.Start(createdAt.AddSeconds(2));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => secondWorker.SaveChangesAsync());

        // The winner's transition must remain intact.
        await using var verification = new NexusAgentDbContext(options);

        var persisted = await verification.Workflows
            .SingleAsync(workflow => workflow.Id == workflowId);

        Assert.Equal(WorkflowStatus.Running, persisted.Status);
        var expectedUpdatedAt = createdAt.AddSeconds(1);
        var timestampDifference =
            (persisted.UpdatedAtUtc - expectedUpdatedAt).Duration();

        Assert.True(
            timestampDifference < TimeSpan.FromMicroseconds(1),
            $"Unexpected timestamp difference: {timestampDifference}.");
    }
}