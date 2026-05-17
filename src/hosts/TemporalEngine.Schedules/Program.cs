using Temporalio.Client;
using Temporalio.Client.Schedules;
using Temporalio.Common;
using TemporalEngine.Shared.Contracts;
using TemporalEngine.Sport.Contracts;

// Bootstraps a Temporal Schedule that periodically starts FixtureWorkflows.
// Usage:
//   dotnet run --project src/hosts/TemporalEngine.Schedules -- apply
//   dotnet run --project src/hosts/TemporalEngine.Schedules -- delete
//
// "apply" is idempotent: it creates the schedule if missing, updates if present.

const string ScheduleId = "demo-fixture-ingest";

var command = args.FirstOrDefault() ?? "apply";
var temporalHost = Environment.GetEnvironmentVariable("TEMPORAL_TARGET_HOST") ?? "localhost:7233";
var temporalNamespace = Environment.GetEnvironmentVariable("TEMPORAL_NAMESPACE") ?? "default";

var client = await TemporalClient.ConnectAsync(new TemporalClientConnectOptions(temporalHost)
{
    Namespace = temporalNamespace,
});

switch (command)
{
    case "apply":
        await ApplyAsync();
        break;
    case "delete":
        await DeleteAsync();
        break;
    default:
        Console.Error.WriteLine($"Unknown command '{command}'. Use 'apply' or 'delete'.");
        Environment.Exit(2);
        break;
}

async Task ApplyAsync()
{
    var fixtureInput = new CreateFixtureInput(
        ExternalId: "scheduled-{{now}}",  // placeholder; we override per-run below
        Name: "Scheduled Demo Match",
        ScheduledKickoff: DateTimeOffset.UtcNow,
        EventDuration: TimeSpan.FromSeconds(30),
        CorrelationId: "scheduled");

    var action = ScheduleActionStartWorkflow.Create(
        WorkflowNames.FixtureWorkflow,
        new object[] { fixtureInput },
        new WorkflowOptions(
            id: $"fixture-scheduled-{Guid.NewGuid():N}",
            taskQueue: TaskQueues.Sport));

    var spec = new ScheduleSpec
    {
        Intervals = new[] { new ScheduleIntervalSpec(Every: TimeSpan.FromMinutes(5)) },
    };

    var schedule = new Schedule(Action: action, Spec: spec);

    try
    {
        await client.CreateScheduleAsync(ScheduleId, schedule);
        Console.WriteLine($"Created schedule '{ScheduleId}' (runs every 5 minutes).");
    }
    catch (Temporalio.Exceptions.ScheduleAlreadyRunningException)
    {
        var handle = client.GetScheduleHandle(ScheduleId);
        await handle.UpdateAsync(_ => new ScheduleUpdate(schedule));
        Console.WriteLine($"Updated existing schedule '{ScheduleId}'.");
    }
}

async Task DeleteAsync()
{
    try
    {
        await client.GetScheduleHandle(ScheduleId).DeleteAsync();
        Console.WriteLine($"Deleted schedule '{ScheduleId}'.");
    }
    catch (Temporalio.Exceptions.RpcException ex) when (ex.Code == Temporalio.Exceptions.RpcException.StatusCode.NotFound)
    {
        Console.WriteLine($"Schedule '{ScheduleId}' did not exist.");
    }
}
