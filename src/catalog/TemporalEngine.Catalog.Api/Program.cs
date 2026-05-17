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
app.MapPost("/products/{externalFixtureId}/{athleteExternalId}/bids", async (
    string externalFixtureId, string athleteExternalId,
    BidPlacedSignal signal, ITemporalClient client) =>
{
    var handle = client.GetWorkflowHandle($"product-{externalFixtureId}-{athleteExternalId}");
    await handle.SignalAsync("BidPlaced", new object[] { signal });
    return Results.Accepted();
});

app.Run();
