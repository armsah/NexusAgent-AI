using NexusAgent.Application.Abstractions;
using NexusAgent.Application.Workflows;
using NexusAgent.Domain.Workflows;

namespace NexusAgent.Application.Orchestration;

public sealed class WorkflowOrchestrator(
    IWorkflowRepository repository,
    IClock clock,
    IEnumerable<IOrchestrationStepExecutor> executors)
{
    private readonly IReadOnlyDictionary<string, IOrchestrationStepExecutor>
        _executors = executors.ToDictionary(
            executor => executor.StepName,
            StringComparer.Ordinal);

    public async Task<WorkflowDto?> ExecuteAsync(
        Guid workflowId,
        WorkflowAccess access,
        OrchestrationPlan plan,
        OrchestrationLimits limits,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(limits);

        limits.Validate();

        if (plan.Steps is null ||
            plan.Steps.Count == 0 ||
            plan.Steps.Count > limits.MaxSteps)
        {
            throw new ArgumentException(
                "Plan exceeds configured step limits.",
                nameof(plan));
        }

        foreach (var step in plan.Steps)
        {
            if (step is null ||
                string.IsNullOrWhiteSpace(step.Name) ||
                !_executors.ContainsKey(step.Name))
            {
                throw new InvalidOperationException(
                    "Plan contains an unregistered step.");
            }
        }

        var workflow = await repository.GetByIdAsync(
            workflowId,
            cancellationToken);

        if (workflow is null || !access.CanAccess(workflow))
        {
            return null;
        }

        if (workflow.Status != WorkflowStatus.Created)
        {
            throw new InvalidOperationException(
                "Only newly created workflows can be orchestrated.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);

        timeout.CancelAfter(limits.EffectiveTimeout);

        // Claim the workflow before invoking any executor.
        // A competing worker receives WorkflowConcurrencyException and exits.
        workflow.Start(clock.UtcNow);

        try
        {
            await repository.SaveChangesAsync(timeout.Token);
        }
        catch (WorkflowConcurrencyException)
        {
            // Do not execute steps or mutate the stale workflow further.
            throw;
        }

        WorkflowStep? activeStep = null;

        try
        {
            foreach (var step in plan.Steps)
            {
                timeout.Token.ThrowIfCancellationRequested();

                activeStep = workflow.StartStep(
                    step.Name,
                    clock.UtcNow);

                await repository.SaveChangesAsync(timeout.Token);

                OrchestrationStepResult? result = null;

                for (var attempt = 1;
                     attempt <= limits.MaxAttemptsPerStep;
                     attempt++)
                {
                    timeout.Token.ThrowIfCancellationRequested();

                    result = await _executors[step.Name].ExecuteAsync(
                        step.Input,
                        timeout.Token);

                    if (result.Succeeded || !result.Retryable)
                    {
                        break;
                    }
                }

                if (result is null || !result.Succeeded)
                {
                    var reason = $"Step '{step.Name}' failed.";

                    workflow.CompleteStep(
                        activeStep.Id,
                        reason,
                        clock.UtcNow);

                    workflow.Fail(reason, clock.UtcNow);

                    await repository.SaveChangesAsync(
                        CancellationToken.None);

                    return WorkflowDto.FromDomain(workflow);
                }

                workflow.CompleteStep(
                    activeStep.Id,
                    result.Outcome,
                    clock.UtcNow);

                activeStep = null;

                await repository.SaveChangesAsync(timeout.Token);
            }

            workflow.Complete(clock.UtcNow);

            await repository.SaveChangesAsync(timeout.Token);

            return WorkflowDto.FromDomain(workflow);
        }
        catch (OperationCanceledException)
        {
            if (activeStep is not null && !activeStep.IsCompleted)
            {
                workflow.CompleteStep(
                    activeStep.Id,
                    "Cancelled",
                    clock.UtcNow);
            }

            workflow.Cancel(clock.UtcNow);

            await repository.SaveChangesAsync(
                CancellationToken.None);

            throw;
        }
        catch (Exception)
        {
            if (activeStep is not null && !activeStep.IsCompleted)
            {
                workflow.CompleteStep(
                    activeStep.Id,
                    "Execution failed",
                    clock.UtcNow);
            }

            workflow.Fail(
                "Unexpected orchestration failure.",
                clock.UtcNow);

            await repository.SaveChangesAsync(
                CancellationToken.None);

            throw;
        }
    }
}