using System.Text.Json;
using NexusAgent.Application.Workflows;

namespace NexusAgent.Application.Agents;

public sealed class MultiAgentCoordinator(
    AuthorizedAgentExecutor executor)
{
    public async Task<MultiAgentResult> ExecuteAsync(
        AgentRequest request,
        WorkflowAccess caller,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Input);

        if (request.Input.Length > 2000)
            throw new ArgumentException("Question exceeds input limit.");

        if (timeout <= TimeSpan.Zero ||
            timeout > TimeSpan.FromMinutes(5))
            throw new ArgumentException("Invalid workflow timeout.");

        using var budget =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        budget.CancelAfter(timeout);

        var stages = new List<AgentStageResult>();
        var limits = new AgentExecutionLimits(timeout, 1);

        async Task<AgentResult> RunAsync(string name, string input)
        {
            budget.Token.ThrowIfCancellationRequested();

            var result = await executor.ExecuteGovernedAsync(
                name,
                request with
                {
                    ExecutionId = Guid.NewGuid(),
                    Input = input
                },
                caller,
                limits,
                budget.Token);

            budget.Token.ThrowIfCancellationRequested();

            stages.Add(new AgentStageResult(
                name, result.Succeeded));

            return result;
        }

        MultiAgentResult Reject() =>
            new(false, "Execution rejected.", stages.ToArray());

        var supervisor = await RunAsync(
            AgentNames.Supervisor, request.Input);

        if (!supervisor.Succeeded)
            return Reject();

        var policy = await RunAsync(
            AgentNames.Policy, request.Input);

        if (!policy.Succeeded)
            return Reject();

        var knowledge = await RunAsync(
            AgentNames.Knowledge, request.Input);

        if (!knowledge.Succeeded ||
            string.IsNullOrWhiteSpace(knowledge.Output) ||
            knowledge.Output.Length > 32000)
            return Reject();

        KnowledgeHandoff? handoff;

        try
        {
            handoff = JsonSerializer.Deserialize<KnowledgeHandoff>(
                knowledge.Output);
        }
        catch (JsonException)
        {
            return Reject();
        }

        if (handoff is null ||
            !string.Equals(
                handoff.Question,
                request.Input,
                StringComparison.Ordinal))
        {
            return Reject();
        }

        var verifier = await RunAsync(
            AgentNames.Verifier, knowledge.Output);

        if (!verifier.Succeeded ||
            string.IsNullOrWhiteSpace(verifier.Output) ||
            verifier.Output.Length > 8000)
            return Reject();

        budget.Token.ThrowIfCancellationRequested();

        return new MultiAgentResult(
            true, verifier.Output, stages.ToArray());
    }
}