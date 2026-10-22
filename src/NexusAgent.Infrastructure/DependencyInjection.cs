using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexusAgent.Application.Abstractions;
using NexusAgent.Application.Documents;
using NexusAgent.Infrastructure.Persistence;
using NexusAgent.Infrastructure.Persistence.Repositories;
using NexusAgent.Infrastructure.System;
using NexusAgent.Infrastructure.Storage;

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
        services.AddScoped<IKnowledgeDocumentRepository, KnowledgeDocumentRepository>();

        var storageRoot = configuration["DocumentStorage:RootPath"];

        if (string.IsNullOrWhiteSpace(storageRoot))
        {
            throw new InvalidOperationException(
                "DocumentStorage:RootPath is not configured.");
        }

        if (!Path.IsPathFullyQualified(storageRoot))
        {
            throw new InvalidOperationException(
                "DocumentStorage:RootPath must be an absolute path.");
        }

        services.AddSingleton<IDocumentContentStore>(
            new LocalDocumentContentStore(storageRoot));
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdGenerator, GuidIdGenerator>();

        return services;
    }
}
