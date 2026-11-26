using System;
using System.IO;
using System.Text.Json;
using NexusAgent.Application.Documents;
using Xunit;

public sealed class E6DocumentWireContractTests
{
    [Theory]
    [InlineData("document.created", 1, 1)]
    [InlineData("document.updated", 2, 2)]
    [InlineData("document.deleted", 3, 2)]
    public void SerializeProducerContract(string type, int revision, int content)
    {
        var value = new DocumentIngestionEvent(
            Guid.Parse($"00000000-0000-0000-0000-{revision:D12}"),
            type,
            "tenant-e6",
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            revision,
            content,
            DateTimeOffset.Parse("2026-01-01T12:00:00+02:00")
                .AddSeconds(revision));

        var json = DocumentEventWireCodec.Serialize(value);
        using var parsed = JsonDocument.Parse(json);

        Assert.Equal(1, parsed.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(type, parsed.RootElement.GetProperty("eventType").GetString());
        Assert.Equal(value.EventId, parsed.RootElement.GetProperty("eventId").GetGuid());
        Assert.Equal(value.OccurredAt, parsed.RootElement.GetProperty("occurredAt").GetDateTimeOffset());

        var directory = Environment.GetEnvironmentVariable("NEXUS_E6_FIXTURES");

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, type + ".json"), json);
        }
    }

    [Fact]
    public void RejectInvalidProducerEvent()
    {
        var value = new DocumentIngestionEvent(
            Guid.Empty, "document.created", "tenant-e6",
            Guid.NewGuid(), 1, 1, DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentException>(
            () => DocumentEventWireCodec.Serialize(value));
    }
}