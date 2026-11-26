namespace NexusAgent.Infrastructure.Persistence.Agents;

public sealed class AgentExecutionState
{
    public Guid Id { get; set; }
    public Guid WorkflowId { get; set; }
    public string TenantId { get; set; } = "";
    public string RequesterId { get; set; } = "";
    public string RequestDigest { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public string PlanVersion { get; set; } = "v1";
    public int NextStage { get; set; }
    public int AttemptCount { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset DeadlineAt { get; set; }
    public string? FinalAnswer { get; set; }
    public string? FailureCode { get; set; }
}

public sealed class AgentExecutionCheckpoint
{
    public Guid ExecutionId { get; set; }
    public int StageNumber { get; set; }
    public Guid InvocationId { get; set; }
    public string AgentName { get; set; } = "";
    public bool Succeeded { get; set; }
    public string Output { get; set; } = "";
    public DateTimeOffset CompletedAt { get; set; }
}

public sealed class AgentApprovalState
{
    public Guid? WorkflowId { get; set; }
    public Guid Id { get; set; }
    public string TenantId { get; set; } = "";
    public string RequesterId { get; set; } = "";
    public string ToolName { get; set; } = "";
    public string PayloadDigest { get; set; } = "";
    public int Risk { get; set; }
    public int Status { get; set; }
    public string? ApproverId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}