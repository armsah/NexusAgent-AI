namespace NexusAgent.Application.Tools;

public sealed class ToolAllowList
{
    private readonly HashSet<string> _allowed;

    public ToolAllowList(IEnumerable<string> allowedToolNames)
    {
        ArgumentNullException.ThrowIfNull(allowedToolNames);

        _allowed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var name in allowedToolNames)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException(
                    "Allow-list entries must have nonempty names.");
            }

            _allowed.Add(name);
        }
    }

    public bool IsAllowed(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        _allowed.Contains(name);
}