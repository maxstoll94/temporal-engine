var builder = DistributedApplication.CreateBuilder(args);

// === Temporal dev server (single-process, in-memory) ===
var temporal = builder.AddContainer("temporal", "temporalio/admin-tools")
    .WithArgs("temporal", "server", "start-dev",
        "--ip", "0.0.0.0",
        "--port", "7233",
        "--ui-port", "8233",
        "--namespace", "default")
    .WithEndpoint(port: 7233, targetPort: 7233, name: "grpc")
    .WithEndpoint(port: 8233, targetPort: 8233, name: "ui", scheme: "http");

var temporalGrpc = temporal.GetEndpoint("grpc");

// === Postgres (one instance, three databases) ===
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("temporal-engine-postgres")
    .WithPgWeb(); // optional little admin UI on its own random port

var sportDb   = postgres.AddDatabase("Sport",   "temporal_sport");
var catalogDb = postgres.AddDatabase("Catalog", "temporal_catalog");
var financeDb = postgres.AddDatabase("Finance", "temporal_finance");

// Shared env var that points every service at the same Temporal frontend.
// (Temporal client wants host:port without a scheme.)
const string temporalTargetEnv = "Temporal__TargetHost";

// === Worker (hosts all three task queues) ===
var worker = builder.AddProject<Projects.TemporalEngine_Worker>("worker")
    .WithReference(sportDb).WaitFor(sportDb)
    .WithReference(catalogDb).WaitFor(catalogDb)
    .WithReference(financeDb).WaitFor(financeDb)
    .WithEnvironment(temporalTargetEnv, $"{temporalGrpc.Property(Aspire.Hosting.ApplicationModel.EndpointProperty.Host)}:{temporalGrpc.Property(Aspire.Hosting.ApplicationModel.EndpointProperty.Port)}")
    .WaitFor(temporal);

// === Per-service APIs ===
var sportApi = builder.AddProject<Projects.TemporalEngine_Sport_Api>("sport-api")
    .WithEnvironment(temporalTargetEnv, $"{temporalGrpc.Property(Aspire.Hosting.ApplicationModel.EndpointProperty.Host)}:{temporalGrpc.Property(Aspire.Hosting.ApplicationModel.EndpointProperty.Port)}")
    .WaitFor(temporal).WaitFor(worker);

var catalogApi = builder.AddProject<Projects.TemporalEngine_Catalog_Api>("catalog-api")
    .WithEnvironment(temporalTargetEnv, $"{temporalGrpc.Property(Aspire.Hosting.ApplicationModel.EndpointProperty.Host)}:{temporalGrpc.Property(Aspire.Hosting.ApplicationModel.EndpointProperty.Port)}")
    .WaitFor(temporal).WaitFor(worker);

var financeApi = builder.AddProject<Projects.TemporalEngine_Finance_Api>("finance-api")
    .WithEnvironment(temporalTargetEnv, $"{temporalGrpc.Property(Aspire.Hosting.ApplicationModel.EndpointProperty.Host)}:{temporalGrpc.Property(Aspire.Hosting.ApplicationModel.EndpointProperty.Port)}")
    .WaitFor(temporal).WaitFor(worker);

// === Demo driver (on-demand console — not started automatically) ===
// Use the Aspire dashboard's "Start" button on the resource, or run it manually:
//   dotnet run --project src/hosts/TemporalEngine.DemoDriver
builder.AddProject<Projects.TemporalEngine_DemoDriver>("demo-driver")
    .WithEnvironment("SPORT_URL",   sportApi.GetEndpoint("http"))
    .WithEnvironment("CATALOG_URL", catalogApi.GetEndpoint("http"))
    .WithEnvironment("FINANCE_URL", financeApi.GetEndpoint("http"))
    .WithExplicitStart();

builder.Build().Run();
