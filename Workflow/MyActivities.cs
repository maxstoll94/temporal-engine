using Temporalio.Activities;

namespace Workflow;

public class MyActivities
{
    [Activity]
    public string SayHello(string name) => $"Hello {name}";
}