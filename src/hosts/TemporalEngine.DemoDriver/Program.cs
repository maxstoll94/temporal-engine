using System.Net.Http.Json;
using System.Text.Json;

// Drives the full end-to-end auction flow by calling the three per-service HTTP APIs.
//
// Usage:
//   dotnet run --project src/hosts/TemporalEngine.DemoDriver
//
// Requires: Postgres + Temporal dev server up; workers running; Sport/Catalog/Finance APIs each running.
// Under Aspire the AppHost injects SPORT_URL, CATALOG_URL, FINANCE_URL, TEMPORAL_UI_URL.

var sportUrl      = Environment.GetEnvironmentVariable("SPORT_URL")       ?? "http://localhost:5001";
var catalogUrl    = Environment.GetEnvironmentVariable("CATALOG_URL")     ?? "http://localhost:5002";
var financeUrl    = Environment.GetEnvironmentVariable("FINANCE_URL")     ?? "http://localhost:5003";
var temporalUiUrl = Environment.GetEnvironmentVariable("TEMPORAL_UI_URL") ?? "http://localhost:8233";

var externalFixtureId    = $"drv-{DateTimeOffset.UtcNow:HHmmss}";
var correlationId        = externalFixtureId;
var eventDurationSeconds = 15;

// Bid plan: one bid on the seeded starting athlete plus two on sim athletes the
// FixtureWorkflow's simulator will spawn during the live window. Each (athlete, amount, bidder)
// corresponds to one ProductWorkflow → OrderWorkflow chain we'll then pay for.
var bids = new (string Athlete, decimal Amount, string Bidder)[]
{
    ("athlete-001",     250m, "alice"),
    ("sim-athlete-100", 300m, "bob"),
    ("sim-athlete-101", 175m, "charlie"),
};

var jsonOpts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
using var http = new HttpClient();

Console.WriteLine($"== TemporalEngine demo driver  (correlation: {correlationId}) ==");

// 1. Start the fixture.
Console.WriteLine($"\nPOST {sportUrl}/fixtures");
var startResp = await http.PostAsJsonAsync($"{sportUrl}/fixtures", new
{
    externalId           = externalFixtureId,
    name                 = "Driver Demo Match",
    scheduledKickoff     = DateTimeOffset.UtcNow,
    eventDurationSeconds = eventDurationSeconds,
    correlationId        = correlationId,
}, jsonOpts);
await EnsureSuccess(startResp, "start fixture");
Console.WriteLine($"      -> fixture-{externalFixtureId} started; simulator will spawn sim-athlete-100, 101, …");

// 2. Wait briefly for athlete-001's product to spawn, then place bids in sequence.
//    Each 2s spacing lines up with the simulator's sub-in interval, so by the time
//    we bid on sim-athlete-N, the simulator has already had the catalog workflow
//    spawn the corresponding ProductWorkflow.
await Task.Delay(TimeSpan.FromSeconds(3));

foreach (var (athlete, amount, bidder) in bids)
{
    Console.WriteLine($"\nPOST {catalogUrl}/products/{externalFixtureId}/{athlete}/bids");
    var bidResp = await http.PostAsJsonAsync(
        $"{catalogUrl}/products/{externalFixtureId}/{athlete}/bids",
        new
        {
            bidId    = $"bid-{Guid.NewGuid():N}".Substring(0, 12),
            bidderId = bidder,
            amount,
            currency = "EUR",
            placedAt = DateTimeOffset.UtcNow,
        }, jsonOpts);
    await EnsureSuccess(bidResp, $"place bid on {athlete}");
    Console.WriteLine($"      -> {amount} EUR by {bidder}");
    await Task.Delay(TimeSpan.FromSeconds(2));
}

// 3. Wait for the event-end timer + order creation. Bids occupied ~3+2*N seconds; the
//    auction's scheduled end is eventDurationSeconds from kickoff. Add ~5s for the
//    auction to close, products to determine winners, and the OrderWorkflows to spawn.
var waitForOrders = Math.Max(eventDurationSeconds - (3 + 2 * bids.Length) + 5, 1);
Console.WriteLine($"\nwaiting {waitForOrders}s for event to end and orders to be created...");
await Task.Delay(TimeSpan.FromSeconds(waitForOrders));

// 4. Pay for each winning bid. Each athlete with a bid gets its own OrderWorkflow,
//    waiting on a PaymentReceived signal at order-{externalFixtureId}-{athlete}.
foreach (var (athlete, amount, _) in bids)
{
    Console.WriteLine($"\nPOST {financeUrl}/orders/{externalFixtureId}/{athlete}/payments");
    var payResp = await http.PostAsJsonAsync(
        $"{financeUrl}/orders/{externalFixtureId}/{athlete}/payments",
        new
        {
            paymentId  = $"pay-{Guid.NewGuid():N}".Substring(0, 12),
            amount,
            currency   = "EUR",
            receivedAt = DateTimeOffset.UtcNow,
        }, jsonOpts);
    await EnsureSuccess(payResp, $"pay for {athlete}");
    Console.WriteLine($"      -> payment sent for {athlete}");
}

Console.WriteLine($"\nFlow triggered end-to-end. {bids.Length} winning bids → {bids.Length} OrderWorkflows.");
Console.WriteLine($"Inspect in Temporal UI:");
Console.WriteLine($"  {temporalUiUrl}/namespaces/default/workflows?query=EngineCorrelationId%3D%22{correlationId}%22");

static async Task EnsureSuccess(HttpResponseMessage resp, string what)
{
    if (resp.IsSuccessStatusCode) return;
    var body = await resp.Content.ReadAsStringAsync();
    throw new InvalidOperationException($"Failed to {what}: HTTP {(int)resp.StatusCode}. Body: {body}");
}
