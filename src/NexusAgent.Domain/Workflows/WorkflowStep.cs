namespace NexusAgent.Domain.Workflows;

public sealed class WorkflowStep
{
    private WorkflowStep()
    {
    }

    internal WorkflowStep(
        Guid id,
        string name,
        DateTimeOffset startedAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Workflow step ID cannot be empty.",
                nameof(id));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Workflow step name is required.",
                nameof(name));
        }

        Id = id;
        Name = name.Trim();
        StartedAtUtc = startedAtUtc;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public DateTimeOffset StartedAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public string? Outcome { get; private set; }

    public bool IsCompleted => CompletedAtUtc.HasValue;

    internal void Complete(
        string outcome,
        DateTimeOffset completedAtUtc)
    {
        if (IsCompleted)
        {
            throw new InvalidOperationException(
                "Workflow step has already been completed.");
        }

        if (string.IsNullOrWhiteSpace(outcome))
        {
            throw new ArgumentException(
                "Workflow step outcome is required.",
                nameof(outcome));
        }

        if (completedAtUtc < StartedAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(completedAtUtc),
                "Completion time cannot precede start time.");
        }

        Outcome = outcome.Trim();
        CompletedAtUtc = completedAtUtc;
    }
}