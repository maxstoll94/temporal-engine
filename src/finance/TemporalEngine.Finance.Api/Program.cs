using Temporalio.Client;
using TemporalEngine.Finance.Contracts;

var builder = WebApplication.CreateBuilder(args);

var temporalHost = builder.Configuration["Temporal:TargetHost"] ?? "localhost:7233";
var temporalNamespace = builder.Configuration["Temporal:Namespace"] ?? "default";

builder.Services.AddSingleton<ITemporalClient>(_ =>
    TemporalClient.ConnectAsync(new TemporalClientConnectOptions(temporalHost)
    {
        Namespace = temporalNamespace,
    }).GetAwaiter().GetResult());

builder.WebHost.UseUrls(builder.Configuration["Urls"] ?? "http://localhost:5003");

var app = builder.Build();

app.MapGet("/", () => "Finance API");

// Signal a payment received (lands on OrderWorkflow).
app.MapPost("/orders/{externalFixtureId}/{athleteExternalId}/payments", async (
    string externalFixtureId, string athleteExternalId,
    PaymentReceivedSignal signal, ITemporalClient client) =>
{
    var handle = client.GetWorkflowHandle($"order-{externalFixtureId}-{athleteExternalId}");
    await handle.SignalAsync("PaymentReceived", new object[] { signal });
    return Results.Accepted();
});

// Signal a shipment update (lands on OrderWorkflow). Wired but not used by the current OrderWorkflow flow yet.
app.MapPost("/orders/{externalFixtureId}/{athleteExternalId}/shipments", async (
    string externalFixtureId, string athleteExternalId,
    ShipmentUpdatedSignal signal, ITemporalClient client) =>
{
    var handle = client.GetWorkflowHandle($"order-{externalFixtureId}-{athleteExternalId}");
    await handle.SignalAsync("ShipmentUpdated", new object[] { signal });
    return Results.Accepted();
});

app.Run();
