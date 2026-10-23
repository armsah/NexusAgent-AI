using NexusAgent.Infrastructure.Persistence.Search;

namespace NexusAgent.Workers;

public sealed class DocumentIndexWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<DocumentIndexWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();

                var processor = scope.ServiceProvider
                    .GetRequiredService<PublishedDocumentIndexProcessor>();

                var count = await processor.ProcessAsync(
                    cancellationToken: stoppingToken);

                if (count == 0)
                    await Task.Delay(1000, stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Document indexing cycle failed.");

                await Task.Delay(5000, stoppingToken);
            }
        }
    }
}