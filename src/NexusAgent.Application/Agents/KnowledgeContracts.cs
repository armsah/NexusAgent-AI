using NexusAgent.Application.Questions;

namespace NexusAgent.Application.Agents;

public interface IAuthorizedEvidenceRetriever
{
    Task<IReadOnlyList<GroundingEvidence>> RetrieveAsync(
        Guid workflowId,
        string tenantId,
        string question,
        CancellationToken cancellationToken);
}

internal sealed record KnowledgeHandoff(
    string Question,
    GroundingEvidence[] Evidence,
    GroundedGenerationResult? Generated);