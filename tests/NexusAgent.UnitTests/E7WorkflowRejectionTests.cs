using NexusAgent.Domain.Common;
using NexusAgent.Domain.Workflows;
using Xunit;

namespace NexusAgent.UnitTests;

public sealed class E7WorkflowRejectionTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static Workflow Create() =>
        Workflow.Create(
            Guid.NewGuid(),
            "requester",
            "tenant",
            "Approval lifecycle acceptance",
            Start);

    private static Workflow Waiting()
    {
        var workflow = Create();
        workflow.Start(Start.AddSeconds(1));
        workflow.WaitForApproval(Start.AddSeconds(2));
        return workflow;
    }

    [Fact]
    public void WaitingWorkflowCanBeRejected()
    {
        var workflow = Waiting();
        var rejectedAt = Start.AddSeconds(3);

        workflow.Reject(rejectedAt);

        Assert.Equal(WorkflowStatus.Rejected, workflow.Status);
        Assert.Equal(rejectedAt, workflow.UpdatedAtUtc);
        Assert.Null(workflow.FailureReason);
    }

    [Fact]
    public void CreatedWorkflowCannotBeRejected()
    {
        var workflow = Create();

        Assert.Throws<DomainException>(
            () => workflow.Reject(Start.AddSeconds(1)));

        Assert.Equal(WorkflowStatus.Created, workflow.Status);
        Assert.Equal(Start, workflow.UpdatedAtUtc);
    }

    [Fact]
    public void RunningWorkflowCannotBeRejected()
    {
        var workflow = Create();
        workflow.Start(Start.AddSeconds(1));

        Assert.Throws<DomainException>(
            () => workflow.Reject(Start.AddSeconds(2)));

        Assert.Equal(WorkflowStatus.Running, workflow.Status);
    }

    [Fact]
    public void ApprovedWorkflowCannotBeRejectedWithoutNewApproval()
    {
        var workflow = Waiting();
        workflow.ResumeAfterApproval(Start.AddSeconds(3));

        Assert.Throws<DomainException>(
            () => workflow.Reject(Start.AddSeconds(4)));

        Assert.Equal(WorkflowStatus.Running, workflow.Status);
    }

    [Fact]
    public void RejectionCannotMoveTimestampBackwards()
    {
        var workflow = Waiting();
        var previous = workflow.UpdatedAtUtc;

        Assert.Throws<DomainException>(
            () => workflow.Reject(Start.AddSeconds(1)));

        Assert.Equal(
            WorkflowStatus.WaitingForApproval,
            workflow.Status);

        Assert.Equal(previous, workflow.UpdatedAtUtc);
    }

    [Theory]
    [InlineData("start")]
    [InlineData("resume")]
    [InlineData("complete")]
    [InlineData("fail")]
    [InlineData("cancel")]
    [InlineData("reject")]
    [InlineData("wait")]
    [InlineData("step")]
    public void RejectedWorkflowIsTerminal(string operation)
    {
        var workflow = Waiting();
        var rejectedAt = Start.AddSeconds(3);
        workflow.Reject(rejectedAt);

        var later = Start.AddSeconds(4);

        Action transition = operation switch
        {
            "start" => () => workflow.Start(later),
            "resume" => () => workflow.ResumeAfterApproval(later),
            "complete" => () => workflow.Complete(later),
            "fail" => () => workflow.Fail("failure", later),
            "cancel" => () => workflow.Cancel(later),
            "reject" => () => workflow.Reject(later),
            "wait" => () => workflow.WaitForApproval(later),
            "step" => () => workflow.StartStep("tool", later),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

        Assert.Throws<DomainException>(transition);

        Assert.Equal(WorkflowStatus.Rejected, workflow.Status);
        Assert.Equal(rejectedAt, workflow.UpdatedAtUtc);
        Assert.Empty(workflow.Steps);
        Assert.Null(workflow.FailureReason);
    }

    [Fact]
    public void ActiveStepPreventsWaitingForApproval()
    {
        var workflow = Create();
        workflow.Start(Start.AddSeconds(1));
        workflow.StartStep("active-step", Start.AddSeconds(2));

        Assert.Throws<DomainException>(
            () => workflow.WaitForApproval(Start.AddSeconds(3)));

        Assert.Equal(WorkflowStatus.Running, workflow.Status);
        Assert.Single(workflow.Steps);
    }

    [Fact]
    public void CompletedStepAllowsApprovalAndRejection()
    {
        var workflow = Create();
        workflow.Start(Start.AddSeconds(1));

        var step = workflow.StartStep(
            "completed-step", Start.AddSeconds(2));

        workflow.CompleteStep(
            step.Id, "success", Start.AddSeconds(3));

        workflow.WaitForApproval(Start.AddSeconds(4));
        workflow.Reject(Start.AddSeconds(5));

        Assert.Equal(WorkflowStatus.Rejected, workflow.Status);
        Assert.Single(workflow.Steps);
        Assert.True(step.IsCompleted);
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("failed")]
    [InlineData("cancelled")]
    public void OtherTerminalStatesCannotBecomeRejected(string terminal)
    {
        var workflow = Create();
        workflow.Start(Start.AddSeconds(1));

        switch (terminal)
        {
            case "completed":
                workflow.Complete(Start.AddSeconds(2));
                break;
            case "failed":
                workflow.Fail("failure", Start.AddSeconds(2));
                break;
            case "cancelled":
                workflow.Cancel(Start.AddSeconds(2));
                break;
        }

        var previous = workflow.Status;

        Assert.Throws<DomainException>(
            () => workflow.Reject(Start.AddSeconds(3)));

        Assert.Equal(previous, workflow.Status);
    }
}
