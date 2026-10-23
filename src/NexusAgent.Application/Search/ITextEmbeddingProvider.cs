namespace NexusAgent.Application.Search;

/// <summary>
/// Generates semantic embeddings for document content and search queries.
/// Implementations must use the same model for indexing and querying.
/// </summary>
public interface ITextEmbeddingProvider
{
    string ModelId { get; }

    int Dimensions { get; }

    Task<float[]> EmbedAsync(
        string text,
        CancellationToken cancellationToken = default);
}