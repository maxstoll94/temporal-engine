using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;
using TemporalEngine.Catalog.Data;
using TemporalEngine.Catalog.Workflows;
using TemporalEngine.Finance.Data;
using TemporalEngine.Finance.Domain;
using TemporalEngine.Finance.Workflows;
using TemporalEngine.Shared.Contracts;
using TemporalEngine.Sport.Data;
using TemporalEngine.Sport.Workflows;
using Temporalio.Extensions.Hosting;
using Temporalio.Extensions.OpenTelemetry;

var builder = WebApplication.CreateBuilder(args);

// Aspire service defaults (OTel, health checks, resilience, service discovery).
builder.AddServiceDefaults();

// Add Temporalio workflow/activity/client tracing sources on top of the defaults.
builder.Services.AddOpenTelemetry().WithTracing(t => t
    .AddSource(TracingInterceptor.ClientSource.Name)
    .AddSource(TracingInterceptor.WorkflowsSource.Name)
    .AddSource(TracingInterceptor.ActivitiesSource.Name));

var temporalHost = builder.Configuration["Temporal:TargetHost"] ?? "localhost:7233";
var temporalNamespace = builder.Configuration["Temporal:Namespace"] ?? "default";

// Comma-separated list of task queues this process should host. Default: all three.
var loadWorkers = (builder.Configuration["TemporalEngine:LoadWorkers"] ?? "sport,catalog,finance")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Select(s => s.ToLowerInvariant())
    .ToHashSet();

builder.Services.AddTemporalClient(opts =>
{
    opts.TargetHost = temporalHost;
    opts.Namespace = temporalNamespace;
    opts.Interceptors = new[] { new TracingInterceptor() };
});

// === SPORT ===
if (loadWorkers.Contains("sport"))
{
    builder.AddNpgsqlDbContext<SportDbContext>("Sport");
    builder.Services
        .AddHostedTemporalWorker(taskQueue: TaskQueues.Sport)
        .AddScopedActivities<SportActivities>()
        .AddWorkflow<FixtureWorkflow>()
        .AddWorkflow<EventWorkflow>();
}

// === CATALOG ===
if (loadWorkers.Contains("catalog"))
{
    builder.AddNpgsqlDbContext<CatalogDbContext>("Catalog");
    builder.Services
        .AddHostedTemporalWorker(taskQueue: TaskQueues.Catalog)
        .AddScopedActivities<CatalogActivities>()
        .AddWorkflow<ProductWorkflow>();
}

// === FINANCE ===
if (loadWorkers.Contains("finance"))
{
    builder.AddNpgsqlDbContext<FinanceDbContext>("Finance");
    builder.Services.AddScoped<IOdooClient, FakeOdooClient>();
    builder.Services
        .AddHostedTemporalWorker(taskQueue: TaskQueues.Finance)
        .AddScopedActivities<FinanceActivities>()
        .AddWorkflow<OrderWorkflow>();
}

var app = builder.Build();
app.MapDefaultEndpoints();

// Ensure custom search attributes exist on the namespace before the worker starts polling.
// Idempotent — failing on already-exists is fine.
using (var scope = app.Services.CreateScope())
{
    var client = scope.ServiceProvider.GetRequiredService<Temporalio.Client.ITemporalClient>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    foreach (var name in new[] { SearchAttributeNames.CorrelationId, SearchAttributeNames.Stage })
    {
        try
        {
            await client.Connection.OperatorService.AddSearchAttributesAsync(
                new Temporalio.Api.OperatorService.V1.AddSearchAttributesRequest
                {
                    Namespace = temporalNamespace,
                    SearchAttributes = { [name] = Temporalio.Api.Enums.V1.IndexedValueType.Keyword },
                });
            logger.LogInformation("Registered search attribute {Name}", name);
        }
        catch (Temporalio.Exceptions.RpcException ex) when (ex.Code == Temporalio.Exceptions.RpcException.StatusCode.AlreadyExists)
        {
            // already registered — fine
        }
    }
}

// Schema bootstrap (demo only — replace with migrations in production).
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    if (loadWorkers.Contains("sport"))
    {
        await scope.ServiceProvider.GetRequiredService<SportDbContext>().Database.EnsureCreatedAsync();
        logger.LogInformation("Sport schema ensured");
    }
    if (loadWorkers.Contains("catalog"))
    {
        await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.EnsureCreatedAsync();
        logger.LogInformation("Catalog schema ensured");
    }
    if (loadWorkers.Contains("finance"))
    {
        await scope.ServiceProvider.GetRequiredService<FinanceDbContext>().Database.EnsureCreatedAsync();
        logger.LogInformation("Finance schema ensured");
    }
}

await app.RunAsync();
