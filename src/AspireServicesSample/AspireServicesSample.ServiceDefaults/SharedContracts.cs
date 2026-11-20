namespace AspireServicesSample.ServiceDefaults;
public sealed record WeatherProbeQueueMessage(
	Guid ProbeBatchId,
	string[] Cities,
	int Times);

public record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
	public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
};

public sealed class WeatherProbeDocument
{
	public string id { get; set; } = default!;
	public string partitionKey { get; set; } = default!;
	public Guid probeBatchId { get; set; }
	public string city { get; set; } = default!;
	public WeatherForecast[] forecast { get; set; } = [];
	public DateTimeOffset capturedAt { get; set; }
}

// Naive partition strategy, for testing aggregation approach
public static class WeatherProbePartitionKey
{
	public static string Create(DateTimeOffset timestamp)
	{
		DateTime utc = timestamp.UtcDateTime;
		int bucket = utc.Second % 4;
		return $"{utc:yyyy-MM-dd}|p{bucket}";
	}

	public static string[] AllBucketsForDate(DateOnly utcDate) =>
		[.. Enumerable.Range(0, 4).Select(b => $"{utcDate:yyyy-MM-dd}|p{b}")];
}

public static class Shared
{
	public static string[] Summaries = ["Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"];
}