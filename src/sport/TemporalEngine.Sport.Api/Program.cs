using OpenTelemetry.Trace;
using Temporalio.Client;
using Temporalio.Extensions.OpenTelemetry;
using TemporalEngine.Shared.Contracts;
using TemporalEngine.Sport.Contracts;

var builder = WebApplication.CreateBuilder(args);

// Aspire service defaults (OTel, health checks, resilience, service discovery).
builder.AddServiceDefaults();
builder.Services.AddOpenTelemetry().WithTracing(t => t.AddSource(TracingInterceptor.ClientSource.Name));

var temporalHost = builder.Configuration["Temporal:TargetHost"] ?? "localhost:7233";
var temporalNamespace = builder.Configuration["Temporal:Namespace"] ?? "default";

builder.Services.AddSingleton<ITemporalClient>(_ =>
    TemporalClient.ConnectAsync(new TemporalClientConnectOptions(temporalHost)
    {
        Namespace = temporalNamespace,
        Interceptors = new[] { new TracingInterceptor() },
    }).GetAwaiter().GetResult());

var app = builder.Build();
app.MapDefaultEndpoints();

app.MapGet("/", () => "Sport API");

// Start a fixture (begins the whole flow).
// Retries on transient Temporal RPC errors — handles the case where the server is
// still warming up when the request arrives.
app.MapPost("/fixtures", async (StartFixtureRequest req, ITemporalClient client) =>
{
    var input = new CreateFixtureInput(
        ExternalId: req.ExternalId,
        Name: req.Name,
        ScheduledKickoff: req.ScheduledKickoff,
        EventDuration: TimeSpan.FromSeconds(req.EventDurationSeconds),
        CorrelationId: req.CorrelationId);

    var handle = await WithRpcRetry(() => client.StartWorkflowAsync(
        WorkflowNames.FixtureWorkflow,
        new object[] { input },
        new WorkflowOptions(
            id: $"fixture-{req.ExternalId}",
            taskQueue: TaskQueues.Sport)
        {
            IdReusePolicy = Temporalio.Api.Enums.V1.WorkflowIdReusePolicy.RejectDuplicate,
        }));

    return Results.Ok(new { workflowId = handle.Id, firstExecutionRunId = handle.ResultRunId });
});

// Signal an athlete subbing in (lands on EventWorkflow).
app.MapPost("/fixtures/{externalFixtureId}/athletes", async (
    string externalFixtureId, AthleteSubbingInSignal signal, ITemporalClient client) =>
{
    var handle = client.GetWorkflowHandle($"event-{externalFixtureId}");
    await WithRpcRetry<object?>(async () => { await handle.SignalAsync("AthleteSubbingIn", new object[] { signal }); return null; });
    return Results.Accepted();
});

app.Run();

static async Task<T> WithRpcRetry<T>(Func<Task<T>> action, int maxAttempts = 10, int initialDelayMs = 250)
{
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            return await action();
        }
        catch (Temporalio.Exceptions.RpcException ex) when (attempt < maxAttempts && (
            ex.Code is Temporalio.Exceptions.RpcException.StatusCode.NotFound
                    or Temporalio.Exceptions.RpcException.StatusCode.Unavailable
                    or Temporalio.Exceptions.RpcException.StatusCode.DeadlineExceeded))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(initialDelayMs * attempt));
        }
    }
}

public record StartFixtureRequest(
    string ExternalId,
    string Name,
    DateTimeOffset ScheduledKickoff,
    int EventDurationSeconds,
    string CorrelationId);
