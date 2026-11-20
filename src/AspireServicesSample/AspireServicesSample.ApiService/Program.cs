using AspireServicesSample.ServiceDefaults;
using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Scalar.AspNetCore;
using System.Net;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

builder.AddAzureCosmosClient(AspireResourceNames.CosmosDatabase);
builder.AddAzureServiceBusClient(AspireResourceNames.ServiceBusConnection);

// Add services to the container.
builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
	app.MapScalarApiReference(); // serves UI at /scalar
}

app.MapGet("/", () => "API service is running. Navigate to /weatherforecast to see sample data.");

app.MapGet("/weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
			Shared.Summaries[Random.Shared.Next(Shared.Summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

app.MapPost("/weatherprobe", async (
	WeatherProbeRequest request,
	ServiceBusClient serviceBusClient,
	CancellationToken cancellationToken) =>
{
	var validCities = request.Cities?
			.Where(c => !string.IsNullOrWhiteSpace(c))
			.Select(c => c.Trim())
			.ToArray() ?? [];

	var errors = new Dictionary<string, string[]>();
	if (validCities.Length == 0)
	{
		errors[nameof(request.Cities)] = ["At least one city name is required."];
	}
	if (request.Times is < 1 or > 60)
	{
		errors[nameof(request.Times)] = ["Times must be between 1 and 60."];
	}
	if (errors.Count > 0)
	{
		return Results.ValidationProblem(errors);
	}

	var probeBatchId = Guid.NewGuid();
	var payload = new WeatherProbeQueueMessage(probeBatchId, validCities, request.Times);

	await using ServiceBusSender sender = serviceBusClient.CreateSender(AspireResourceNames.WeatherProbeQueue);

	var sbMessage = new ServiceBusMessage(JsonSerializer.SerializeToUtf8Bytes(payload))
	{
		MessageId = probeBatchId.ToString(),
		ContentType = "application/json",
		Subject = nameof(WeatherProbeQueueMessage)
	};

	await sender.SendMessageAsync(sbMessage, cancellationToken);

	return Results.Accepted($"/weatherprobe/{probeBatchId}", new { probeBatchId });
})
.WithName("CreateWeatherProbe");

app.MapGet("/weatherprobes", async (
	string? city,
	DateOnly? date,
	CosmosClient cosmosClient,
	ILogger<Program> logger,
	CancellationToken cancellationToken) =>
{
	DateOnly targetDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
	string[] buckets = WeatherProbePartitionKey.AllBucketsForDate(targetDate);
	string? normalizedCity = string.IsNullOrWhiteSpace(city) ? null : city.Trim();

	Container container = cosmosClient
		.GetDatabase(AspireResourceNames.CosmosDatabase)
		.GetContainer(AspireResourceNames.WeatherProbesContainer);

	var documents = new List<WeatherProbeDocument>();

	try
	{
		foreach (string bucket in buckets)
		{
			var queryDefinition = normalizedCity is null
				? new QueryDefinition(
					"SELECT c.id, c.partitionKey, c.probeBatchId, c.city, c.forecast, c.capturedAt FROM c WHERE c.partitionKey = @pk")
					.WithParameter("@pk", bucket)
				: new QueryDefinition(
					"SELECT c.id, c.partitionKey, c.probeBatchId, c.city, c.forecast, c.capturedAt FROM c WHERE c.partitionKey = @pk AND LOWER(c.city) = LOWER(@city)")
					.WithParameter("@pk", bucket)
					.WithParameter("@city", normalizedCity);

			using FeedIterator<WeatherProbeDocument> iterator = container.GetItemQueryIterator<WeatherProbeDocument>(
				queryDefinition,
				requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(bucket) });

			while (iterator.HasMoreResults)
			{
				FeedResponse<WeatherProbeDocument> page = await iterator.ReadNextAsync(cancellationToken);
				documents.AddRange(page);
			}
		}
	}
	catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
	{
		logger.LogWarning("Container was not found - sample data should be added first.");
	}

	var groupedByCity = documents
		.GroupBy(d => d.city, StringComparer.OrdinalIgnoreCase)
		.Select(group =>
		{
			var temperatures = group
				.SelectMany(d => d.forecast ?? [])
				.Select(f => f.TemperatureC)
				.ToArray();

			return new CityWeatherSummary(
				City: group.Key,
				ForecastSampleCount: temperatures.Length,
				AverageTemperatureC: temperatures.Length > 0 ? Math.Round(temperatures.Average(), 2) : 0,
				MinTemperatureC: temperatures.Length > 0 ? temperatures.Min() : 0,
				MaxTemperatureC: temperatures.Length > 0 ? temperatures.Max() : 0,
				LatestCapturedAt: group.Max(d => (DateTimeOffset?)d.capturedAt));
		})
		.OrderBy(s => s.City)
		.ToArray();

	return Results.Ok(new WeatherProbeAggregationResponse(
		UtcDate: targetDate,
		CityFilter: normalizedCity,
		PartitionKeysQueried: buckets,
		Cities: groupedByCity));
})
.WithName("GetWeatherProbesAggregated");

app.MapDefaultEndpoints();

app.Run();

public sealed record WeatherProbeRequest(string[] Cities, int Times);

public sealed record CityWeatherSummary(
	string City,
	int ForecastSampleCount,
	double AverageTemperatureC,
	int MinTemperatureC,
	int MaxTemperatureC,
	DateTimeOffset? LatestCapturedAt);

public sealed record WeatherProbeAggregationResponse(
	DateOnly UtcDate,
	string? CityFilter,
	string[] PartitionKeysQueried,
	IReadOnlyList<CityWeatherSummary> Cities);