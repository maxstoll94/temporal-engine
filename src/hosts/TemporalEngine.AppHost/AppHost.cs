using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

// === Temporal dev server (single-process, in-memory) ===
var temporal = builder.AddContainer("temporal", "temporalio/admin-tools")
    .WithArgs("temporal", "server", "start-dev",
        "--ip", "0.0.0.0",
        "--port", "7233",
        "--ui-port", "8233",
        "--namespace", "default")
    .WithEndpoint(targetPort: 7233, name: "grpc")
    .WithEndpoint(targetPort: 8233, name: "ui", scheme: "http");

var temporalGrpc = temporal.GetEndpoint("grpc");

// Temporal client wants host:port without a scheme. ReferenceExpression defers resolution
// until the host process starts, so the dynamically-assigned Aspire port gets substituted.
var temporalTarget = ReferenceExpression.Create(
    $"{temporalGrpc.Property(EndpointProperty.Host)}:{temporalGrpc.Property(EndpointProperty.Port)}");

// === Postgres (one instance, three databases) ===
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("temporal-engine-postgres")
    .WithPgWeb(); // optional little admin UI on its own random port

var sportDb   = postgres.AddDatabase("Sport",   "temporal_sport");
var catalogDb = postgres.AddDatabase("Catalog", "temporal_catalog");
var financeDb = postgres.AddDatabase("Finance", "temporal_finance");

const string temporalTargetEnv = "Temporal__TargetHost";

// === Worker (hosts all three task queues) ===
var worker = builder.AddProject<Projects.TemporalEngine_Worker>("worker")
    .WithReference(sportDb).WaitFor(sportDb)
    .WithReference(catalogDb).WaitFor(catalogDb)
    .WithReference(financeDb).WaitFor(financeDb)
    .WithEnvironment(temporalTargetEnv, temporalTarget)
    .WaitFor(temporal);

// === Per-service APIs ===
var sportApi = builder.AddProject<Projects.TemporalEngine_Sport_Api>("sport-api")
    .WithEnvironment(temporalTargetEnv, temporalTarget)
    .WaitFor(temporal).WaitFor(worker);

var catalogApi = builder.AddProject<Projects.TemporalEngine_Catalog_Api>("catalog-api")
    .WithEnvironment(temporalTargetEnv, temporalTarget)
    .WaitFor(temporal).WaitFor(worker);

var financeApi = builder.AddProject<Projects.TemporalEngine_Finance_Api>("finance-api")
    .WithEnvironment(temporalTargetEnv, temporalTarget)
    .WaitFor(temporal).WaitFor(worker);

// === Demo driver (on-demand console — not started automatically) ===
// Use the Aspire dashboard's "Start" button on the resource, or run it manually:
//   dotnet run --project src/hosts/TemporalEngine.DemoDriver
builder.AddProject<Projects.TemporalEngine_DemoDriver>("demo-driver")
    .WithEnvironment("SPORT_URL",       sportApi.GetEndpoint("http"))
    .WithEnvironment("CATALOG_URL",     catalogApi.GetEndpoint("http"))
    .WithEnvironment("FINANCE_URL",     financeApi.GetEndpoint("http"))
    .WithEnvironment("TEMPORAL_UI_URL", temporal.GetEndpoint("ui"))
    .WithExplicitStart();

builder.Build().Run();
