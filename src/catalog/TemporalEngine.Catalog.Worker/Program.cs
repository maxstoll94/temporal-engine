using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Trace;
using TemporalEngine.Catalog.Data;
using TemporalEngine.Catalog.Workflows;
using TemporalEngine.Shared.Contracts;
using Temporalio.Extensions.Hosting;
using Temporalio.Extensions.OpenTelemetry;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddOpenTelemetry().WithTracing(t => t
    .AddSource(TracingInterceptor.ClientSource.Name)
    .AddSource(TracingInterceptor.WorkflowsSource.Name)
    .AddSource(TracingInterceptor.ActivitiesSource.Name));

var temporalHost = builder.Configuration["Temporal:TargetHost"] ?? "localhost:7233";
var temporalNamespace = builder.Configuration["Temporal:Namespace"] ?? "default";

builder.Services.AddTemporalClient(opts =>
{
    opts.TargetHost = temporalHost;
    opts.Namespace = temporalNamespace;
    opts.Interceptors = new[] { new TracingInterceptor() };
});

builder.AddNpgsqlDbContext<CatalogDbContext>("Catalog");

builder.Services
    .AddHostedTemporalWorker(taskQueue: TaskQueues.Catalog)
    .AddScopedActivities<CatalogActivities>()
    .AddWorkflow<ProductWorkflow>();

var app = builder.Build();
app.MapDefaultEndpoints();

using (var scope = app.Services.CreateScope())
{
    await SearchAttributeRegistration.EnsureAsync(
        scope.ServiceProvider.GetRequiredService<Temporalio.Client.ITemporalClient>(),
        temporalNamespace,
        scope.ServiceProvider.GetRequiredService<ILogger<Program>>());

    await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.EnsureCreatedAsync();
}

await app.RunAsync();
