using Microsoft.EntityFrameworkCore;
using NexusAgent.Application.Agents;
using NexusAgent.Application.Workflows;
using NexusAgent.Infrastructure.Persistence;
using NexusAgent.Infrastructure.Persistence.Agents;

var connection = Environment.GetEnvironmentVariable("NEXUS_CRASH_DB")
    ?? throw new InvalidOperationException("Missing connection.");

var id = Guid.Parse(args[0]);
var owner = new WorkflowAccess(args[1], args[2]);

await using var db = new NexusAgentDbContext(
    new DbContextOptionsBuilder<NexusAgentDbContext>()
        .UseNpgsql(connection, options => options.UseVector())
        .Options);

var store = new PostgresAgentExecutionStore(db);

var first = await store.TryClaimAsync(id, owner, default)
    ?? throw new InvalidOperationException("Initial claim failed.");

if (!await store.CheckpointAsync(
    first, owner, new AgentResult(true, "Crash probe checkpoint"), default))
    throw new InvalidOperationException("Checkpoint failed.");

var second = await store.TryClaimAsync(id, owner, default)
    ?? throw new InvalidOperationException("Second claim failed.");

Console.WriteLine("CRASH_READY");
Console.Out.Flush();

await Task.Delay(Timeout.InfiniteTimeSpan);