using Microsoft.EntityFrameworkCore;
using NexusAgent.Application.Agents;
using NexusAgent.Application.Approvals;
using NexusAgent.Application.Workflows;
using NexusAgent.Domain.Workflows;
using NexusAgent.Infrastructure.Persistence;
using NexusAgent.Infrastructure.Persistence.Agents;

namespace NexusAgent.IntegrationTests;

public sealed class DurableAgentPersistenceTests
{
    private const string ConnectionString =
        "Host=127.0.0.1;Port=15432;Database=nexusagent;" +
        "Username=nexusagent;Password=nexusagent_dev_only";

    private static NexusAgentDbContext Open() =>
        new(new DbContextOptionsBuilder<NexusAgentDbContext>()
            .UseNpgsql(ConnectionString, options => options.UseVector())
            .Options);

    private static readonly WorkflowAccess Owner =
        new("e4-test-tenant", "e4-test-owner");

    private static readonly WorkflowAccess ForeignOwner =
        new("e4-test-tenant", "foreign-owner");

    private static readonly WorkflowAccess ForeignTenant =
        new("foreign-tenant", "e4-test-owner");

    private static async Task WithExecution(
        Func<Guid, Guid, Task> test)
    {
        var workflowId = Guid.NewGuid();
        var executionId = Guid.NewGuid();

        await using var db = Open();

        try
        {
            db.Workflows.Add(Workflow.Create(
                workflowId, Owner.RequesterId, Owner.TenantId,
                "Durable execution acceptance",
                DateTimeOffset.UtcNow));

            await db.SaveChangesAsync();

            var store = new PostgresAgentExecutionStore(db);

            await store.CreateOrGetAsync(
                executionId, workflowId, Owner,
                TimeSpan.FromMinutes(5), default);

            await test(workflowId, executionId);
        }
        finally
        {
            db.ChangeTracker.Clear();

            await db.Workflows.Where(x => x.Id == workflowId)
                .ExecuteDeleteAsync();
        }
    }

    [Fact]
    public Task CheckpointsSurviveContextRestart() =>
        WithExecution(async (workflowId, executionId) =>
        {
            Guid invocationId;

            await using (var first = Open())
            {
                var store = new PostgresAgentExecutionStore(first);

                var lease = await store.TryClaimAsync(
                    executionId, Owner, default);

                Assert.NotNull(lease);

                invocationId = BoundedAgentPlan.GetInvocationId(
                    executionId, lease.Stage);

                Assert.True(await store.CheckpointAsync(
                    lease, Owner,
                    new AgentResult(true, "Supervisor accepted."),
                    default));
            }

            await using var second = Open();
            var recovered = new PostgresAgentExecutionStore(second);

            var state = await recovered.CreateOrGetAsync(
                executionId, workflowId, Owner,
                TimeSpan.FromMinutes(5), default);

            Assert.Equal("Pending", state.Status);
            Assert.Equal(1, state.NextStage);
            Assert.Equal(0, state.AttemptCount);

            var memory = await recovered.ReadMemoryAsync(
                executionId, Owner, default);

            var checkpoint = Assert.Single(memory);

            Assert.Equal(invocationId, checkpoint.InvocationId);
            Assert.Equal("Supervisor accepted.", checkpoint.Output);

            var next = await recovered.TryClaimAsync(
                executionId, Owner, default);

            Assert.NotNull(next);
            Assert.Equal(1, next.Stage);
        });

    [Fact]
    public Task ConcurrentClaimsHaveExactlyOneWinner() =>
        WithExecution(async (_, executionId) =>
        {
            async Task<AgentExecutionLease?> Claim()
            {
                await using var db = Open();

                return await new PostgresAgentExecutionStore(db)
                    .TryClaimAsync(executionId, Owner, default);
            }

            var results = await Task.WhenAll(
                Enumerable.Range(0, 8).Select(_ => Claim()));

            Assert.Single(results, x => x is not null);
        });

