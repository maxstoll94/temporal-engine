namespace TemporalEngine;

using Microsoft.Extensions.Logging;
using Temporalio.Activities;

public class MyActivities
{
    private readonly ILogger<MyActivities> _logger;

    public MyActivities(ILogger<MyActivities> logger) => _logger = logger;

    [Activity]
    public string SayHello(string name)
    {
        _logger.LogInformation("SayHello activity invoked with name={Name}", name);
        return $"Hello, {name}!";
    }
}
