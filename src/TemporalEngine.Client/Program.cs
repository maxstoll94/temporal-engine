using TemporalEngine;
using Temporalio.Client;

var client = await TemporalClient.ConnectAsync(new("localhost:7233"));

var name = args.Length > 0 ? args[0] : "Temporal";

var result = await client.ExecuteWorkflowAsync(
    (SayHelloWorkflow wf) => wf.RunAsync(name),
    new(id: $"say-hello-{name}-{Guid.NewGuid():N}", taskQueue: TaskQueues.SayHello));

Console.WriteLine($"Workflow result: {result}");
