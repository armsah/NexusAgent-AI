using System;
using System.IO;
using System.Text.Json;
using NexusAgent.Application.Workflows;
using Xunit;

public sealed class E6WorkflowWireContractTests
{
    [Theory]
    [InlineData(WorkflowEventKind.Started, "STARTED", 1, null)]
    [InlineData(WorkflowEventKind.ApprovalRequired, "APPROVAL_REQUIRED", 2, null)]
    [InlineData(WorkflowEventKind.Approved, "APPROVED", 3, null)]
    [InlineData(WorkflowEventKind.Completed, "COMPLETED", 4, 3000L)]
    [InlineData(WorkflowEventKind.Rejected, "REJECTED", 3, 2000L)]
    [InlineData(WorkflowEventKind.Failed, "FAILED", 2, 1000L)]
    [InlineData(WorkflowEventKind.Cancelled, "CANCELLED", 2, 1000L)]
    public void SerializeLifecycleEvent(
        WorkflowEventKind kind, string expected, int sequence, long? duration)
    {
        var value = new WorkflowLifecycleEvent(
            Guid.Parse($"20000000-0000-0000-0000-{(int)kind + 1:D12}"),
            "tenant-e6",
            Guid.Parse("30000000-0000-0000-0000-000000000001"),
            sequence,
            kind,
            DateTimeOffset.Parse("2026-01-01T12:00:00+02:00")
                .AddSeconds(sequence),
            duration);

        var json = WorkflowEventWireCodec.Serialize(value);
        using var parsed = JsonDocument.Parse(json);

        Assert.Equal(expected, parsed.RootElement.GetProperty("eventType").GetString());
        Assert.Equal(sequence, parsed.RootElement.GetProperty("sequence").GetInt64());
        Assert.Equal(value.EventId, parsed.RootElement.GetProperty("eventId").GetGuid());

        var directory = Environment.GetEnvironmentVariable("NEXUS_E6_FIXTURES");

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "workflow." + expected + ".json"), json);
        }
    }

    [Theory]
    [InlineData(WorkflowEventKind.Completed, null)]
    [InlineData(WorkflowEventKind.Failed, -1L)]
    [InlineData(WorkflowEventKind.Started, 100L)]
    public void RejectInvalidDuration(WorkflowEventKind kind, long? duration)
    {
        var value = new WorkflowLifecycleEvent(
            Guid.NewGuid(), "tenant-e6", Guid.NewGuid(),
            1, kind, DateTimeOffset.UtcNow, duration);

        Assert.Throws<ArgumentException>(
            () => WorkflowEventWireCodec.Serialize(value));
    }
}