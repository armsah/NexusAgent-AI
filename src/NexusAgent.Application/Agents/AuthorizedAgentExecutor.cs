using NexusAgent.Application.Abstractions;
using NexusAgent.Application.Workflows;

namespace NexusAgent.Application.Agents;

public sealed class AuthorizedAgentExecutor(
    AgentHarness harness,
    IWorkflowRepository repository,
    IAgentAuthorizationPolicy authorization)
{
    internal async Task<AgentResult> ExecuteGovernedAsync(
        string name,
        AgentRequest request,
        WorkflowAccess caller,
        AgentExecutionLimits limits,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(caller);

        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(caller.TenantId) ||
            string.IsNullOrWhiteSpace(caller.RequesterId) ||
            !string.Equals(request.TenantId, caller.TenantId,
                StringComparison.Ordinal) ||
            !authorization.IsAuthorized(caller, name))
            throw new UnauthorizedAccessException("Agent access denied.");

        var workflow = await repository.GetByIdAsync(
            request.WorkflowId, cancellationToken);

        if (workflow is null || !caller.CanAccess(workflow))
            throw new UnauthorizedAccessException("Agent access denied.");

        return await harness.ExecuteAsync(
            name, request, limits, cancellationToken);
    }

    public Task<AgentResult> ExecuteAsync(
        string agentName,
        AgentRequest request,
        WorkflowAccess caller,
        AgentExecutionLimits limits,
        CancellationToken cancellationToken = default)
    {
        if (agentName is AgentNames.Supervisor
            or AgentNames.Policy
            or AgentNames.Knowledge
            or AgentNames.Verifier)
        {
            throw new UnauthorizedAccessException(
                "Built-in agents require governed coordination.");
        }

        return ExecuteGovernedAsync(
            agentName, request, caller, limits, cancellationToken);
    }
}