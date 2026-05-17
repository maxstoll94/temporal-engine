using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Trace;
using TemporalEngine.Finance.Data;
using TemporalEngine.Finance.Domain;
using TemporalEngine.Finance.Workflows;
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

builder.AddNpgsqlDbContext<FinanceDbContext>("Finance");

builder.Services.AddScoped<IOdooClient, FakeOdooClient>();

builder.Services
    .AddHostedTemporalWorker(taskQueue: TaskQueues.Finance)
    .AddScopedActivities<FinanceActivities>()
    .AddWorkflow<OrderWorkflow>();

var app = builder.Build();
app.MapDefaultEndpoints();

using (var scope = app.Services.CreateScope())
{
    await SearchAttributeRegistration.EnsureAsync(
        scope.ServiceProvider.GetRequiredService<Temporalio.Client.ITemporalClient>(),
        temporalNamespace,
        scope.ServiceProvider.GetRequiredService<ILogger<Program>>());

    await scope.ServiceProvider.GetRequiredService<FinanceDbContext>().Database.EnsureCreatedAsync();
}

await app.RunAsync();
