using System.Net.Http.Json;
using System.Text.Json;

// Drives the full end-to-end auction flow by calling the three per-service HTTP APIs.
//
// Usage:
//   dotnet run --project src/hosts/TemporalEngine.DemoDriver
//
// Requires: Postgres + Temporal dev server up; worker running; Sport/Catalog/Finance APIs each running.

var sportUrl   = Environment.GetEnvironmentVariable("SPORT_URL")   ?? "http://localhost:5001";
var catalogUrl = Environment.GetEnvironmentVariable("CATALOG_URL") ?? "http://localhost:5002";
var financeUrl = Environment.GetEnvironmentVariable("FINANCE_URL") ?? "http://localhost:5003";

var externalFixtureId   = $"drv-{DateTimeOffset.UtcNow:HHmmss}";
var athleteExternalId   = "athlete-001"; // matches the default starting athlete seeded in FixtureWorkflow
var correlationId       = externalFixtureId;
var eventDurationSeconds = 15;

var jsonOpts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
using var http = new HttpClient();

Console.WriteLine($"== TemporalEngine demo driver  (correlation: {correlationId}) ==");

// 1. Start the fixture
Console.WriteLine($"\n[1/3] POST {sportUrl}/fixtures");
var startResp = await http.PostAsJsonAsync($"{sportUrl}/fixtures", new
{
    externalId           = externalFixtureId,
    name                 = "Driver Demo Match",
    scheduledKickoff     = DateTimeOffset.UtcNow,
    eventDurationSeconds = eventDurationSeconds,
    correlationId        = correlationId,
}, jsonOpts);
await EnsureSuccess(startResp, "start fixture");
Console.WriteLine($"      -> started. Workflow ids: fixture-{externalFixtureId}, event-{externalFixtureId}, product-{externalFixtureId}-{athleteExternalId}");

// 2. Wait briefly for the EventWorkflow + ProductWorkflow to come up, then place a bid.
//    The catalog signal endpoint also retries internally on NotFound for ~5s, so even
//    if we race ahead of the chain, the bid will land as soon as the product spawns.
await Task.Delay(TimeSpan.FromSeconds(2));

Console.WriteLine($"\n[2/3] POST {catalogUrl}/products/{externalFixtureId}/{athleteExternalId}/bids");
var bidResp = await http.PostAsJsonAsync(
    $"{catalogUrl}/products/{externalFixtureId}/{athleteExternalId}/bids",
    new
    {
        bidId     = $"bid-{Guid.NewGuid():N}".Substring(0, 12),
        bidderId  = "alice",
        amount    = 250.00m,
        currency  = "EUR",
        placedAt  = DateTimeOffset.UtcNow,
    }, jsonOpts);
await EnsureSuccess(bidResp, "place bid");
Console.WriteLine($"      -> bid placed: 250.00 EUR by alice");

// 3. Wait for the event-end timer to fire and the OrderWorkflow to be created, then pay.
Console.WriteLine($"\n      waiting {eventDurationSeconds + 4}s for event to end + order to be created...");
await Task.Delay(TimeSpan.FromSeconds(eventDurationSeconds + 4));

Console.WriteLine($"\n[3/3] POST {financeUrl}/orders/{externalFixtureId}/{athleteExternalId}/payments");
var payResp = await http.PostAsJsonAsync(
    $"{financeUrl}/orders/{externalFixtureId}/{athleteExternalId}/payments",
    new
    {
        paymentId  = $"pay-{Guid.NewGuid():N}".Substring(0, 12),
        amount     = 250.00m,
        currency   = "EUR",
        receivedAt = DateTimeOffset.UtcNow,
    }, jsonOpts);
await EnsureSuccess(payResp, "send payment");
Console.WriteLine($"      -> payment sent");

Console.WriteLine($"\nFlow triggered end-to-end.");
Console.WriteLine($"Inspect in Temporal UI:");
Console.WriteLine($"  http://localhost:8233/namespaces/default/workflows?query=EngineCorrelationId%3D%22{correlationId}%22");
Console.WriteLine($"Inspect in Jaeger:");
Console.WriteLine($"  http://localhost:16686/search?service=temporal-engine.worker");

static async Task EnsureSuccess(HttpResponseMessage resp, string what)
{
    if (resp.IsSuccessStatusCode) return;
    var body = await resp.Content.ReadAsStringAsync();
    throw new InvalidOperationException($"Failed to {what}: HTTP {(int)resp.StatusCode}. Body: {body}");
}
