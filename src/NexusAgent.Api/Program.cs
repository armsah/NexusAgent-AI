using NexusAgent.Api.Health;
using NexusAgent.Api.Questions;
using NexusAgent.Api.Search;
using NexusAgent.Api.Documents;
using NexusAgent.Application;
using NexusAgent.Application.Workflows;
using NexusAgent.Api.Identity;
using NexusAgent.Domain.Common;
using NexusAgent.Infrastructure;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using NexusAgent.Infrastructure.Persistence;


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter());
});
builder.Services.AddApplication();
builder.Services.AddInfrastructure(
    builder.Configuration);
builder.Services.AddSingleton<DevelopmentRequestIdentity>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapDocumentEndpoints();
    app.MapDocumentSearchEndpoints();
    app.MapGroundedQuestionEndpoints();
}


app.MapNexusAgentHealthEndpoints();

if (app.Environment.IsDevelopment())
{
var workflows = app.MapGroup("/api/workflows");

workflows.MapPost(
    "/",
    async (
        CreateWorkflowRequest request,
        CreateWorkflowHandler handler,
        DevelopmentRequestIdentity identity,
        CancellationToken cancellationToken) =>
    {
        if (string.IsNullOrWhiteSpace(request.Request))
        {
            return Results.BadRequest(new
            {
                error = "Request is required."
            });
        }

        var command = new CreateWorkflowCommand(
            identity.RequesterId,
            identity.TenantId,
            request.Request);

        var workflow = await handler.HandleAsync(
            command,
            cancellationToken);

        return Results.Created(
            $"/api/workflows/{workflow.Id}",
            workflow);
    });


workflows.MapGet(
    "/{id:guid}",
    async (
        Guid id,
        GetWorkflowHandler handler,
        DevelopmentRequestIdentity identity,
        CancellationToken cancellationToken) =>
    {
        var workflow = await handler.HandleAsync(
                id,
                new WorkflowAccess(identity.TenantId, identity.RequesterId),
                cancellationToken);

        return workflow is null
            ? Results.NotFound()
            : Results.Ok(workflow);
    });

workflows.MapPost(
    "/{id:guid}/start",
    async (
        Guid id,
        StartWorkflowHandler handler,
        DevelopmentRequestIdentity identity,
        CancellationToken cancellationToken) =>
    {
        try
        {
            var workflow = await handler.HandleAsync(
                id,
                new WorkflowAccess(identity.TenantId, identity.RequesterId),
                cancellationToken);

            return workflow is null
                ? Results.NotFound()
                : Results.Ok(workflow);
        }
        catch (DomainException exception)
        {
            return Results.Conflict(new
            {
                error = exception.Message
            });
        }
    });

workflows.MapPost(
    "/{id:guid}/complete",
    async (
        Guid id,
        CompleteWorkflowHandler handler,
        DevelopmentRequestIdentity identity,
        CancellationToken cancellationToken) =>
    {
        try
        {
            var workflow = await handler.HandleAsync(
                id,
                new WorkflowAccess(identity.TenantId, identity.RequesterId),
                cancellationToken);

            return workflow is null
                ? Results.NotFound()
                : Results.Ok(workflow);
        }
        catch (DomainException exception)
        {
            return Results.Conflict(new
            {
                error = exception.Message
            });
        }
    });


}
app.Run();

public sealed record CreateWorkflowRequest(string Request);
public partial class Program;