    [Fact]
    public Task ExpiredLeaseIsRecoveredAndOldWriterFenced() =>
        WithExecution(async (_, executionId) =>
        {
            await using var db = Open();
            var store = new PostgresAgentExecutionStore(db);

            var oldLease = await store.TryClaimAsync(
                executionId, Owner, default);

            Assert.NotNull(oldLease);

            await db.AgentExecutions
                .Where(x => x.Id == executionId)
                .ExecuteUpdateAsync(s => s.SetProperty(
                    x => x.LeaseExpiresAt,
                    DateTimeOffset.UtcNow.AddSeconds(-1)));

            await using var restarted = Open();
            var recovered = new PostgresAgentExecutionStore(restarted);

            var newLease = await recovered.TryClaimAsync(
                executionId, Owner, default);

            Assert.NotNull(newLease);
            Assert.NotEqual(oldLease.LeaseToken, newLease.LeaseToken);
            Assert.Equal(oldLease.Stage, newLease.Stage);
            Assert.Equal(2, newLease.Attempt);

            Assert.False(await store.CheckpointAsync(
                oldLease, Owner,
                new AgentResult(true, "Stale output"), default));

            Assert.True(await recovered.CheckpointAsync(
                newLease, Owner,
                new AgentResult(true, "Recovered output"), default));

            var memory = await recovered.ReadMemoryAsync(
                executionId, Owner, default);

            Assert.Equal("Recovered output", Assert.Single(memory).Output);
        });

    [Fact]
    public Task RetryBudgetCannotBeExceeded() =>
        WithExecution(async (_, executionId) =>
        {
            await using var db = Open();
            var store = new PostgresAgentExecutionStore(db);

            for (var attempt = 1; attempt <= 3; attempt++)
            {
                var lease = await store.TryClaimAsync(
                    executionId, Owner, default);

                Assert.NotNull(lease);
                Assert.Equal(attempt, lease.Attempt);

                Assert.True(await store.ReleaseForRetryAsync(
                    lease, Owner, default));
            }

            Assert.Null(await store.TryClaimAsync(
                executionId, Owner, default));

            var state = await store.GetAsync(
                executionId, Owner, default);

            Assert.NotNull(state);
            Assert.Equal("Failed", state.Status);
            Assert.Equal("RetryBudgetExhausted", state.FailureCode);
        });

    [Fact]
    public Task CancellationFencesActiveExecution() =>
        WithExecution(async (_, executionId) =>
        {
            await using var db = Open();
            var store = new PostgresAgentExecutionStore(db);

            var lease = await store.TryClaimAsync(
                executionId, Owner, default);

            Assert.NotNull(lease);

            Assert.True(await store.CancelAsync(
                executionId, Owner, default));

            Assert.False(await store.CheckpointAsync(
                lease, Owner,
                new AgentResult(true, "Late output"), default));

            Assert.Null(await store.TryClaimAsync(
                executionId, Owner, default));

            var state = await store.GetAsync(
                executionId, Owner, default);

            Assert.NotNull(state);
            Assert.Equal("Cancelled", state.Status);
        });

    [Fact]
    public Task ForeignCallersCannotAccessExecution() =>
        WithExecution(async (workflowId, executionId) =>
        {
            await using var db = Open();
            var store = new PostgresAgentExecutionStore(db);

            foreach (var caller in new[] { ForeignOwner, ForeignTenant })
            {
                Assert.Null(await store.GetAsync(
                    executionId, caller, default));

                Assert.Null(await store.TryClaimAsync(
                    executionId, caller, default));

                Assert.Empty(await store.ReadMemoryAsync(
                    executionId, caller, default));

                Assert.False(await store.CancelAsync(
                    executionId, caller, default));

                await Assert.ThrowsAsync<UnauthorizedAccessException>(
                    () => store.CreateOrGetAsync(
                        executionId, workflowId, caller,
                        TimeSpan.FromMinutes(5), default));
            }
        });

