using NexusAgent.Application.Abstractions;
using NexusAgent.Application.Workflows;

namespace NexusAgent.Application.Agents;

public sealed class AuthorizedAgentExecutor(
    AgentHarness harness,
    IWorkflowRepository repository,
    IAgentAuthorizationPolicy authorization)
{
    public async Task<AgentResult> ExecuteAsync(
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
}