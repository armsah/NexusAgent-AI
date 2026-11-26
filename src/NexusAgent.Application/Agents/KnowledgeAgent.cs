using System.Text.Json;
using NexusAgent.Application.Questions;

namespace NexusAgent.Application.Agents;

internal sealed class KnowledgeAgent(
    IAuthorizedEvidenceRetriever retriever,
    IServiceProvider services) : IAgent
{
    public string Name => AgentNames.Knowledge;

    public async Task<AgentResult> ExecuteAsync(
        AgentRequest request,
        CancellationToken cancellationToken)
    {
        request.RequireAuthorization(Name);
        if (string.IsNullOrWhiteSpace(request.Input) ||
            request.Input.Length > 2000 ||
            PromptInjectionDefense.IsSuspicious(request.Input))
        {
            return new AgentResult(false, "Invalid knowledge request.");
        }

        var evidence = await retriever.RetrieveAsync(
            request.WorkflowId,
            request.TenantId,
            request.Input,
            cancellationToken);

        var safeEvidence = PromptInjectionDefense
            .FilterEvidence(evidence)
            .Take(5)
            .ToArray();

        GroundedGenerationResult? generated = null;

        if (safeEvidence.Length > 0)
        {
            var generator = services.GetService(
                typeof(IGroundedAnswerGenerator))
                as IGroundedAnswerGenerator;

            if (generator is null)
                return new AgentResult(false, "Generator unavailable.");

            generated = await generator.GenerateAsync(
                request.Input,
                safeEvidence,
                cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var handoff = new KnowledgeHandoff(
            request.Input, safeEvidence, generated);

        return new AgentResult(
            true, JsonSerializer.Serialize(handoff));
    }
}