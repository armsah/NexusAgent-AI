using NexusAgent.Application.Abstractions;
using NexusAgent.Application.Workflows;
using NexusAgent.Domain.Workflows;

namespace NexusAgent.UnitTests;

public sealed class WorkflowApplicationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateWorkflow_ShouldPersistAndReturnWorkflow()
    {
        var repository = new FakeWorkflowRepository();
        var id = Guid.NewGuid();

        var handler = new CreateWorkflowHandler(
            repository,
            new FakeClock(Now),
            new FakeIdGenerator(id));

        var result = await handler.HandleAsync(
            new CreateWorkflowCommand(
                "user-123",
                "tenant-abc",
                "Summarize the incident policy."));

        Assert.Equal(id, result.Id);
        Assert.Equal("user-123", result.RequesterId);
        Assert.Equal("tenant-abc", result.TenantId);
        Assert.Equal(
            "Summarize the incident policy.",
            result.Request);
        Assert.Equal(WorkflowStatus.Created, result.Status);

        Assert.Single(repository.Workflows);
        Assert.Equal(1, repository.SaveChangesCount);
    }

    [Fact]
    public async Task GetWorkflow_ShouldReturnExistingWorkflow()
    {
        var workflow = Workflow.Create(
            Guid.NewGuid(),
            "user-123",
            "tenant-abc",
            "Test request",
            Now);

        var repository = new FakeWorkflowRepository();
        repository.Workflows.Add(workflow);

        var handler = new GetWorkflowHandler(repository);

        var result = await handler.HandleAsync(workflow.Id);

        Assert.NotNull(result);
        Assert.Equal(workflow.Id, result.Id);
        Assert.Equal(WorkflowStatus.Created, result.Status);
    }

    [Fact]
    public async Task GetWorkflow_ShouldReturnNullWhenMissing()
    {
        var repository = new FakeWorkflowRepository();
        var handler = new GetWorkflowHandler(repository);

        var result = await handler.HandleAsync(
            Guid.NewGuid());

        Assert.Null(result);
    }

    private sealed class FakeClock(
        DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class FakeIdGenerator(
        Guid id) : IIdGenerator
    {
        public Guid NewId() => id;
    }

    private sealed class FakeWorkflowRepository
        : IWorkflowRepository
    {
        public List<Workflow> Workflows { get; } = [];

        public int SaveChangesCount { get; private set; }

        public Task AddAsync(
            Workflow workflow,
            CancellationToken cancellationToken = default)
        {
            Workflows.Add(workflow);

            return Task.CompletedTask;
        }

        public Task<Workflow?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                Workflows.SingleOrDefault(
                    workflow => workflow.Id == id));
        }

        public Task SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            SaveChangesCount++;

            return Task.CompletedTask;
        }
    }
}