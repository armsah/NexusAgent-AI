namespace NexusAgent.Application.Approvals;

public interface IActionRiskPolicy
{
    ActionRisk Classify(string toolName);

    bool RequiresApproval(ActionRisk risk);
}

public sealed class ExplicitActionRiskPolicy : IActionRiskPolicy
{
    private readonly Dictionary<string, ActionRisk> _risks;

    public ExplicitActionRiskPolicy(
        IEnumerable<KeyValuePair<string, ActionRisk>> risks)
    {
        ArgumentNullException.ThrowIfNull(risks);

        _risks = new Dictionary<string, ActionRisk>(
            StringComparer.Ordinal);

        foreach (var entry in risks)
        {
            if (string.IsNullOrWhiteSpace(entry.Key))
            {
                throw new ArgumentException(
                    "Tool names must be nonempty.");
            }

            if (!Enum.IsDefined(entry.Value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(risks),
                    "Invalid risk classification.");
            }

            if (!_risks.TryAdd(entry.Key, entry.Value))
            {
                throw new InvalidOperationException(
                    "Duplicate tool risk classification.");
            }
        }
    }

    public ActionRisk Classify(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName) ||
            !_risks.TryGetValue(toolName, out var risk))
        {
            return ActionRisk.Critical;
        }

        return risk;
    }

    public bool RequiresApproval(ActionRisk risk) =>
        risk >= ActionRisk.High;
}