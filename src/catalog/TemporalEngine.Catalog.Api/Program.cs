using Temporalio.Client;
using TemporalEngine.Catalog.Contracts;

var builder = WebApplication.CreateBuilder(args);

var temporalHost = builder.Configuration["Temporal:TargetHost"] ?? "localhost:7233";
var temporalNamespace = builder.Configuration["Temporal:Namespace"] ?? "default";

builder.Services.AddSingleton<ITemporalClient>(_ =>
    TemporalClient.ConnectAsync(new TemporalClientConnectOptions(temporalHost)
    {
        Namespace = temporalNamespace,
    }).GetAwaiter().GetResult());

builder.WebHost.UseUrls(builder.Configuration["Urls"] ?? "http://localhost:5002");

var app = builder.Build();

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
