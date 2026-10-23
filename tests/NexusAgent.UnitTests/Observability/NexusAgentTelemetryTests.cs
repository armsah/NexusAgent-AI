using System.Diagnostics;
using NexusAgent.Application.Observability;
using Xunit;

namespace NexusAgent.UnitTests.Observability;

public sealed class NexusAgentTelemetryTests
{
    [Fact]
    public void Child_activity_inherits_parent_trace_identifier()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source =>
                source.Name == NexusAgentTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> options) =>
                ActivitySamplingResult.AllDataAndRecorded
        };

        ActivitySource.AddActivityListener(listener);

        using var parent = NexusAgentTelemetry.StartActivity(
            "grounded_answer.request");

        Assert.NotNull(parent);

        using var child = NexusAgentTelemetry.StartActivity(
            "grounded_answer.retrieval");

        Assert.NotNull(child);
        Assert.Equal(parent.TraceId, child.TraceId);
        Assert.Equal(parent.SpanId, child.ParentSpanId);
    }

    [Fact]
    public void Telemetry_source_uses_stable_name()
    {
        Assert.Equal(
            "NexusAgent",
            NexusAgentTelemetry.ActivitySource.Name);
    }
}