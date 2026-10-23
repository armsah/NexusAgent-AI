using NexusAgent.Application.Questions;

namespace NexusAgent.UnitTests;

public sealed class GroundedAnswerValidationTests
{
    private static readonly GroundingEvidence[] Evidence =
    [
        new(
            "S1",
            Guid.NewGuid(),
            "Policy",
            2,
            3,
            "Employees must use approved devices."),
        new(
            "S2",
            Guid.NewGuid(),
            "Procedure",
            1,
            1,
            "Exceptions require written approval.")
    ];

    [Fact]
    public void AcceptsDeclaredCitationsPresentInAnswer()
    {
        var result = new GroundedGenerationResult(
            "Approved devices are required [S1].",
            ["S1"]);

        Assert.True(
            GroundedAnswerValidation.HasValidCitations(
                result,
                Evidence));
    }

    [Fact]
    public void RejectsInventedCitation()
    {
        var result = new GroundedGenerationResult(
            "An invented policy exists [S9].",
            ["S9"]);

        Assert.False(
            GroundedAnswerValidation.HasValidCitations(
                result,
                Evidence));
    }

    [Fact]
    public void RejectsMissingInlineCitation()
    {
        var result = new GroundedGenerationResult(
            "Approved devices are required.",
            ["S1"]);

        Assert.False(
            GroundedAnswerValidation.HasValidCitations(
                result,
                Evidence));
    }

    [Fact]
    public void RejectsUndeclaredInlineCitation()
    {
        var result = new GroundedGenerationResult(
            "Approved devices [S1] and exceptions [S2].",
            ["S1"]);

        Assert.False(
            GroundedAnswerValidation.HasValidCitations(
                result,
                Evidence));
    }

    [Fact]
    public void RejectsEmptyEvidence()
    {
        var result = new GroundedGenerationResult(
            "Approved devices are required [S1].",
            ["S1"]);

        Assert.False(
            GroundedAnswerValidation.HasValidCitations(
                result,
                []));
    }
}