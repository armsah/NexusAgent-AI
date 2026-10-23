namespace NexusAgent.Application.Tools;

public sealed record ToolRequest<TInput>(
    string ToolName,
    TInput Input);

public sealed record ToolResult<TOutput>(
    bool Succeeded,
    TOutput? Output,
    string? ErrorCode = null);

public interface ITypedTool<TInput, TOutput>
{
    string Name { get; }

    Task<ToolResult<TOutput>> ExecuteAsync(
        TInput input,
        CancellationToken cancellationToken);
}

public interface IToolRegistration
{
    string Name { get; }

    Type InputType { get; }

    Type OutputType { get; }
}

public sealed record ToolRegistration<TInput, TOutput>(
    ITypedTool<TInput, TOutput> Tool) : IToolRegistration
{
    public string Name => Tool.Name;

    public Type InputType => typeof(TInput);

    public Type OutputType => typeof(TOutput);
}