namespace NexusAgent.Application.Agents;

public static class AgentNames
{
    public const string Supervisor = "supervisor";
    public const string Knowledge = "knowledge";
    public const string Policy = "policy";
    public const string Verifier = "verifier";
}

public sealed record AgentStageResult(
    string AgentName,
    bool Succeeded);

public sealed record MultiAgentResult(
    bool Succeeded,
    string Answer,
    IReadOnlyList<AgentStageResult> Stages);