using NexusAgent.Api.Identity;
using NexusAgent.Application.Agents;
using NexusAgent.Application.Workflows;

namespace NexusAgent.Api.Agents;

public sealed class DevelopmentAgentAuthorizationPolicy(
    DevelopmentRequestIdentity identity)
    : IAgentAuthorizationPolicy
{
    public bool IsAuthorized(
        WorkflowAccess caller, string agentName)
    {
        return caller.TenantId == identity.TenantId
            && caller.RequesterId == identity.RequesterId
            && agentName is AgentNames.Supervisor
                or AgentNames.Policy
                or AgentNames.Knowledge
                or AgentNames.Verifier;
    }
}