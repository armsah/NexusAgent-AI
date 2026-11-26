using NexusAgent.Application.Questions;

namespace NexusAgent.Application.Agents;

public sealed class PolicyAgent : IAgent
{
    public string Name => AgentNames.Policy;

    public Task<AgentResult> ExecuteAsync(
        AgentRequest request,
        CancellationToken cancellationToken)
    {
        request.RequireAuthorization(Name);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var accepted =
            !string.IsNullOrWhiteSpace(request.Input) &&
            request.Input.Length <= 2000 &&
            !PromptInjectionDefense.IsSuspicious(request.Input);

        return Task.FromResult(new AgentResult(
            accepted,
            accepted ? "Policy checks passed." : "Policy checks rejected."));
    }
}