using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Trace;
using TemporalEngine.Shared.Contracts;
using TemporalEngine.Sport.Data;
using TemporalEngine.Sport.Workflows;
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

builder.AddNpgsqlDbContext<SportDbContext>("Sport");

builder.Services
    .AddHostedTemporalWorker(taskQueue: TaskQueues.Sport)
    .AddScopedActivities<SportActivities>()
    .AddWorkflow<FixtureWorkflow>();

var app = builder.Build();
app.MapDefaultEndpoints();

using (var scope = app.Services.CreateScope())
{
    await SearchAttributeRegistration.EnsureAsync(
        scope.ServiceProvider.GetRequiredService<Temporalio.Client.ITemporalClient>(),
        temporalNamespace,
        scope.ServiceProvider.GetRequiredService<ILogger<Program>>());

    await scope.ServiceProvider.GetRequiredService<SportDbContext>().Database.EnsureCreatedAsync();
}

await app.RunAsync();
