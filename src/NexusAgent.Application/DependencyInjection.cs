using NexusAgent.Application.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using NexusAgent.Application.Workflows;
using NexusAgent.Application.Documents;

namespace NexusAgent.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(
        this IServiceCollection services)
    {
        services.AddScoped<CreateDocumentHandler>();
        services.AddScoped<GetDocumentHandler>();
        services.AddScoped<UpdateDocumentHandler>();
        services.AddScoped<UpdateDocumentContentHandler>();
        services.AddScoped<DeleteDocumentHandler>();
        services.AddScoped<GetDocumentContentHandler>();
        services.AddScoped<CreateWorkflowHandler>();
        services.AddScoped<GetWorkflowHandler>();
        services.AddScoped<StartWorkflowHandler>();
        services.AddScoped<CompleteWorkflowHandler>();
        services.AddScoped<WorkflowOrchestrator>();

        return services;
    }
}
