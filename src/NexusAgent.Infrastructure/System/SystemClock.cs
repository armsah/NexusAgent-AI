using NexusAgent.Application.Abstractions;

namespace NexusAgent.Infrastructure.System;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}