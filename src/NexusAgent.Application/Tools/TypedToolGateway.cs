using System.Diagnostics;

namespace NexusAgent.Application.Tools;

public sealed class TypedToolGateway(
    ToolRegistry registry,
    ToolAllowList allowList,
    IToolAuthorizationPolicy authorization,
    IToolAuditSink auditSink)
{
    public async Task<ToolExecutionResponse<TOutput>> ExecuteAsync<TInput, TOutput>(
        ToolRequest<TInput> request,
        ToolCaller caller,
        ToolExecutionLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(caller);

        limits ??= new ToolExecutionLimits();
        limits.Validate();

        var correlationId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var outcome = "rejected";

        try
        {
            ToolResult<TOutput> result;

            if (string.IsNullOrWhiteSpace(caller.TenantId) ||
                string.IsNullOrWhiteSpace(caller.RequesterId) ||
                !allowList.IsAllowed(request.ToolName) ||
                !authorization.IsAuthorized(caller, request.ToolName))
            {
                outcome = "tool_access_denied";
                result = new ToolResult<TOutput>(
                    false,
                    default,
                    outcome);
            }
            else if (!registry.TryResolve<TInput, TOutput>(
                request.ToolName,
                out var tool))
            {
                outcome = "tool_not_registered_or_type_mismatch";
                result = new ToolResult<TOutput>(
                    false,
                    default,
                    outcome);
            }
            else
            {
                using var timeout =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken);

                timeout.CancelAfter(limits.EffectiveTimeout);

                try
                {
                    result = await tool!.ExecuteAsync(
                        request.Input,
                        timeout.Token);

                    outcome = result.Succeeded
                        ? "succeeded"
                        : "tool_failed";
                }
                catch (OperationCanceledException)
                    when (timeout.IsCancellationRequested)
                {
                    outcome = cancellationToken.IsCancellationRequested
                        ? "tool_cancelled"
                        : "tool_timeout";

                    result = new ToolResult<TOutput>(
                        false,
                        default,
                        outcome);
                }
                catch (Exception)
                {
                    outcome = "tool_execution_failed";

                    result = new ToolResult<TOutput>(
                        false,
                        default,
                        outcome);
                }
            }

            return new ToolExecutionResponse<TOutput>(
                correlationId,
                result);
        }
        finally
        {
            stopwatch.Stop();

            await auditSink.RecordAsync(
                new ToolExecutionAudit(
                    correlationId,
                    request.ToolName,
                    caller.TenantId,
                    caller.RequesterId,
                    startedAt,
                    stopwatch.Elapsed,
                    outcome),
                CancellationToken.None);
        }
    }
}