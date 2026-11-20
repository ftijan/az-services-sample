using AspireServicesSample.ServiceDefaults;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Cosmos;
using System.Text.Json;

namespace AspireServicesSample.Worker;

public sealed class ProbeWorker(
	ServiceBusClient serviceBusClient,
	CosmosClient cosmosClient,
	ILogger<ProbeWorker> logger) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		await using ServiceBusProcessor processor = serviceBusClient.CreateProcessor(
			AspireResourceNames.WeatherProbeQueue,
			new ServiceBusProcessorOptions
			{
				AutoCompleteMessages = false,
				MaxConcurrentCalls = 2,
				PrefetchCount = 4
			});

		processor.ProcessMessageAsync += async args =>
		{
			try
			{
				var probeMessage = JsonSerializer.Deserialize<WeatherProbeQueueMessage>(
					args.Message.Body.ToArray());

				if (probeMessage is null || probeMessage.Cities.Length == 0 || probeMessage.Times < 1)
				{
					logger.LogWarning(
						"Dead-lettering invalid WeatherProbeQueueMessage {MessageId}",
						args.Message.MessageId);

					await args.DeadLetterMessageAsync(
						args.Message,
						deadLetterReason: "InvalidProbeMessage",
						deadLetterErrorDescription: "Message body was null, had no cities, or had Times < 1.",
						cancellationToken: args.CancellationToken);
					return;
				}

				await ProcessProbeBatchAsync(probeMessage, args.CancellationToken);
				await args.CompleteMessageAsync(args.Message, args.CancellationToken);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				// Host shutting down
			}
			catch (Exception ex)
			{
				logger.LogError(
					ex,
					"Failed processing Service Bus message {MessageId}",
					args.Message.MessageId);
				throw;
			}
		};

		processor.ProcessErrorAsync += args =>
		{
			logger.LogError(
				args.Exception,
				"Service Bus processor error on {EntityPath} ({ErrorSource})",
				args.EntityPath,
				args.ErrorSource);
			return Task.CompletedTask;
		};

		await processor.StartProcessingAsync(stoppingToken);

		try
		{
			await Task.Delay(Timeout.Infinite, stoppingToken);
		}
		catch (OperationCanceledException)
		{
			// Normal shutdown
		}
		finally
		{
			await processor.StopProcessingAsync(CancellationToken.None);
		}
	}

	public async Task ProcessProbeBatchAsync(
		WeatherProbeQueueMessage message,
		CancellationToken cancellationToken)
	{
		Container container = await GetOrCreateContainerAsync(cancellationToken);

		for (int iteration = 1; iteration <= message.Times; iteration++)
		{
			DateTimeOffset capturedAt = DateTimeOffset.UtcNow;
			string partitionKey = WeatherProbePartitionKey.Create(capturedAt);

			foreach (string city in message.Cities)
			{
				WeatherForecast[] forecast = Enumerable.Range(1, 5)
					.Select(index => new WeatherForecast(
						DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
						Random.Shared.Next(-20, 55),
						Shared.Summaries[Random.Shared.Next(Shared.Summaries.Length)]))
					.ToArray();

				var document = new WeatherProbeDocument
				{
					id = $"{message.ProbeBatchId}:{iteration}:{city}",
					partitionKey = partitionKey,
					probeBatchId = message.ProbeBatchId,
					city = city,
					forecast = forecast,
					capturedAt = capturedAt
				};

				await container.UpsertItemAsync(
					document,
					new PartitionKey(document.partitionKey),
					cancellationToken: cancellationToken);
			}

			logger.LogInformation(
				"Completed probe iteration {Iteration}/{TotalTimes} for batch {ProbeBatchId} across {CityCount} cities",
				iteration,
				message.Times,
				message.ProbeBatchId,
				message.Cities.Length);

			if (iteration < message.Times)
			{
				await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
			}
		}
	}

	private async Task<Container> GetOrCreateContainerAsync(CancellationToken cancellationToken)
	{
		Database database = await cosmosClient.CreateDatabaseIfNotExistsAsync(
			AspireResourceNames.CosmosDatabase,
			cancellationToken: cancellationToken);

		return await database.CreateContainerIfNotExistsAsync(
			new ContainerProperties(
				AspireResourceNames.WeatherProbesContainer,
				partitionKeyPath: "/partitionKey"),
			cancellationToken: cancellationToken);
	}
}