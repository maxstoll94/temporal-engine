namespace TemporalEngine;

using Temporalio.Workflows;

[Workflow]
public class SayHelloWorkflow
{
    [WorkflowRun]
    public async Task<string> RunAsync(string name) =>
        await Workflow.ExecuteActivityAsync(
            (MyActivities act) => act.SayHello(name),
            new() { StartToCloseTimeout = TimeSpan.FromMinutes(5) });
}
