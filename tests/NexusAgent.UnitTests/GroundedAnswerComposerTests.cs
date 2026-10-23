using NexusAgent.Application.Questions;

namespace NexusAgent.UnitTests;

public sealed class GroundedAnswerComposerTests
{
    private static readonly GroundingEvidence[] Evidence =
    [
        new(
            "S1",
            Guid.NewGuid(),
            "Security Policy",
            2,
            3,
            "Employees must use approved devices.")
    ];

    [Fact]
    public void NoEvidence_Abstains()
    {
        var result = GroundedAnswerComposer.Compose(
            "What devices are allowed?",
            []);

        Assert.True(result.Abstained);
        Assert.Empty(result.Citations);
    }

    [Fact]
    public void ExtractiveAnswer_PreservesCitation()
    {
        var result = GroundedAnswerComposer.Compose(
            "What devices are allowed?",
            Evidence);

        Assert.False(result.Abstained);
        Assert.Contains("[S1]", result.Answer);
        Assert.Single(result.Citations);
        Assert.Equal(3, result.Citations[0].ContentVersion);
    }

    [Fact]
    public void ValidGeneratedAnswer_UsesAuthorizedCitation()
    {
        var result = GroundedAnswerComposer.Compose(
            "What devices are allowed?",
            Evidence,
            new GroundedGenerationResult(
                "Approved devices are required [S1].",
                ["S1"]));

        Assert.False(result.Abstained);
        Assert.Single(result.Citations);
    }

    [Fact]
    public void InventedCitation_Abstains()
    {
        var result = GroundedAnswerComposer.Compose(
            "What devices are allowed?",
            Evidence,
            new GroundedGenerationResult(
                "Anything is allowed [S9].",
                ["S9"]));

        Assert.True(result.Abstained);
        Assert.Empty(result.Citations);
    }
}