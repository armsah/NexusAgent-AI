namespace NexusAgent.Application.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}