using NexusAgent.Application.Questions;

namespace NexusAgent.UnitTests;

public sealed class ExtractiveGroundingVerifierTests
{
    private static GroundingEvidence[] Evidence() =>
        [
            new("S1", Guid.NewGuid(), "Architecture", 1, 1,
                "NexusAgent uses PostgreSQL."),
            new("S2", Guid.NewGuid(), "Policy", 1, 1,
                "NexusAgent does not use MongoDB.")
        ];

    [Theory]
    [InlineData("NexusAgent uses PostgreSQL [S1].", "S1", true)]
    [InlineData("NexusAgent uses PostgreSQL [S2].", "S2", false)]
    [InlineData("NexusAgent does not use PostgreSQL [S1].", "S1", false)]
    [InlineData("PostgreSQL uses NexusAgent [S1].", "S1", false)]
    [InlineData("NexusAgent uses MongoDB [S2].", "S2", false)]
    [InlineData("NexusAgent does not use MongoDB [S2].", "S2", true)]
    [InlineData("NexusAgent uses PostgreSQL.", "S1", false)]
    [InlineData("NexusAgent uses PostgreSQL [S9].", "S9", false)]
    public void EnforcesExactEvidence(
        string answer, string citation, bool expected)
    {
        var generated = new GroundedGenerationResult(
            answer, [citation]);

        Assert.Equal(expected,
            ExtractiveGroundingVerifier.IsSupported(
                generated, Evidence()));
    }
}