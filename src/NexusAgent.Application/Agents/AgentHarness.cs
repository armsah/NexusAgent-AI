using System.Diagnostics;
using NexusAgent.Application.Observability;

namespace NexusAgent.Application.Agents;

public sealed class AgentHarness(AgentRegistry registry)
{
    internal async Task<AgentResult> ExecuteAsync(
        string name,
        AgentRequest request,
        AgentExecutionLimits limits,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Input);

        if (request.ExecutionId == Guid.Empty ||
            request.WorkflowId == Guid.Empty)
            throw new ArgumentException("Invalid execution identity.");

        if (limits.MaxAttempts is < 1 or > 5 ||
            limits.Timeout <= TimeSpan.Zero ||
            limits.Timeout > TimeSpan.FromMinutes(5))
            throw new ArgumentException("Invalid execution limits.");

        var agent = registry.Resolve(name);

        using var activity =
            NexusAgentTelemetry.StartActivity("agent.execute");

        activity?.SetTag("agent.name", name);
        activity?.SetTag("agent.max_attempts", limits.MaxAttempts);
        activity?.SetTag("agent.timeout_ms", limits.Timeout.TotalMilliseconds);

        using var budget =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        budget.CancelAfter(limits.Timeout);

        for (var attempt = 1; ; attempt++)
        {
            budget.Token.ThrowIfCancellationRequested();

            activity?.SetTag("agent.attempt", attempt);

            AgentResult result;

            try
            {
                result = await agent.ExecuteAsync(request, budget.Token);
                budget.Token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (budget.IsCancellationRequested)
            {
                activity?.SetTag("agent.outcome",
                    cancellationToken.IsCancellationRequested
                        ? "cancelled" : "timeout");
                activity?.SetStatus(ActivityStatusCode.Error);
                throw;
            }
            catch (Exception)
            {
                activity?.SetTag("agent.outcome", "exception");
                activity?.SetStatus(ActivityStatusCode.Error);
                throw;
            }

            activity?.SetTag("agent.outcome",
                result.Succeeded ? "succeeded" : "failed");

            activity?.SetStatus(result.Succeeded
                ? ActivityStatusCode.Ok : ActivityStatusCode.Error);

            budget.Token.ThrowIfCancellationRequested();

            if (result.Succeeded || !result.Retryable ||
                attempt == limits.MaxAttempts)
                return result;
        }
    }
}