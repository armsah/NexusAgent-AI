namespace NexusAgent.Application.Agents;

public static class BoundedAgentPlan
{
    public const string Version = "v1";
    public const int StageCount = 4;
    public const int MaxAttemptsPerStage = 3;

    public static readonly TimeSpan MaximumDuration =
        TimeSpan.FromMinutes(5);

    public static readonly TimeSpan MaximumLease =
        TimeSpan.FromMinutes(3);

    public static string GetAgentName(int stage) => stage switch
    {
        0 => AgentNames.Supervisor,
        1 => AgentNames.Policy,
        2 => AgentNames.Knowledge,
        3 => AgentNames.Verifier,
        _ => throw new ArgumentOutOfRangeException(nameof(stage))
    };

    public static void ValidateTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero || timeout > MaximumDuration)
            throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    public static Guid GetInvocationId(Guid executionId, int stage)
    {
        if (executionId == Guid.Empty)
            throw new ArgumentException("Execution ID is required.");

        _ = GetAgentName(stage);

        var bytes = System.Text.Encoding.UTF8.GetBytes(
            $"{Version}:{executionId:D}:{stage}");

        var digest = System.Security.Cryptography.SHA256.HashData(bytes);

        return new Guid(digest.AsSpan(0, 16));
    }
}