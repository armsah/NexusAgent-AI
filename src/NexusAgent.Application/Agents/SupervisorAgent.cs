namespace NexusAgent.Application.Agents;

public sealed class SupervisorAgent : IAgent
{
    public string Name => AgentNames.Supervisor;

    public Task<AgentResult> ExecuteAsync(
        AgentRequest request,
        CancellationToken cancellationToken)
    {
        request.RequireAuthorization(Name);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var accepted =
            request.ExecutionId != Guid.Empty &&
            request.WorkflowId != Guid.Empty &&
            !string.IsNullOrWhiteSpace(request.TenantId) &&
            !string.IsNullOrWhiteSpace(request.Input) &&
            request.Input.Length <= 2000;

        return Task.FromResult(new AgentResult(
            accepted,
            accepted ? "Request accepted." : "Request rejected."));
    }
}