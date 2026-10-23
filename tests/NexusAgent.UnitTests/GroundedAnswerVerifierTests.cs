using NexusAgent.Application.Questions;

namespace NexusAgent.UnitTests;

public sealed class GroundedAnswerVerifierTests
{
    private static readonly GroundingEvidence[] Evidence =
    [
        new(
            "S1",
            Guid.NewGuid(),
            "Device Policy",
            1,
            1,
            "Employees must use approved devices.")
    ];

    [Fact]
    public void SupportedClaim_IsAccepted()
    {
        var result = new GroundedGenerationResult(
            "Employees must use approved devices [S1].",
            ["S1"]);

        Assert.True(GroundedAnswerVerifier.IsSupported(result, Evidence));
    }

    [Fact]
    public void UnsupportedClaim_IsRejected()
    {
        var result = new GroundedGenerationResult(
            "Employees may use personal devices [S1].",
            ["S1"]);

        Assert.False(GroundedAnswerVerifier.IsSupported(result, Evidence));
    }

    [Fact]
    public void InventedCitation_IsRejected()
    {
        var result = new GroundedGenerationResult(
            "Employees must use approved devices [S9].",
            ["S9"]);

        Assert.False(GroundedAnswerVerifier.IsSupported(result, Evidence));
    }

    [Fact]
    public void ClaimWithoutCitation_IsRejected()
    {
        var result = new GroundedGenerationResult(
            "Employees must use approved devices [S1]. Personal devices are allowed.",
            ["S1"]);

        Assert.False(GroundedAnswerVerifier.IsSupported(result, Evidence));
    }

    [Fact]
    public void UnsupportedGeneratedAnswer_CausesAbstention()
    {
        var decision = GroundedAnswerComposer.Compose(
            "What devices are allowed?",
            Evidence,
            new GroundedGenerationResult(
                "Personal devices are allowed [S1].",
                ["S1"]));

        Assert.True(decision.Abstained);
        Assert.Empty(decision.Citations);
    }
}