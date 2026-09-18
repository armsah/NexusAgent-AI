using NexusAgent.Application.Abstractions;

namespace NexusAgent.Infrastructure.System;

public sealed class GuidIdGenerator : IIdGenerator
{
    public Guid NewId() => Guid.NewGuid();
}