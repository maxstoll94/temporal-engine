using TemporalEngine;
using Microsoft.Extensions.Logging;
using Temporalio.Client;
using Temporalio.Worker;

using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole().SetMinimumLevel(LogLevel.Information));

var client = await TemporalClient.ConnectAsync(new("localhost:7233")
{
    LoggerFactory = loggerFactory,
});

var activities = new MyActivities(loggerFactory.CreateLogger<MyActivities>());

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

using var worker = new TemporalWorker(
    client,
    new TemporalWorkerOptions(TaskQueues.SayHello)
        .AddActivity(activities.SayHello)
        .AddWorkflow<SayHelloWorkflow>());

try
{
    await worker.ExecuteAsync(cts.Token);
}
catch (OperationCanceledException)
{
    // Clean Ctrl+C exit.
}
