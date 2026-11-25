namespace NexusAgent.Application.Agents;

public sealed record AgentRequest(
    Guid ExecutionId,
    Guid WorkflowId,
    string TenantId,
    string Input);

public sealed record AgentResult(
    bool Succeeded,
    string Output,
    bool Retryable = false);

public interface IAgent
{
    string Name { get; }

    Task<AgentResult> ExecuteAsync(
        AgentRequest request,
        CancellationToken cancellationToken);
}

public sealed record AgentExecutionLimits(
    TimeSpan Timeout,
    int MaxAttempts = 1);