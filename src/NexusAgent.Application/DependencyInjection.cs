using Microsoft.Extensions.DependencyInjection;
using NexusAgent.Application.Workflows;

namespace NexusAgent.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(
        this IServiceCollection services)
    {
        services.AddScoped<CreateWorkflowHandler>();
        services.AddScoped<GetWorkflowHandler>();
        services.AddScoped<StartWorkflowHandler>();
        services.AddScoped<CompleteWorkflowHandler>();

        return services;
    }
}