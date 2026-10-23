namespace NexusAgent.Application.Questions;

public sealed record GroundingEvidence(
    string SourceId,
    Guid DocumentId,
    string Title,
    int DocumentVersion,
    int ContentVersion,
    string Excerpt);

public sealed record GroundedGenerationResult(
    string Answer,
    IReadOnlyList<string> CitedSourceIds);

public interface IGroundedAnswerGenerator
{
    Task<GroundedGenerationResult> GenerateAsync(
        string question,
        IReadOnlyList<GroundingEvidence> evidence,
        CancellationToken cancellationToken = default);
}