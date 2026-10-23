namespace NexusAgent.Application.Search;

public static class EmbeddingValidation
{
    public const int RequiredDimensions = 1536;

    public static void Validate(
        ITextEmbeddingProvider provider,
        float[] embedding)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(embedding);

        if (string.IsNullOrWhiteSpace(provider.ModelId))
        {
            throw new InvalidOperationException(
                "Embedding model identifier is required.");
        }

        if (provider.Dimensions != RequiredDimensions)
        {
            throw new InvalidOperationException(
                $"Embedding provider must produce {RequiredDimensions} dimensions.");
        }

        if (embedding.Length != RequiredDimensions)
        {
            throw new InvalidOperationException(
                $"Embedding contains {embedding.Length} dimensions; expected {RequiredDimensions}.");
        }

        if (embedding.Any(value => !float.IsFinite(value)))
        {
            throw new InvalidOperationException(
                "Embedding contains non-finite values.");
        }

        if (embedding.All(value => value == 0))
        {
            throw new InvalidOperationException(
                "Embedding must not be a zero vector.");
        }
    }
}