namespace NexusAgent.Application.Orchestration;

public sealed record OrchestrationStep(
    string Name,
    string Input);

public sealed record OrchestrationPlan(
    IReadOnlyList<OrchestrationStep> Steps);

public sealed record OrchestrationLimits(
    int MaxSteps = 5,
    int MaxAttemptsPerStep = 2,
    TimeSpan? ExecutionTimeout = null)
{
    public TimeSpan EffectiveTimeout =>
        ExecutionTimeout ?? TimeSpan.FromSeconds(30);

    public void Validate()
    {
        if (MaxSteps is < 1 or > 20)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxSteps));
        }

        if (MaxAttemptsPerStep is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxAttemptsPerStep));
        }

        if (EffectiveTimeout <= TimeSpan.Zero ||
            EffectiveTimeout > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(
                nameof(ExecutionTimeout));
        }
    }
}

public sealed record OrchestrationStepResult(
    bool Succeeded,
    string Outcome,
    bool Retryable = false);

public interface IOrchestrationStepExecutor
{
    string StepName { get; }

    Task<OrchestrationStepResult> ExecuteAsync(
        string input,
        CancellationToken cancellationToken);
}