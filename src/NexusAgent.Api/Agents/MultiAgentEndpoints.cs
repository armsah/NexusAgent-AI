using NexusAgent.Api.Identity;
using NexusAgent.Application.Abstractions;
using NexusAgent.Application.Agents;
using NexusAgent.Application.Workflows;

namespace NexusAgent.Api.Agents;

public static class MultiAgentEndpoints
{
    public static void MapMultiAgentEndpoints(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
            throw new InvalidOperationException(
                "Development agent endpoints are disabled.");

        app.MapPost(
            "/api/workflows/{id:guid}/agents/execute",
            async (
                Guid id,
                Guid? executionId,
                DevelopmentRequestIdentity identity,
                IWorkflowRepository repository,
                DurableMultiAgentCoordinator coordinator,
                IDurableAgentExecutionStore store,
                CancellationToken cancellationToken) =>
            {
                var caller = new WorkflowAccess(
                    identity.TenantId, identity.RequesterId);

                var workflow = await repository.GetByIdAsync(
                    id, cancellationToken);

                if (workflow is null || !caller.CanAccess(workflow))
                    return Results.NotFound();

                if (string.IsNullOrWhiteSpace(workflow.Request) ||
                    workflow.Request.Length > 2000 ||
                    executionId == Guid.Empty)
                    return Results.BadRequest();

                var request = new AgentRequest(
                    executionId ?? workflow.Id,
                    workflow.Id,
                    caller.TenantId,
                    workflow.Request);

                try
                {
                    var state = await coordinator.ExecuteAsync(
                        request, caller, TimeSpan.FromMinutes(5),
                        cancellationToken);

                    if (state.Status is "Pending" or "Running" ||
                        (state.Status == "Completed" &&
                         string.IsNullOrWhiteSpace(state.FinalAnswer)))
                    {
                        return Results.Accepted(
                            $"/api/workflows/{id}/agents/executions/{state.ExecutionId}",
                            new
                            {
                                state.ExecutionId,
                                state.Status,
                                state.NextStage,
                                state.DeadlineAt
                            });
                    }

                    var memory = await store.ReadMemoryAsync(
                        state.ExecutionId, caller, cancellationToken);

                    var succeeded = state.Status == "Completed" &&
                        !string.IsNullOrWhiteSpace(state.FinalAnswer);

                    return Results.Ok(new MultiAgentResult(
                        succeeded,
                        succeeded ? state.FinalAnswer! : "Execution rejected.",
                        memory.Select(x => new AgentStageResult(
                            x.AgentName, x.Succeeded)).ToArray()));
                }
                catch (UnauthorizedAccessException)
                {
                    return Results.NotFound();
                }
                catch (OperationCanceledException)
                    when (!cancellationToken.IsCancellationRequested)
                {
                    return Results.StatusCode(504);
                }
                catch (HttpRequestException)
                {
                    return Results.StatusCode(503);
                }
            });

        app.MapGet(
            "/api/workflows/{id:guid}/agents/executions/{executionId:guid}",
            async (
                Guid id,
                Guid executionId,
                DevelopmentRequestIdentity identity,
                IDurableAgentExecutionStore store,
                CancellationToken cancellationToken) =>
            {
                var caller = new WorkflowAccess(
                    identity.TenantId, identity.RequesterId);

                var state = await store.GetAsync(
                    executionId, caller, cancellationToken);

                if (state is null || state.WorkflowId != id)
                    return Results.NotFound();

                // Status inspection must not disclose cached answers.
                return Results.Ok(new
                {
                    state.ExecutionId,
                    state.WorkflowId,
                    state.Status,
                    state.NextStage,
                    state.AttemptCount,
                    state.DeadlineAt,
                    state.FailureCode
                });
            });

        app.MapPost(
            "/api/workflows/{id:guid}/agents/executions/{executionId:guid}/cancel",
            async (
                Guid id,
                Guid executionId,
                DevelopmentRequestIdentity identity,
                IDurableAgentExecutionStore store,
                CancellationToken cancellationToken) =>
            {
                var caller = new WorkflowAccess(
                    identity.TenantId, identity.RequesterId);

                var state = await store.GetAsync(
                    executionId, caller, cancellationToken);

                if (state is null || state.WorkflowId != id)
                    return Results.NotFound();

                var cancelled = await store.CancelAsync(
                    executionId, caller, cancellationToken);

                return cancelled
                    ? Results.NoContent()
                    : Results.Conflict();
            });
    }
}