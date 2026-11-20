using AspireServicesSample.ServiceDefaults;

var builder = DistributedApplication.CreateBuilder(args);

// 1. Emulators
var cosmos = builder.AddAzureCosmosDB(AspireResourceNames.CosmosConnection).RunAsEmulator();
var cosmosDb = cosmos.AddCosmosDatabase(AspireResourceNames.CosmosDatabase);

var serviceBus = builder.AddAzureServiceBus(AspireResourceNames.ServiceBusConnection).RunAsEmulator();
serviceBus.AddServiceBusQueue(AspireResourceNames.WeatherProbeQueue);

// 2. Register the Web API via AddDotnetProject (Aspire 17 path-based model)
var apiService = builder.AddDotnetProject("apiservice", "../AspireServicesSample.ApiService/AspireServicesSample.ApiService.csproj")
	.WithReference(cosmosDb)
	.WithReference(serviceBus)
    .WithHttpHealthCheck("/health")
	.WaitFor(cosmosDb)
	.WaitFor(serviceBus);

// 3. Register the Background Worker
var backgroundWorker = builder.AddDotnetProject("backgroundworker", "../AspireServicesSample.Worker/AspireServicesSample.Worker.csproj")
	.WithReference(cosmosDb)
	.WithReference(serviceBus)
	.WaitFor(cosmosDb)
	.WaitFor(serviceBus);

builder.Build().Run();
