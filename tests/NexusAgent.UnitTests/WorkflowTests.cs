using NexusAgent.Domain.Common;
using NexusAgent.Domain.Workflows;

namespace NexusAgent.UnitTests;

public sealed class WorkflowTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_ShouldCreateWorkflowInCreatedState()
    {
        var id = Guid.NewGuid();

        var workflow = Workflow.Create(
            id,
            "user-123",
            "tenant-abc",
            "Summarize the incident policy.",
            Now);

        Assert.Equal(id, workflow.Id);
        Assert.Equal("user-123", workflow.RequesterId);
        Assert.Equal("tenant-abc", workflow.TenantId);
        Assert.Equal(
            "Summarize the incident policy.",
            workflow.Request);
        Assert.Equal(WorkflowStatus.Created, workflow.Status);
        Assert.Empty(workflow.Steps);
        Assert.Null(workflow.FailureReason);
    }

    [Fact]
    public void Workflow_ShouldFollowHappyPath()
    {
        var workflow = CreateWorkflow();

        workflow.Start(Now.AddSeconds(1));

        var step = workflow.StartStep(
            "AcceptRequest",
            Now.AddSeconds(2));

        workflow.CompleteStep(
            step.Id,
            "Accepted",
            Now.AddSeconds(3));

        workflow.Complete(Now.AddSeconds(4));

        Assert.Equal(
            WorkflowStatus.Completed,
            workflow.Status);

        Assert.Single(workflow.Steps);
        Assert.True(step.IsCompleted);
        Assert.Equal("Accepted", step.Outcome);
    }

    [Fact]
    public void Complete_ShouldRejectActiveStep()
    {
        var workflow = CreateWorkflow();

        workflow.Start(Now.AddSeconds(1));

        workflow.StartStep(
            "AcceptRequest",
            Now.AddSeconds(2));

        Assert.Throws<DomainException>(
            () => workflow.Complete(
                Now.AddSeconds(3)));
    }

    [Fact]
    public void ApprovalWorkflow_ShouldRequireWaitingStateBeforeResume()
    {
        var workflow = CreateWorkflow();

        Assert.Throws<DomainException>(
            () => workflow.ResumeAfterApproval(
                Now.AddSeconds(1)));

        workflow.Start(Now.AddSeconds(1));
        workflow.WaitForApproval(Now.AddSeconds(2));

        Assert.Equal(
            WorkflowStatus.WaitingForApproval,
            workflow.Status);

        workflow.ResumeAfterApproval(
            Now.AddSeconds(3));

        Assert.Equal(
            WorkflowStatus.Running,
            workflow.Status);
    }

    [Fact]
    public void CompletedWorkflow_ShouldRejectFurtherTransitions()
    {
        var workflow = CreateWorkflow();

        workflow.Start(Now.AddSeconds(1));
        workflow.Complete(Now.AddSeconds(2));

        Assert.Throws<DomainException>(
            () => workflow.Cancel(
                Now.AddSeconds(3)));

        Assert.Throws<DomainException>(
            () => workflow.Fail(
                "Unexpected failure.",
                Now.AddSeconds(3)));
    }

    [Fact]
    public void StartStep_ShouldRejectConcurrentActiveStep()
    {
        var workflow = CreateWorkflow();

        workflow.Start(Now.AddSeconds(1));

        workflow.StartStep(
            "FirstStep",
            Now.AddSeconds(2));

        Assert.Throws<DomainException>(
            () => workflow.StartStep(
                "SecondStep",
                Now.AddSeconds(3)));
    }

    private static Workflow CreateWorkflow()
    {
        return Workflow.Create(
            Guid.NewGuid(),
            "user-123",
            "tenant-abc",
            "Test request",
            Now);
    }
}