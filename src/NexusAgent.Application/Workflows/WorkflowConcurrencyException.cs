namespace NexusAgent.Application.Workflows;

public sealed class WorkflowConcurrencyException : Exception
{
    public WorkflowConcurrencyException(Exception innerException)
        : base(
            "Workflow state changed concurrently. Execution was not claimed.",
            innerException)
    {
    }
}