using System.Text.Json;
using NexusAgent.Application.Abstractions;
using NexusAgent.Application.Workflows;

namespace NexusAgent.Application.Agents;

public sealed class DurableMultiAgentCoordinator(
    AuthorizedAgentExecutor executor,
    IDurableAgentExecutionStore store,
    IWorkflowRepository workflows,
    IAgentAuthorizationPolicy authorization)
{
    public async Task<DurableAgentExecution> ExecuteAsync(
        AgentRequest request,
        WorkflowAccess caller,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(caller);

        BoundedAgentPlan.ValidateTimeout(timeout);

        using var requestBudget =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        requestBudget.CancelAfter(timeout);
        cancellationToken = requestBudget.Token;

        if (request.ExecutionId == Guid.Empty ||
            request.WorkflowId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.Input) ||
            request.Input.Length > 2000 ||
            !string.Equals(request.TenantId, caller.TenantId,
                StringComparison.Ordinal))
        {
            throw new ArgumentException("Invalid execution request.");
        }

        var workflow = await workflows.GetByIdAsync(
            request.WorkflowId, cancellationToken);

        if (workflow is null ||
            !caller.CanAccess(workflow) ||
            !string.Equals(workflow.Request, request.Input,
                StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException();
        }

        for (var stage = 0; stage < BoundedAgentPlan.StageCount; stage++)
        {
            if (!authorization.IsAuthorized(
                    caller, BoundedAgentPlan.GetAgentName(stage)))
                throw new UnauthorizedAccessException();
        }

        var state = await store.CreateOrGetAsync(
            request.ExecutionId,
            request.WorkflowId,
            caller,
            timeout,
            cancellationToken);

        while (state.Status is "Pending" or "Running")
        {
            cancellationToken.ThrowIfCancellationRequested();

            var lease = await store.TryClaimAsync(
                state.ExecutionId, caller, cancellationToken);

            if (lease is null)
                return (await ReloadAsync()) with { FinalAnswer = null };

            var remaining = lease.ExpiresAt - DateTimeOffset.UtcNow;

            if (remaining <= TimeSpan.Zero)
                return (await ReloadAsync()) with { FinalAnswer = null };

            using var budget =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);

            budget.CancelAfter(remaining);

            try
            {
                var memory = await store.ReadMemoryAsync(
                    state.ExecutionId, caller, budget.Token);

                var validMemory = memory.Count == lease.Stage;

                for (var i = 0; i < memory.Count && validMemory; i++)
                {
                    validMemory =
                        memory[i].Stage == i &&
                        memory[i].Succeeded &&
                        memory[i].AgentName ==
                            BoundedAgentPlan.GetAgentName(i) &&
                        memory[i].InvocationId ==
                            BoundedAgentPlan.GetInvocationId(
                                state.ExecutionId, i);
                }

                AgentResult result;

                if (!validMemory)
                {
                    result = new AgentResult(
                        false, "Invalid execution memory.");
                }
                else
                {
                    var input = lease.Stage == 3
                        ? memory[2].Output
                        : request.Input;

                    if (lease.Stage == 3 &&
                        !ValidHandoff(input, request.Input))
                    {
                        result = new AgentResult(
                            false, "Invalid evidence handoff.");
                    }
                    else
                    {
                        result = await executor.ExecuteGovernedAsync(
                            BoundedAgentPlan.GetAgentName(lease.Stage),
                            request with
                            {
                                ExecutionId =
                                    BoundedAgentPlan.GetInvocationId(
                                        state.ExecutionId, lease.Stage),
                                Input = input
                            },
                            caller,
                            new AgentExecutionLimits(remaining, 1),
                            budget.Token);
                    }
                }

                budget.Token.ThrowIfCancellationRequested();

                if (result.Succeeded &&
                    ((lease.Stage == 2 &&
                      !ValidHandoff(result.Output, request.Input)) ||
                     (lease.Stage == 3 &&
                      (string.IsNullOrWhiteSpace(result.Output) ||
                       result.Output.Length > 8000))))
                {
                    result = new AgentResult(
                        false, "Invalid agent output.");
                }

                if (result.Output is null ||
                    result.Output.Length > 32000 ||
                    (lease.Stage == 3 && result.Output.Length > 8000))
                {
                    result = new AgentResult(
                        false, "Agent output exceeds limits.");
                }

                var saved = !result.Succeeded && result.Retryable
                    ? await store.ReleaseForRetryAsync(
                        lease, caller, budget.Token)
                    : await store.CheckpointAsync(
                        lease, caller, result, budget.Token);

                if (!saved)
                    return (await ReloadAsync()) with { FinalAnswer = null };
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                // Disconnects do not destroy recoverable execution state.
                // The lease expires and another authorized invocation resumes.
                throw;
            }
            catch (OperationCanceledException)
                when (budget.IsCancellationRequested)
            {
                return (await ReloadAsync()) with { FinalAnswer = null };
            }
            catch (HttpRequestException)
            {
                await store.ReleaseForRetryAsync(
                    lease, caller, cancellationToken);
            }

            state = await ReloadAsync();
        }

        if (state.Status == "Completed")
        {
            var memory = await store.ReadMemoryAsync(
                state.ExecutionId, caller, cancellationToken);

            if (memory.Count != BoundedAgentPlan.StageCount ||
                memory.Where((item, index) =>
                    item.Stage != index ||
                    !item.Succeeded ||
                    item.AgentName != BoundedAgentPlan.GetAgentName(index) ||
                    item.InvocationId != BoundedAgentPlan.GetInvocationId(
                        state.ExecutionId, index)).Any() ||
                !ValidHandoff(memory[2].Output, request.Input))
            {
                return state with
                {
                    Status = "Failed",
                    FinalAnswer = null,
                    FailureCode = "InvalidExecutionMemory"
                };
            }

            var verified = await executor.ExecuteGovernedAsync(
                AgentNames.Verifier,
                request with
                {
                    ExecutionId = BoundedAgentPlan.GetInvocationId(
                        state.ExecutionId, 3),
                    Input = memory[2].Output
                },
                caller,
                new AgentExecutionLimits(TimeSpan.FromSeconds(150), 1),
                cancellationToken);

            if (!verified.Succeeded ||
                string.IsNullOrWhiteSpace(verified.Output) ||
                verified.Output.Length > 8000 ||
                !string.Equals(verified.Output, state.FinalAnswer,
                    StringComparison.Ordinal))
            {
                return state with
                {
                    Status = "Failed",
                    FinalAnswer = null,
                    FailureCode = "EvidenceRevalidationFailed"
                };
            }
        }

        return state;

        async Task<DurableAgentExecution> ReloadAsync() =>
            await store.GetAsync(
                request.ExecutionId, caller, cancellationToken)
            ?? throw new UnauthorizedAccessException();
    }

    private static bool ValidHandoff(string? output, string question)
    {
        if (string.IsNullOrWhiteSpace(output) ||
            output.Length > 32000)
            return false;

        try
        {
            var handoff =
                JsonSerializer.Deserialize<KnowledgeHandoff>(output);

            return handoff is not null &&
                string.Equals(handoff.Question, question,
                    StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}