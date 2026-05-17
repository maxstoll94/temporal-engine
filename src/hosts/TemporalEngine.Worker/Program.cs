using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Resources;
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
using Temporalio.Runtime;

var builder = Host.CreateApplicationBuilder(args);

var temporalHost = builder.Configuration["Temporal:TargetHost"] ?? "localhost:7233";
var temporalNamespace = builder.Configuration["Temporal:Namespace"] ?? "default";
var otlpEndpoint = builder.Configuration["Otlp:Endpoint"] ?? "http://localhost:4317";

// === OpenTelemetry tracing ===
// One ActivitySource per "kind of work" we emit spans for. The Temporal interceptor
// emits its own spans for workflow/activity execution; we register its source here.
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("temporal-engine.worker"))
    .WithTracing(t => t
        .AddSource(TracingInterceptor.ClientSource.Name)
        .AddSource(TracingInterceptor.WorkflowsSource.Name)
        .AddSource(TracingInterceptor.ActivitiesSource.Name)
        .AddOtlpExporter(opt => opt.Endpoint = new Uri(otlpEndpoint)));

// Comma-separated list of task queues this process should host. Default: all three.
// Set TemporalEngine__LoadWorkers=sport (etc.) to run only one in a deployed pod.
var loadWorkers = (builder.Configuration["TemporalEngine:LoadWorkers"] ?? "sport,catalog,finance")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Select(s => s.ToLowerInvariant())
    .ToHashSet();

// Shared Temporal client (one connection used by all hosted workers in this process).
// TracingInterceptor wires OTel spans into the client + workers (workflow/activity execution).
builder.Services.AddTemporalClient(opts =>
{
    opts.TargetHost = temporalHost;
    opts.Namespace = temporalNamespace;
    opts.Interceptors = new[] { new TracingInterceptor() };
});

// === SPORT ===
if (loadWorkers.Contains("sport"))
{
    builder.Services.AddDbContext<SportDbContext>(o =>
        o.UseNpgsql(builder.Configuration.GetConnectionString("Sport")
            ?? throw new InvalidOperationException("ConnectionStrings:Sport not configured")));

    builder.Services
        .AddHostedTemporalWorker(taskQueue: TaskQueues.Sport)
        .AddScopedActivities<SportActivities>()
        .AddWorkflow<FixtureWorkflow>()
        .AddWorkflow<EventWorkflow>();
}

// === CATALOG ===
if (loadWorkers.Contains("catalog"))
{
    builder.Services.AddDbContext<CatalogDbContext>(o =>
        o.UseNpgsql(builder.Configuration.GetConnectionString("Catalog")
            ?? throw new InvalidOperationException("ConnectionStrings:Catalog not configured")));

    builder.Services
        .AddHostedTemporalWorker(taskQueue: TaskQueues.Catalog)
        .AddScopedActivities<CatalogActivities>()
        .AddWorkflow<ProductWorkflow>();
}

// === FINANCE ===
if (loadWorkers.Contains("finance"))
{
    builder.Services.AddDbContext<FinanceDbContext>(o =>
        o.UseNpgsql(builder.Configuration.GetConnectionString("Finance")
            ?? throw new InvalidOperationException("ConnectionStrings:Finance not configured")));

    builder.Services.AddScoped<IOdooClient, FakeOdooClient>();

    builder.Services
        .AddHostedTemporalWorker(taskQueue: TaskQueues.Finance)
        .AddScopedActivities<FinanceActivities>()
        .AddWorkflow<OrderWorkflow>();
}

var app = builder.Build();

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
