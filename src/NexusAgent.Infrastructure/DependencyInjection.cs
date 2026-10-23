using NexusAgent.Infrastructure.Search;
using NexusAgent.Application.Search;
using NexusAgent.Infrastructure.Persistence.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexusAgent.Application.Abstractions;
using NexusAgent.Application.Documents;
using NexusAgent.Infrastructure.Persistence;
using NexusAgent.Infrastructure.Persistence.Outbox;
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
            options => options.UseNpgsql(connectionString, npgsql => npgsql.UseVector()));

        var embeddingProvider = configuration["Embeddings:Provider"];

        if (string.Equals(
            embeddingProvider,
            "AzureOpenAI",
            StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<ITextEmbeddingProvider, AzureOpenAiEmbeddingProvider>(
                client => client.Timeout = TimeSpan.FromSeconds(30));
        }
        else if (!string.IsNullOrWhiteSpace(embeddingProvider) &&
                 !string.Equals(
                     embeddingProvider,
                     "Disabled",
                     StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Unsupported embedding provider: {embeddingProvider}");
        }
        services.AddScoped<DocumentContentIndexer>();
        services.AddScoped<PublishedDocumentIndexProcessor>();
        services.AddScoped<IWorkflowRepository, WorkflowRepository>();
        services.AddScoped<IKnowledgeDocumentRepository, KnowledgeDocumentRepository>();
        services.AddScoped<IDocumentIngestionEventPublisher, PostgresDocumentIngestionEventPublisher>();

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
