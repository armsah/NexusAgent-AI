namespace NexusAgent.Infrastructure;

internal static class LocalInferenceAuthentication
{
    internal static void Configure(HttpClient client)
    {
        var key = Environment.GetEnvironmentVariable(
            "NEXUS_INFERENCE_API_KEY");

        if (string.IsNullOrWhiteSpace(key) || key.Length < 32)
            throw new InvalidOperationException(
                "Local inference authentication is not configured.");

        client.DefaultRequestHeaders.Add("X-Inference-Key", key);
    }
}