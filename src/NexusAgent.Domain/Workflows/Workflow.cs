using NexusAgent.Domain.Common;

namespace NexusAgent.Domain.Workflows;

public sealed class Workflow
{
    private readonly List<WorkflowStep> _steps = [];

    private Workflow()
    {
    }

    private Workflow(
        Guid id,
        string requesterId,
        string tenantId,
        string request,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Workflow ID cannot be empty.",
                nameof(id));
        }

        RequesterId = RequireValue(requesterId, nameof(requesterId));
        TenantId = RequireValue(tenantId, nameof(tenantId));
        Request = RequireValue(request, nameof(request));

        Id = id;
        Status = WorkflowStatus.Created;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public string RequesterId { get; private set; } = string.Empty;

    public string TenantId { get; private set; } = string.Empty;

    public string Request { get; private set; } = string.Empty;

    public WorkflowStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public string? FailureReason { get; private set; }

    public IReadOnlyCollection<WorkflowStep> Steps => _steps.AsReadOnly();

    public static Workflow Create(
        Guid id,
        string requesterId,
        string tenantId,
        string request,
        DateTimeOffset now)
    {
        return new Workflow(
            id,
            requesterId,
            tenantId,
            request,
            now);
    }

    public void Start(DateTimeOffset now)
    {
        RequireStatus(WorkflowStatus.Created);

        Status = WorkflowStatus.Running;
        Touch(now);
    }

    public WorkflowStep StartStep(
        string name,
        DateTimeOffset now)
    {
        RequireStatus(WorkflowStatus.Running);

        if (_steps.Any(step => !step.IsCompleted))
        {
            throw new DomainException(
                "A workflow step is already active.");
        }

        var step = new WorkflowStep(
            Guid.NewGuid(),
            name,
            now);

        _steps.Add(step);
        Touch(now);

        return step;
    }

    public void CompleteStep(
        Guid stepId,
        string outcome,
        DateTimeOffset now)
    {
        RequireStatus(WorkflowStatus.Running);

        var step = _steps.SingleOrDefault(
            candidate => candidate.Id == stepId)
            ?? throw new DomainException(
                $"Workflow step '{stepId}' does not exist.");

        step.Complete(outcome, now);
        Touch(now);
    }

    public void WaitForApproval(DateTimeOffset now)
    {
        RequireStatus(WorkflowStatus.Running);
        EnsureNoActiveStep();

        Status = WorkflowStatus.WaitingForApproval;
        Touch(now);
    }

    public void ResumeAfterApproval(DateTimeOffset now)
    {
        RequireStatus(WorkflowStatus.WaitingForApproval);

        Status = WorkflowStatus.Running;
        Touch(now);
    }

    public void Complete(DateTimeOffset now)
    {
        RequireStatus(WorkflowStatus.Running);
        EnsureNoActiveStep();

        Status = WorkflowStatus.Completed;
        Touch(now);
    }

    public void Fail(
        string reason,
        DateTimeOffset now)
    {
        EnsureNotTerminal();

        FailureReason = RequireValue(reason, nameof(reason));
        Status = WorkflowStatus.Failed;
        Touch(now);
    }

    public void Cancel(DateTimeOffset now)
    {
        EnsureNotTerminal();

        Status = WorkflowStatus.Cancelled;
        Touch(now);
    }

    private void RequireStatus(WorkflowStatus expected)
    {
        if (Status != expected)
        {
            throw new DomainException(
                $"Workflow '{Id}' must be in state '{expected}' " +
                $"but is currently '{Status}'.");
        }
    }

    private void EnsureNoActiveStep()
    {
        if (_steps.Any(step => !step.IsCompleted))
        {
            throw new DomainException(
                "Workflow cannot transition while a step is active.");
        }
    }

    private void EnsureNotTerminal()
    {
        if (Status is WorkflowStatus.Completed
            or WorkflowStatus.Failed
            or WorkflowStatus.Cancelled)
        {
            throw new DomainException(
                $"Workflow '{Id}' is already terminal.");
        }
    }

    private void Touch(DateTimeOffset now)
    {
        if (now < UpdatedAtUtc)
        {
            throw new DomainException(
                "Workflow timestamp cannot move backwards.");
        }

        UpdatedAtUtc = now;
    }

    private static string RequireValue(
        string value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                $"{parameterName} is required.",
                parameterName);
        }

        return value.Trim();
    }
}