using NexusAgent.Application.Abstractions;
using NexusAgent.Application.Orchestration;
using NexusAgent.Application.Workflows;
using NexusAgent.Domain.Workflows;

namespace NexusAgent.UnitTests;

public sealed class WorkflowAuthorizationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("other-tenant", "user-123")]
    [InlineData("tenant-abc", "other-user")]
    public async Task Get_ShouldDenyUnauthorizedIdentity(
        string tenantId,
        string requesterId)
    {
        var repository = new FakeRepository(CreateWorkflow());
        var handler = new GetWorkflowHandler(repository);

        var result = await handler.HandleAsync(
            repository.Workflow.Id,
            new WorkflowAccess(tenantId, requesterId));

        Assert.Null(result);
        Assert.Equal(0, repository.SaveCount);
    }

    [Theory]
    [InlineData("other-tenant", "user-123")]
    [InlineData("tenant-abc", "other-user")]
    public async Task Start_ShouldDenyUnauthorizedIdentity(
        string tenantId,
        string requesterId)
    {
        var workflow = CreateWorkflow();
        var repository = new FakeRepository(workflow);

        var handler = new StartWorkflowHandler(
            repository,
            new FakeClock());

        var result = await handler.HandleAsync(
            workflow.Id,
            new WorkflowAccess(tenantId, requesterId));

        Assert.Null(result);
        Assert.Equal(WorkflowStatus.Created, workflow.Status);
        Assert.Equal(0, repository.SaveCount);
    }

    [Theory]
    [InlineData("other-tenant", "user-123")]
    [InlineData("tenant-abc", "other-user")]
    public async Task Complete_ShouldDenyUnauthorizedIdentity(
        string tenantId,
        string requesterId)
    {
        var workflow = CreateWorkflow();
        workflow.Start(Now);

        var repository = new FakeRepository(workflow);

        var handler = new CompleteWorkflowHandler(
            repository,
            new FakeClock());

        var result = await handler.HandleAsync(
            workflow.Id,
            new WorkflowAccess(tenantId, requesterId));

        Assert.Null(result);
        Assert.Equal(WorkflowStatus.Running, workflow.Status);
        Assert.Equal(0, repository.SaveCount);
    }

    [Theory]
    [InlineData("other-tenant", "user-123")]
    [InlineData("tenant-abc", "other-user")]
    public async Task Orchestrator_ShouldDenyBeforeExecutingSteps(
        string tenantId,
        string requesterId)
    {
        var workflow = CreateWorkflow();
        var repository = new FakeRepository(workflow);
        var executor = new CountingExecutor();

        var orchestrator = new WorkflowOrchestrator(
            repository,
            new FakeClock(),
            [executor]);

        var result = await orchestrator.ExecuteAsync(
            workflow.Id,
            new WorkflowAccess(tenantId, requesterId),
            new OrchestrationPlan(
                [new OrchestrationStep("Validate", "input")]),
            new OrchestrationLimits());

        Assert.Null(result);
        Assert.Equal(0, executor.CallCount);
        Assert.Equal(0, repository.SaveCount);
        Assert.Equal(WorkflowStatus.Created, workflow.Status);
        Assert.Empty(workflow.Steps);
    }

    private static Workflow CreateWorkflow() =>
        Workflow.Create(
            Guid.NewGuid(),
            "user-123",
            "tenant-abc",
            "Test request",
            Now);

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class CountingExecutor : IOrchestrationStepExecutor
    {
        public string StepName => "Validate";

        public int CallCount { get; private set; }

        public Task<OrchestrationStepResult> ExecuteAsync(
            string input,
            CancellationToken cancellationToken)
        {
            CallCount++;

            return Task.FromResult(
                new OrchestrationStepResult(true, "Accepted"));
        }
    }

    private sealed class FakeRepository(Workflow workflow)
        : IWorkflowRepository
    {
        public Workflow Workflow { get; } = workflow;

        public int SaveCount { get; private set; }

        public Task AddAsync(
            Workflow workflow,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Workflow?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Workflow?>(
                id == Workflow.Id ? Workflow : null);
        }

        public Task SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }
}