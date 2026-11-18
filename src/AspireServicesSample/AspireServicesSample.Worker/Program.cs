using AspireServicesSample.Worker;

var builder = Host.CreateApplicationBuilder(args);

// Inherit Aspire service defaults (telemetry, health checks)
builder.AddServiceDefaults();

// Register Azure SDK clients matching the logical names in AppHost
builder.AddAzureCosmosClient("sample-db");
builder.AddAzureServiceBusClient("servicebus");

// Register your background worker logic
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();