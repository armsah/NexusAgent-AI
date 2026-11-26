using NexusAgent.Application.Workflows;

namespace NexusAgent.Application.Agents;

public sealed record DurableAgentExecution(
    Guid ExecutionId,
    Guid WorkflowId,
    string Status,
    int NextStage,
    int AttemptCount,
    DateTimeOffset DeadlineAt,
    string? FinalAnswer,
    string? FailureCode);

public sealed record AgentExecutionLease(
    Guid ExecutionId,
    Guid LeaseToken,
    int Stage,
    int Attempt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset DeadlineAt);

public sealed record AgentMemoryCheckpoint(
    int Stage,
    Guid InvocationId,
    string AgentName,
    bool Succeeded,
    string Output);

public interface IDurableAgentExecutionStore
{
    Task<DurableAgentExecution> CreateOrGetAsync(
        Guid executionId,
        Guid workflowId,
        WorkflowAccess caller,
        TimeSpan timeout,
        CancellationToken cancellationToken);

    Task<DurableAgentExecution?> GetAsync(
        Guid executionId,
        WorkflowAccess caller,
        CancellationToken cancellationToken);

    Task<AgentExecutionLease?> TryClaimAsync(
        Guid executionId,
        WorkflowAccess caller,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AgentMemoryCheckpoint>> ReadMemoryAsync(
        Guid executionId,
        WorkflowAccess caller,
        CancellationToken cancellationToken);

    Task<bool> CheckpointAsync(
        AgentExecutionLease lease,
        WorkflowAccess caller,
        AgentResult result,
        CancellationToken cancellationToken);

    Task<bool> ReleaseForRetryAsync(
        AgentExecutionLease lease,
        WorkflowAccess caller,
        CancellationToken cancellationToken);

    Task<bool> CancelAsync(
        Guid executionId,
        WorkflowAccess caller,
        CancellationToken cancellationToken);
}