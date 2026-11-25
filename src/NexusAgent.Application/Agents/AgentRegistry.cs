namespace NexusAgent.Application.Agents;

public sealed class AgentRegistry
{
    private readonly Dictionary<string, IAgent> _agents =
        new(StringComparer.Ordinal);

    public AgentRegistry(IEnumerable<IAgent> agents)
    {
        ArgumentNullException.ThrowIfNull(agents);

        foreach (var agent in agents)
        {
            ArgumentNullException.ThrowIfNull(agent);
            ArgumentException.ThrowIfNullOrWhiteSpace(agent.Name);

            if (!_agents.TryAdd(agent.Name, agent))
                throw new InvalidOperationException("Duplicate agent registration.");
        }
    }

    public IAgent Resolve(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return _agents.TryGetValue(name, out var agent)
            ? agent
            : throw new KeyNotFoundException("Agent not registered.");
    }
}