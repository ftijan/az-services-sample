using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// 1. Emulators
var cosmos = builder.AddAzureCosmosDB("cosmos").RunAsEmulator();
var cosmosDb = cosmos.AddCosmosDatabase("sample-db");

var serviceBus = builder.AddAzureServiceBus("servicebus").RunAsEmulator();
serviceBus.AddServiceBusQueue("sample-commands-queue");

// 2. Register the Web API via AddDotnetProject (Aspire 17 path-based model)
var apiService = builder.AddDotnetProject("apiservice", "../AspireServicesSample.ApiService/AspireServicesSample.ApiService.csproj")
	.WithReference(cosmosDb)
	.WithReference(serviceBus)
    .WithHttpHealthCheck("/health");

builder.Build().Run();
