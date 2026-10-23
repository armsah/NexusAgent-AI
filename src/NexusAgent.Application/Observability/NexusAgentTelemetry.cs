using System.Diagnostics;

namespace NexusAgent.Application.Observability;

public static class NexusAgentTelemetry
{
    public const string SourceName = "NexusAgent";

    public static readonly ActivitySource ActivitySource =
        new(SourceName, "1.0.0");

    public static Activity? StartActivity(string operation) =>
        ActivitySource.StartActivity(
            operation,
            ActivityKind.Internal);
}