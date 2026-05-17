using OpenTelemetry.Trace;
using Temporalio.Client;
using Temporalio.Extensions.OpenTelemetry;
using TemporalEngine.Catalog.Contracts;

var builder = WebApplication.CreateBuilder(args);

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

app.MapGet("/", () => "Catalog API");

// Signal a bid (lands on ProductWorkflow).
// Retries on NotFound: external webhooks can arrive before the workflow has spawned
// from the parent EventWorkflow. We retry for ~5s before giving up.
app.MapPost("/products/{externalFixtureId}/{athleteExternalId}/bids", async (
    string externalFixtureId, string athleteExternalId,
    BidPlacedSignal signal, ITemporalClient client) =>
{
    var handle = client.GetWorkflowHandle($"product-{externalFixtureId}-{athleteExternalId}");
    await SignalWithRetry(() => handle.SignalAsync("BidPlaced", new object[] { signal }));
    return Results.Accepted();
});

app.Run();

static async Task SignalWithRetry(Func<Task> signal, int maxAttempts = 10, int initialDelayMs = 250)
{
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            await signal();
            return;
        }
        catch (Temporalio.Exceptions.RpcException ex)
            when (ex.Code == Temporalio.Exceptions.RpcException.StatusCode.NotFound && attempt < maxAttempts)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(initialDelayMs * attempt));
        }
    }
}
