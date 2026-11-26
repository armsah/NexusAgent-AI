using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using NexusAgent.Application.Approvals;
using NexusAgent.Application.Tools;
using NexusAgent.Domain.Workflows;
using NexusAgent.Infrastructure.Persistence;
using NexusAgent.Infrastructure.Persistence.Agents;
using Xunit;

namespace NexusAgent.IntegrationTests;

public sealed class E7ToolCrashRecoveryTests
{
    private static NexusAgentDbContext Open() =>
        new(new DbContextOptionsBuilder<NexusAgentDbContext>()
            .UseNpgsql(
                "Host=127.0.0.1;Port=15432;Database=nexusagent;" +
                "Username=nexusagent;Password=nexusagent_dev_only",
                options => options.UseVector())
            .Options);

    [Fact]
    public async Task KilledToolProcessRecoversWithoutReplay()
    {
        var workflowId = Guid.NewGuid();
        var tenant = $"e7-tool-crash-{Guid.NewGuid():N}";
        var caller = new ToolCaller(tenant, "requester");
        var marker = Path.Combine(
            Path.GetTempPath(), $"{tenant}.txt");

        var probe = Path.Combine(
            AppContext.BaseDirectory,
            "ToolCrashProbe",
            "NexusAgent.ToolCrashProbe.dll");

        Assert.True(File.Exists(probe), $"Missing probe: {probe}");

        var request = new ToolRequest<string>(
            "workflow-sensitive-action", "approved-payload");

        var risk = new ExplicitActionRiskPolicy(
            new Dictionary<string, ActionRisk>
            {
                [request.ToolName] = ActionRisk.High
            });

        await using var db = Open();

        Process? process = null;
        Process? replay = null;

        try
        {
            var workflow = Workflow.Create(
                workflowId,
                caller.RequesterId,
                tenant,
                "Crash recovery acceptance",
                DateTimeOffset.UtcNow.AddMinutes(-1));

            db.Workflows.Add(workflow);
            await db.SaveChangesAsync();

            workflow.Start(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var service = new ApprovalService(
                new PostgresApprovalStore(db), risk);

            var approval = await service.RequestAsync(
                request,
                caller,
                TimeSpan.FromMinutes(10),
                workflowId: workflowId);

            Assert.True(await service.DecideAsync(
                new ApprovalDecision(
                    approval.ApprovalId,
                    "approver",
                    true,
                    tenant)));

            Process StartProbe()
            {
                var start = new ProcessStartInfo("dotnet")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                start.ArgumentList.Add(probe);
                start.ArgumentList.Add(workflowId.ToString());
                start.ArgumentList.Add(approval.ApprovalId.ToString());
                start.ArgumentList.Add(tenant);
                start.ArgumentList.Add(marker);

                return Process.Start(start)
                    ?? throw new InvalidOperationException(
                        "Cannot launch tool crash probe.");
            }

            process = StartProbe();

            var deadline = DateTimeOffset.UtcNow.AddSeconds(30);

            while (!File.Exists(marker) &&
                   !process.HasExited &&
                   DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(100);
            }

            Assert.True(File.Exists(marker),
                "Tool invocation was not observed.");

            Assert.False(process.HasExited);

            var executing = await db.Database.SqlQuery<int>($"""
                SELECT count(*)::integer AS "Value"
                FROM workflow_tool_executions
                WHERE approval_id = {approval.ApprovalId}
                  AND status = 'Executing'
                """).SingleAsync();

            Assert.Equal(1, executing);

            process.Kill(entireProcessTree: true);

            using (var wait = new CancellationTokenSource(
                TimeSpan.FromSeconds(15)))
            {
                await process.WaitForExitAsync(wait.Token);
            }

            Assert.NotEqual(0, process.ExitCode);

            // Accelerate the recovery deadline without changing
            // the production recovery threshold.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE workflow_tool_executions
                SET started_at = clock_timestamp() - interval '6 minutes'
                WHERE approval_id = {approval.ApprovalId}
                """);

            await db.Database.SqlQueryRaw<int>(
                """
                SELECT recover_workflow_tool_executions(1000) AS "Value"
                """).SingleAsync();

            var receipt = await db.Database.SqlQuery<string>($"""
                SELECT status AS "Value"
                FROM workflow_tool_executions
                WHERE approval_id = {approval.ApprovalId}
                """).SingleAsync();

            Assert.Equal("Uncertain", receipt);

            db.ChangeTracker.Clear();

            var recovered = await db.Workflows.AsNoTracking()
                .SingleAsync(x => x.Id == workflowId);

            Assert.Equal(WorkflowStatus.Failed, recovered.Status);
            Assert.Contains("reconciliation", recovered.FailureReason!);

            replay = StartProbe();

            using (var wait = new CancellationTokenSource(
                TimeSpan.FromSeconds(30)))
            {
                await replay.WaitForExitAsync(wait.Token);
            }

            Assert.Equal(20, replay.ExitCode);
            Assert.Single(File.ReadAllLines(marker));

            await db.Database.SqlQueryRaw<int>(
                """
                SELECT recover_workflow_tool_executions(1000) AS "Value"
                """).SingleAsync();

            var events = await db.Database.SqlQuery<string>($"""
                SELECT payload->>'eventType' AS "Value"
                FROM kafka_workflow_events
                WHERE workflow_id = {workflowId}
                ORDER BY sequence
                """).ToListAsync();

            Assert.Equal(
                new[]
                {
                    "STARTED",
                    "APPROVAL_REQUIRED",
                    "APPROVED",
                    "FAILED"
                },
                events);
        }
        finally
        {
            foreach (var child in new[] { process, replay })
            {
                if (child is null)
                    continue;

                if (!child.HasExited)
                {
                    child.Kill(entireProcessTree: true);
                    await child.WaitForExitAsync();
                }

                child.Dispose();
            }

            db.ChangeTracker.Clear();

            await db.AgentApprovals
                .Where(x => x.TenantId == tenant)
                .ExecuteDeleteAsync();

            await db.Workflows
                .Where(x => x.TenantId == tenant)
                .ExecuteDeleteAsync();

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM kafka_workflow_events
                WHERE tenant_id = {tenant}
                """);

            File.Delete(marker);
        }
    }
}
