using Googols.Application;
using Googols.Infrastructure;

// Googols.Worker runs Hangfire jobs out of the web process: content ingestion (PDF -> passages ->
// AI Search index), AI question drafting, weekly parent reports and XP/leaderboard snapshots.
// The API only enqueues; this process executes. Scale it independently in Azure Container Apps.
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, runHangfireServer: true);

var host = builder.Build();
host.Run();
