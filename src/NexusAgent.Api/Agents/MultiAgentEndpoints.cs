using NexusAgent.Api.Identity;
using NexusAgent.Application.Abstractions;
using NexusAgent.Application.Agents;
using NexusAgent.Application.Workflows;

namespace NexusAgent.Api.Agents;

public static class MultiAgentEndpoints
{
    public static void MapMultiAgentEndpoints(
        this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
            throw new InvalidOperationException(
                "Development agent endpoints are disabled.");

        app.MapPost(
            "/api/workflows/{id:guid}/agents/execute",
            async (
                Guid id,
                DevelopmentRequestIdentity identity,
                IWorkflowRepository repository,
                MultiAgentCoordinator coordinator,
                CancellationToken cancellationToken) =>
            {
                var caller = new WorkflowAccess(
                    identity.TenantId, identity.RequesterId);

                var workflow = await repository.GetByIdAsync(
                    id, cancellationToken);

                if (workflow is null || !caller.CanAccess(workflow))
                    return Results.NotFound();

                if (string.IsNullOrWhiteSpace(workflow.Request)
                    || workflow.Request.Length > 2000)
                    return Results.BadRequest();

                var request = new AgentRequest(
                    Guid.NewGuid(),
                    workflow.Id,
                    caller.TenantId,
                    workflow.Request);

                try
                {
                    var result = await coordinator.ExecuteAsync(
                        request,
                        caller,
                        TimeSpan.FromMinutes(5),
                        cancellationToken);

                    return Results.Ok(result);
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
    }
}