using Temporalio.Client;
using Workflow;

// Create a client to localhost on "default" namespace
var client = await TemporalClient.ConnectAsync(new("localhost:7233"));

// Run workflow
var result = await client.ExecuteWorkflowAsync(
    (SayHelloWorkflow wf) => wf.RunAsync("Temporal"),
    new(id: $"my-workflow-id-{Guid.NewGuid()}", taskQueue: "my-task-queue")
);

Console.WriteLine("Workflow result: {0}", result);