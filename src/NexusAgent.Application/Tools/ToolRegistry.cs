namespace NexusAgent.Application.Tools;

public sealed class ToolRegistry
{
    private readonly Dictionary<string, IToolRegistration> _registrations =
        new(StringComparer.Ordinal);

    public ToolRegistry(IEnumerable<IToolRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        foreach (var registration in registrations)
        {
            ArgumentNullException.ThrowIfNull(registration);

            if (string.IsNullOrWhiteSpace(registration.Name))
            {
                throw new ArgumentException(
                    "Registered tools must have a nonempty name.");
            }

            if (!_registrations.TryAdd(registration.Name, registration))
            {
                throw new InvalidOperationException(
                    "Duplicate tool registration.");
            }
        }
    }

    public bool TryResolve<TInput, TOutput>(
        string name,
        out ITypedTool<TInput, TOutput>? tool)
    {
        tool = null;

        if (string.IsNullOrWhiteSpace(name) ||
            !_registrations.TryGetValue(name, out var registration))
        {
            return false;
        }

        if (registration is not ToolRegistration<TInput, TOutput> typed)
        {
            return false;
        }

        tool = typed.Tool;
        return true;
    }
}