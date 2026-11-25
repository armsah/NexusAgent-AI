using NexusAgent.Application.Workflows;

namespace NexusAgent.Application.Agents;

public interface IAgentAuthorizationPolicy
{
    bool IsAuthorized(WorkflowAccess caller, string agentName);
}

public sealed class DenyAllAgentAuthorizationPolicy
    : IAgentAuthorizationPolicy
{
    public bool IsAuthorized(WorkflowAccess caller, string agentName)
        => false;
}