    [Fact]
    public Task ExpiredExecutionCannotBeClaimed() =>
        WithExecution(async (_, executionId) =>
        {
            await using var db = Open();
            var store = new PostgresAgentExecutionStore(db);

            var now = DateTimeOffset.UtcNow;

            await db.AgentExecutions
                .Where(x => x.Id == executionId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.CreatedAt, now.AddMinutes(-2))
                    .SetProperty(x => x.DeadlineAt, now.AddMinutes(-1)));

            Assert.Null(await store.TryClaimAsync(
                executionId, Owner, default));

            var state = await store.GetAsync(
                executionId, Owner, default);

            Assert.NotNull(state);
            Assert.Equal("Failed", state.Status);
            Assert.Equal("ExecutionBudgetExhausted", state.FailureCode);
        });

    [Fact]
    public Task CheckpointCannotCommitAfterLeaseExpiresWhileBlocked() =>
        WithExecution(async (_, executionId) =>
        {
            await using var holder = Open();
            var store = new PostgresAgentExecutionStore(holder);

            var lease = await store.TryClaimAsync(
                executionId, Owner, default);

            Assert.NotNull(lease);

            var expires = DateTimeOffset.UtcNow.AddSeconds(3);

            await holder.AgentExecutions
                .Where(x => x.Id == executionId)
                .ExecuteUpdateAsync(s => s.SetProperty(
                    x => x.LeaseExpiresAt, expires));

            await using var transaction =
                await holder.Database.BeginTransactionAsync();

            await holder.Database.ExecuteSqlInterpolatedAsync($"""
                SELECT "Id" FROM agent_executions
                WHERE "Id" = {executionId}
                FOR UPDATE
                """);

            await using var contender = Open();
            var competingStore = new PostgresAgentExecutionStore(contender);

            var pending = competingStore.CheckpointAsync(
                lease, Owner,
                new AgentResult(true, "Expired output"), default);

            await Task.Delay(TimeSpan.FromSeconds(4));

            await transaction.CommitAsync();

            Assert.False(await pending);
            Assert.Empty(await competingStore.ReadMemoryAsync(
                executionId, Owner, default));

            var recovered = await competingStore.TryClaimAsync(
                executionId, Owner, default);

            Assert.NotNull(recovered);
            Assert.Equal(2, recovered.Attempt);
        });

    [Fact]
    public Task RetryReleaseCannotSucceedAfterLeaseExpiresWhileBlocked() =>
        WithExecution(async (_, executionId) =>
        {
            await using var holder = Open();
            var store = new PostgresAgentExecutionStore(holder);

            var lease = await store.TryClaimAsync(
                executionId, Owner, default);

            Assert.NotNull(lease);

            var expires = DateTimeOffset.UtcNow.AddSeconds(3);

            await holder.AgentExecutions
                .Where(x => x.Id == executionId)
                .ExecuteUpdateAsync(s => s.SetProperty(
                    x => x.LeaseExpiresAt, expires));

            await using var transaction =
                await holder.Database.BeginTransactionAsync();

            await holder.Database.ExecuteSqlInterpolatedAsync($"""
                SELECT "Id" FROM agent_executions
                WHERE "Id" = {executionId}
                FOR UPDATE
                """);

            await using var contender = Open();
            var competingStore = new PostgresAgentExecutionStore(contender);

            var pending = competingStore.ReleaseForRetryAsync(
                lease, Owner, default);

            await Task.Delay(TimeSpan.FromSeconds(4));
            await transaction.CommitAsync();

            Assert.False(await pending);

            var recovered = await competingStore.TryClaimAsync(
                executionId, Owner, default);

            Assert.NotNull(recovered);
            Assert.Equal(2, recovered.Attempt);
        });

    [Fact]
    public Task ExecutionRecoversAfterDatabaseClientProcessTermination() =>
        WithExecution(async (_, executionId) =>
        {
            await using var db = Open();
            var store = new PostgresAgentExecutionStore(db);

            var lease = await store.TryClaimAsync(
                executionId, Owner, default);

            Assert.NotNull(lease);

            var applicationName = "e4-crash-" + executionId.ToString("N");

            var sql = $"""
                UPDATE agent_executions
                SET "LeaseExpiresAt" = clock_timestamp() + interval '3 seconds'
                WHERE "Id" = '{executionId}';
                SELECT pg_sleep(60);
                """;

            // Separate commands ensure the lease update commits before sleep.
            var arguments =
                $"exec -e PGAPPNAME={applicationName} nexusagent-postgres " +
                "-i psql -U nexusagent -d nexusagent " +
                "-v ON_ERROR_STOP=1";

            // docker exec options must precede the container name.
            arguments =
                $"exec -i -e PGAPPNAME={applicationName} nexusagent-postgres " +
                "psql -U nexusagent -d nexusagent -v ON_ERROR_STOP=1";

            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            Assert.True(process.Start());

            try
            {
                await process.StandardInput.WriteLineAsync(sql);
                await process.StandardInput.FlushAsync();

                var ready = false;

                for (var i = 0; i < 50; i++)
                {
                    ready = await db.AgentExecutions
                        .AsNoTracking()
                        .AnyAsync(x =>
                            x.Id == executionId &&
                            x.LeaseExpiresAt < lease.ExpiresAt);

                    if (ready)
                        break;

                    await Task.Delay(100);
                }

                Assert.True(ready);

                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    SELECT pg_terminate_backend(pid)
                    FROM pg_stat_activity
                    WHERE application_name = {applicationName}
                    """);

                using var exitBudget =
                    new CancellationTokenSource(TimeSpan.FromSeconds(10));

                await process.WaitForExitAsync(exitBudget.Token);

                Assert.NotEqual(0, process.ExitCode);

                await Task.Delay(TimeSpan.FromSeconds(4));

                await using var restarted = Open();
                var recovered = new PostgresAgentExecutionStore(restarted);

                var next = await recovered.TryClaimAsync(
                    executionId, Owner, default);

                Assert.NotNull(next);
                Assert.Equal(2, next.Attempt);

                Assert.True(await recovered.CheckpointAsync(
                    next, Owner,
                    new AgentResult(true, "Recovered"), default));

                Assert.Equal("Recovered",
                    Assert.Single(await recovered.ReadMemoryAsync(
                        executionId, Owner, default)).Output);
            }
            finally
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
        });

    [Fact]
    public Task ClaimCannotStartAfterDeadlineExpiresWhileBlocked() =>
        WithExecution(async (_, executionId) =>
        {
            await using var holder = Open();

            await holder.AgentExecutions
                .Where(x => x.Id == executionId)
                .ExecuteUpdateAsync(s => s.SetProperty(
                    x => x.DeadlineAt,
                    DateTimeOffset.UtcNow.AddSeconds(3)));

            await using var transaction =
                await holder.Database.BeginTransactionAsync();

            await holder.Database.ExecuteSqlInterpolatedAsync($"""
                SELECT "Id" FROM agent_executions
                WHERE "Id" = {executionId}
                FOR UPDATE
                """);

            await using var contender = Open();
            var store = new PostgresAgentExecutionStore(contender);

            var pending = store.TryClaimAsync(
                executionId, Owner, default);

            await Task.Delay(TimeSpan.FromSeconds(4));
            await transaction.CommitAsync();

            Assert.Null(await pending);

            var state = await store.GetAsync(
                executionId, Owner, default);

            Assert.NotNull(state);
            Assert.Equal("Failed", state.Status);
            Assert.Equal("ExecutionBudgetExhausted", state.FailureCode);
        });

    [Fact]
    public Task ActualAgentProcessCrashPreservesMemoryAndRecovers() =>
        WithExecution(async (_, executionId) =>
        {
            var dll = Path.Combine(
                AppContext.BaseDirectory,
                "CrashProbe", "NexusAgent.CrashProbe.dll");

            Assert.True(File.Exists(dll));

            var start = new System.Diagnostics.ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            start.ArgumentList.Add(dll);
            start.ArgumentList.Add(executionId.ToString());
            start.ArgumentList.Add(Owner.TenantId);
            start.ArgumentList.Add(Owner.RequesterId);
            start.Environment["NEXUS_CRASH_DB"] = ConnectionString;

            using var process = System.Diagnostics.Process.Start(start)!;

            try
            {
                using var budget =
                    new CancellationTokenSource(TimeSpan.FromSeconds(30));

                Assert.Equal("CRASH_READY",
                    await process.StandardOutput.ReadLineAsync(budget.Token));

                await using var db = Open();

                var before = await db.AgentExecutions.AsNoTracking()
                    .SingleAsync(x => x.Id == executionId);

                Assert.Equal("Running", before.Status);
                Assert.Equal(1, before.NextStage);

                // Shorten the test lease, then allow natural expiry.
                await db.AgentExecutions
                    .Where(x => x.Id == executionId)
                    .ExecuteUpdateAsync(s => s.SetProperty(
                        x => x.LeaseExpiresAt,
                        DateTimeOffset.UtcNow.AddSeconds(3)));

                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(budget.Token);

                Assert.NotEqual(0, process.ExitCode);

                await Task.Delay(TimeSpan.FromSeconds(4));

                await using var restarted = Open();
                var store = new PostgresAgentExecutionStore(restarted);

                var memory = await store.ReadMemoryAsync(
                    executionId, Owner, default);

                Assert.Equal("Crash probe checkpoint",
                    Assert.Single(memory).Output);

                var recovered = await store.TryClaimAsync(
                    executionId, Owner, default);

                Assert.NotNull(recovered);
                Assert.Equal(1, recovered.Stage);
                Assert.Equal(2, recovered.Attempt);
                Assert.NotEqual(before.LeaseToken, recovered.LeaseToken);

                Assert.True(await store.CheckpointAsync(
                    recovered, Owner,
                    new AgentResult(true, "Recovered after process crash"),
                    default));

                Assert.Equal(2, (await store.ReadMemoryAsync(
                    executionId, Owner, default)).Count);
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }
        });

    private static async Task WithApproval(
        Func<Guid, ApprovalAction, Task> test)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var action = new ApprovalAction(
            Owner.TenantId, Owner.RequesterId,
            "test-tool", new string('A', 64));

        await using var db = Open();

        try
        {
            await new PostgresApprovalStore(db).CreateAsync(
                new ApprovalRequest(
                    id, action, ActionRisk.High,
                    ApprovalStatus.Pending, null,
                    now, now.AddMinutes(10)), default);

            await test(id, action);
        }
        finally
        {
            await db.AgentApprovals.Where(x => x.Id == id)
                .ExecuteDeleteAsync();
        }
    }

    [Fact]
    public Task ApprovalSurvivesRestartAndIsConsumedOnce() =>
        WithApproval(async (id, action) =>
        {
            await using (var db = Open())
            {
                var store = new PostgresApprovalStore(db);

                Assert.False(await store.DecideAsync(
                    id, action.RequesterId, true,
                    DateTimeOffset.UtcNow, default));

                Assert.True(await store.DecideAsync(
                    id, "independent-approver", true,
                    DateTimeOffset.UtcNow, default));
            }

            await using var restarted = Open();
            var recovered = new PostgresApprovalStore(restarted);

            var approval = await recovered.GetAsync(id, default);

            Assert.NotNull(approval);
            Assert.Equal(ApprovalStatus.Approved, approval.Status);

            Assert.False(await recovered.ConsumeAsync(
                id, action with { PayloadDigest = new string('B', 64) },
                DateTimeOffset.UtcNow, default));

            Assert.False(await recovered.ConsumeAsync(
                id, action with { TenantId = "foreign-tenant" },
                DateTimeOffset.UtcNow, default));

            async Task<bool> Consume()
            {
                await using var db = Open();

                return await new PostgresApprovalStore(db)
                    .ConsumeAsync(id, action,
                        DateTimeOffset.UtcNow, default);
            }

            var results = await Task.WhenAll(
                Enumerable.Range(0, 8).Select(_ => Consume()));

            Assert.Single(results, x => x);

            approval = await recovered.GetAsync(id, default);

            Assert.NotNull(approval);
            Assert.Equal(ApprovalStatus.Consumed, approval.Status);
        });

    [Fact]
    public Task ConcurrentApprovalDecisionsHaveOneWinner() =>
        WithApproval(async (id, _) =>
        {
            async Task<bool> Decide(int index)
            {
                await using var db = Open();

                return await new PostgresApprovalStore(db)
                    .DecideAsync(id, $"approver-{index}",
                        index % 2 == 0,
                        DateTimeOffset.UtcNow, default);
            }

            var results = await Task.WhenAll(
                Enumerable.Range(0, 8).Select(Decide));

            Assert.Single(results, x => x);
        });

    [Fact]
    public Task ExpiredApprovalCannotBeGranted() =>
        WithApproval(async (id, _) =>
        {
            await using var db = Open();
            var store = new PostgresApprovalStore(db);

            Assert.False(await store.DecideAsync(
                id, "independent-approver", true,
                DateTimeOffset.UtcNow.AddMinutes(11), default));

            var approval = await store.GetAsync(id, default);

            Assert.NotNull(approval);
            Assert.Equal(ApprovalStatus.Expired, approval.Status);
        });

    [Fact]
    public Task ExpiredGrantedApprovalCannotBeConsumed() =>
        WithApproval(async (id, action) =>
        {
            await using var db = Open();
            var store = new PostgresApprovalStore(db);

            Assert.True(await store.DecideAsync(
                id, "independent-approver", true,
                DateTimeOffset.UtcNow, default));

            Assert.False(await store.ConsumeAsync(
                id, action,
                DateTimeOffset.UtcNow.AddMinutes(11), default));

            var approval = await store.GetAsync(id, default);

            Assert.NotNull(approval);
            Assert.Equal(ApprovalStatus.Expired, approval.Status);
        });
}