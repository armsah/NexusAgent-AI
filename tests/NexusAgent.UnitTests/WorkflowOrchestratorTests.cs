using NexusAgent.Application.Abstractions;
using NexusAgent.Application.Orchestration;
using NexusAgent.Domain.Workflows;

namespace NexusAgent.UnitTests;

public sealed class WorkflowOrchestratorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Execute_ShouldCompleteSuccessfulWorkflow()
    {
        var fixture = new Fixture();
        var executor = new FakeExecutor(
            new OrchestrationStepResult(true, "Accepted"));

        var result = await fixture.RunAsync(executor);

        Assert.NotNull(result);
        Assert.Equal(WorkflowStatus.Completed, result.Status);
        Assert.Single(result.Steps);
        Assert.Equal("Accepted", result.Steps.Single().Outcome);
        Assert.Equal(4, fixture.Repository.SaveCount);
    }

    [Fact]
    public async Task Execute_ShouldRetryRetryableFailure()
    {
        var fixture = new Fixture();
        var executor = new FakeExecutor(
            new OrchestrationStepResult(false, "Temporary", true),
            new OrchestrationStepResult(true, "Recovered"));

        var result = await fixture.RunAsync(executor);

        Assert.NotNull(result);
        Assert.Equal(WorkflowStatus.Completed, result.Status);
        Assert.Equal(2, executor.Attempts);
    }

    [Fact]
    public async Task Execute_ShouldPersistFailedStepOutcome()
    {
        var fixture = new Fixture();
        var executor = new FakeExecutor(
            new OrchestrationStepResult(false, "Rejected"));

        var result = await fixture.RunAsync(executor);

        Assert.NotNull(result);
        Assert.Equal(WorkflowStatus.Failed, result.Status);
        Assert.True(result.Steps.Single().CompletedAtUtc.HasValue);
        Assert.Contains("failed", result.Steps.Single().Outcome!,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Execute_ShouldFailAfterRetryLimit()
    {
        var fixture = new Fixture();
        var executor = new FakeExecutor(
            new OrchestrationStepResult(false, "Temporary", true),
            new OrchestrationStepResult(false, "Still failing", true));

        var result = await fixture.RunAsync(executor);

        Assert.NotNull(result);
        Assert.Equal(WorkflowStatus.Failed, result.Status);
        Assert.Equal(2, executor.Attempts);
    }

    [Fact]
    public async Task Execute_ShouldCancelAndCloseActiveStep()
    {
        var fixture = new Fixture();
        var executor = new CancellingExecutor();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.RunAsync(executor));

        Assert.Equal(
            WorkflowStatus.Cancelled,
            fixture.Workflow.Status);

        Assert.True(
            fixture.Workflow.Steps.Single().IsCompleted);
        Assert.Equal(3, fixture.Repository.SaveCount);
    }

    [Fact]
    public async Task Execute_ShouldFailOnUnexpectedException()
    {
        var fixture = new Fixture();
        var executor = new ThrowingExecutor();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.RunAsync(executor));

        Assert.Equal(WorkflowStatus.Failed, fixture.Workflow.Status);
        Assert.True(fixture.Workflow.Steps.Single().IsCompleted);
        Assert.Equal(3, fixture.Repository.SaveCount);
    }

    [Fact]
    public async Task Execute_ShouldNotInvokeExecutor_WhenInitialClaimConflicts()
    {
        var fixture = new Fixture();

        fixture.Repository.FailOnSaveWithConcurrency = true;

        var executor = new FakeExecutor(
            new OrchestrationStepResult(true, "Accepted"));

        await Assert.ThrowsAsync<
            NexusAgent.Application.Workflows.WorkflowConcurrencyException>(
            () => fixture.RunAsync(executor));

        Assert.Equal(0, executor.Attempts);
        Assert.Empty(fixture.Workflow.Steps);
        Assert.Equal(1, fixture.Repository.SaveCount);
    }
    private sealed class Fixture
    {
        public Workflow Workflow { get; } = Workflow.Create(
            Guid.NewGuid(),
            "user-123",
            "tenant-abc",
            "Test request",
            Now);

        public FakeRepository Repository { get; } = new();

        public async Task<NexusAgent.Application.Workflows.WorkflowDto?> RunAsync(
            IOrchestrationStepExecutor executor)
        {
            Repository.Workflow = Workflow;

            var orchestrator = new WorkflowOrchestrator(
                Repository,
                new FakeClock(),
                [executor]);

            return await orchestrator.ExecuteAsync(
                Workflow.Id,
                new NexusAgent.Application.Workflows.WorkflowAccess("tenant-abc", "user-123"),
                new OrchestrationPlan(
                    [new OrchestrationStep("Validate", "input")]),
                new OrchestrationLimits());
        }
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class FakeRepository : IWorkflowRepository
    {
        public Workflow? Workflow { get; set; }

        public int SaveCount { get; private set; }

        public bool FailOnSaveWithConcurrency { get; set; }

        public Task AddAsync(
            Workflow workflow,
            CancellationToken cancellationToken = default)
        {
            Workflow = workflow;
            return Task.CompletedTask;
        }

        public Task<Workflow?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                Workflow?.Id == id ? Workflow : null);
        }

        public Task SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            SaveCount++;

            if (FailOnSaveWithConcurrency)
            {
                throw new NexusAgent.Application.Workflows.WorkflowConcurrencyException(
                    new InvalidOperationException("Simulated competing worker."));
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeExecutor(
        params OrchestrationStepResult[] results)
        : IOrchestrationStepExecutor
    {
        public string StepName => "Validate";

        public int Attempts { get; private set; }

        public Task<OrchestrationStepResult> ExecuteAsync(
            string input,
            CancellationToken cancellationToken)
        {
            var index = Math.Min(Attempts, results.Length - 1);
            Attempts++;

            return Task.FromResult(results[index]);
        }
    }

    private sealed class CancellingExecutor : IOrchestrationStepExecutor
    {
        public string StepName => "Validate";

        public Task<OrchestrationStepResult> ExecuteAsync(
            string input,
            CancellationToken cancellationToken)
        {
            throw new OperationCanceledException();
        }
    }

    private sealed class ThrowingExecutor : IOrchestrationStepExecutor
    {
        public string StepName => "Validate";

        public Task<OrchestrationStepResult> ExecuteAsync(
            string input,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Simulated failure");
        }
    }
}