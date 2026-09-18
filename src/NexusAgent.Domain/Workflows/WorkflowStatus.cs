namespace NexusAgent.Domain.Workflows;

public enum WorkflowStatus
{
    Created = 0,
    Running = 1,
    WaitingForApproval = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5
}