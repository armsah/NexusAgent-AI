namespace NexusAgent.Application.Tools;

public sealed record ToolCaller(
    string TenantId,
    string RequesterId);

public interface IToolAuthorizationPolicy
{
    bool IsAuthorized(
        ToolCaller caller,
        string toolName);
}

public sealed class DenyAllToolAuthorizationPolicy : IToolAuthorizationPolicy
{
    public bool IsAuthorized(
        ToolCaller caller,
        string toolName) => false;
}

public sealed record ToolExecutionLimits(
    TimeSpan? Timeout = null)
{
    public TimeSpan EffectiveTimeout =>
        Timeout ?? TimeSpan.FromSeconds(10);

    public void Validate()
    {
        if (EffectiveTimeout <= TimeSpan.Zero ||
            EffectiveTimeout > TimeSpan.FromMinutes(2))
        {
            throw new ArgumentOutOfRangeException(nameof(Timeout));
        }
    }
}

public sealed record ToolExecutionAudit(
    Guid CorrelationId,
    string ToolName,
    string TenantId,
    string RequesterId,
    DateTimeOffset StartedAt,
    TimeSpan Duration,
    string Outcome);

public interface IToolAuditSink
{
    Task RecordAsync(
        ToolExecutionAudit audit,
        CancellationToken cancellationToken);
}

public sealed class NullToolAuditSink : IToolAuditSink
{
    public Task RecordAsync(
        ToolExecutionAudit audit,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

public sealed record ToolExecutionResponse<TOutput>(
    Guid CorrelationId,
    ToolResult<TOutput> Result);