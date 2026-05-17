using Temporalio.Client;
using TemporalEngine.Shared.Contracts;
using TemporalEngine.Sport.Contracts;

var builder = WebApplication.CreateBuilder(args);

var temporalHost = builder.Configuration["Temporal:TargetHost"] ?? "localhost:7233";
var temporalNamespace = builder.Configuration["Temporal:Namespace"] ?? "default";

builder.Services.AddSingleton<ITemporalClient>(_ =>
    TemporalClient.ConnectAsync(new TemporalClientConnectOptions(temporalHost)
    {
        Namespace = temporalNamespace,
    }).GetAwaiter().GetResult());

builder.WebHost.UseUrls(builder.Configuration["Urls"] ?? "http://localhost:5001");

var app = builder.Build();

app.MapGet("/", () => "Sport API");

// Start a fixture (begins the whole flow).
app.MapPost("/fixtures", async (StartFixtureRequest req, ITemporalClient client) =>
{
    var input = new CreateFixtureInput(
        ExternalId: req.ExternalId,
        Name: req.Name,
        ScheduledKickoff: req.ScheduledKickoff,
        EventDuration: TimeSpan.FromSeconds(req.EventDurationSeconds),
        CorrelationId: req.CorrelationId);

    var handle = await client.StartWorkflowAsync(
        WorkflowNames.FixtureWorkflow,
        new object[] { input },
        new WorkflowOptions(
            id: $"fixture-{req.ExternalId}",
            taskQueue: TaskQueues.Sport)
        {
            IdReusePolicy = Temporalio.Api.Enums.V1.WorkflowIdReusePolicy.RejectDuplicate,
        });

    return Results.Ok(new { workflowId = handle.Id, firstExecutionRunId = handle.ResultRunId });
});

app.Run();

public record StartFixtureRequest(
    string ExternalId,
    string Name,
    DateTimeOffset ScheduledKickoff,
    int EventDurationSeconds,
    string CorrelationId);
