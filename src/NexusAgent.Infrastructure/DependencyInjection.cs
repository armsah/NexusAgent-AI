using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexusAgent.Application.Abstractions;
using NexusAgent.Infrastructure.Persistence;
using NexusAgent.Infrastructure.Persistence.Repositories;
using NexusAgent.Infrastructure.System;

namespace NexusAgent.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString =
            configuration.GetConnectionString("NexusAgent")
            ?? throw new InvalidOperationException(
                "Connection string 'NexusAgent' is not configured.");

        services.AddDbContext<NexusAgentDbContext>(
            options => options.UseNpgsql(connectionString));

        services.AddScoped<IWorkflowRepository, WorkflowRepository>();

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdGenerator, GuidIdGenerator>();

        return services;
    }
}