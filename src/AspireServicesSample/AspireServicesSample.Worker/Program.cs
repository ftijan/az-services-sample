using AspireServicesSample.ServiceDefaults;
using AspireServicesSample.Worker;

var builder = Host.CreateApplicationBuilder(args);

// Inherit Aspire service defaults (telemetry, health checks)
builder.AddServiceDefaults();

// Register Azure SDK clients matching the logical names in AppHost
builder.AddAzureCosmosClient(AspireResourceNames.CosmosDatabase);
builder.AddAzureServiceBusClient(AspireResourceNames.ServiceBusConnection);

// Register your background worker logic
builder.Services.AddHostedService<ProbeWorker>();

var host = builder.Build();
host.Run();