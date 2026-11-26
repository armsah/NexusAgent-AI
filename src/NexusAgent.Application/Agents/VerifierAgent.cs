using System.Text.Json;
using NexusAgent.Application.Questions;

namespace NexusAgent.Application.Agents;

internal sealed class VerifierAgent(
    IAuthorizedEvidenceRetriever retriever) : IAgent
{
    public string Name => AgentNames.Verifier;

    public async Task<AgentResult> ExecuteAsync(
        AgentRequest request,
        CancellationToken cancellationToken)
    {
        request.RequireAuthorization(Name);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.Input) ||
            request.Input.Length > 32000)
            return new AgentResult(false, "Invalid handoff.");

        KnowledgeHandoff? handoff;

        try
        {
            handoff = JsonSerializer.Deserialize<KnowledgeHandoff>(
                request.Input);
        }
        catch (JsonException)
        {
            return new AgentResult(false, "Invalid handoff.");
        }

        if (handoff is null ||
            string.IsNullOrWhiteSpace(handoff.Question) ||
            handoff.Question.Length > 2000 ||
            PromptInjectionDefense.IsSuspicious(handoff.Question) ||
            handoff.Evidence is null ||
            handoff.Evidence.Length > 5 ||
            handoff.Evidence.Any(item => item is null) ||
            handoff.Evidence.Select(item => item.SourceId)
                .Distinct(StringComparer.Ordinal).Count()
                    != handoff.Evidence.Length)
        {
            return new AgentResult(false, "Invalid handoff.");
        }

        // Independently retrieve authorized evidence.
        // Never trust evidence supplied in a serialized handoff.
        var current = await retriever.RetrieveAsync(
            request.WorkflowId,
            request.TenantId,
            handoff.Question,
            cancellationToken);

        if (handoff.Evidence.Any(item => !current.Contains(item)))
            return new AgentResult(false, "Evidence verification failed.");

        if (handoff.Evidence.Length == 0 ||
            handoff.Generated is null ||
            handoff.Generated.CitedSourceIds is null ||
            PromptInjectionDefense.IsSuspicious(
                handoff.Generated.Answer))
        {
            return new AgentResult(false, "Insufficient verified evidence.");
        }

        if (!ExtractiveGroundingVerifier.IsSupported(
                handoff.Generated, handoff.Evidence))
        {
            return new AgentResult(false,
                "Answer is not supported by exact evidence.");
        }

        var decision = GroundedAnswerComposer.Compose(
            handoff.Question,
            handoff.Evidence,
            handoff.Generated);

        cancellationToken.ThrowIfCancellationRequested();

        return new AgentResult(
            !decision.Abstained,
            decision.Answer);
    }
}