# Aspire Azure Service Sample Solution

.NET Aspire V17 sample app testing Azure Service Bus and Azure Cosmos DB with background probe processing and city-level forecast aggregation.

## Architecture
* **AppHost:** Configures Azure Service Bus (`weather-probe-queue`) and Azure Cosmos DB (`weather-db`, container `weather-probes` with partition key `/partitionKey`) emulators and wires references/waits to services.
* **ServiceDefaults:** Holds shared resource constants (`AspireResourceNames`), contracts (`WeatherProbeQueueMessage`, `WeatherProbeDocument`, `WeatherForecast`), and partition key helpers.
* **apiservice:** Exposes `POST /weatherprobe`, `GET /weatherprobes`, and OpenAPI + Scalar UI (`/scalar`) in development.
* **backgroundworker (`ProbeWorker`):** Consumes `weather-probe-queue` via `ServiceBusProcessor`, generates 5-day forecasts over `times` iterations with a 1-second delay between iterations, and upserts documents into Cosmos DB.

## Partitioning & Resilience
* **Partition key:** `yyyy-MM-dd|p{0..3}` using `DateTimeOffset.UtcNow.UtcDateTime.Second % 4` to spread writes across 4 daily buckets and bound aggregation queries by UTC date.
* **Emulator restart recovery:** Will ignore 404 and log warning to populate data on `CosmosException` (`404 NotFound`) during `ReadNextAsync` / will recreate container on upserts.

## API Usage
* `POST /weatherprobe` — Accepts `{ "cities": ["..."], "times": 3 }` and returns `{ "probeBatchId": "..." }`.
* `GET /weatherprobes` — Optional `?city=` and `?date=` (`yyyy-MM-dd`, defaults to current UTC date); queries the 4 daily buckets and returns metrics grouped by city.
* `/scalar` — Interactive API UI (from Scalar package) in development.

## AI Disclaimer
Gemini LLM model was used to suggest and generate some parts of the code, which were manually cleaned up and simplified afterwards.