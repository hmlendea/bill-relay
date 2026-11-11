# Host and Composition

## Overview

`Program.cs` is the composition root. Configures host, loads/validates configuration, registers services, sets up logging.

**File:** `BillRelay/Program.cs`

## Responsibilities

- Build `IHost` with Generic Host
- Load configuration (JSON + environment variables)
- Validate required settings at startup
- Register DI services (singletons)
- Configure NuciLog structured logging
- Start `BillRelayWorker` as hosted service

## Startup Sequence

```csharp
// 1. Create host builder
var builder = Host.CreateApplicationBuilder(args);

// 2. Configuration
builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddEnvironmentVariables(prefix: "BILLRELAY__");

// 3. Bind and validate settings
var billRelaySettings = builder.Configuration.Get<BillRelaySettings>();
var nuciSettings = builder.Configuration.GetSection("NuciNotificationsSettings").Get<NuciNotificationsSettings>();

ValidateSettings(billRelaySettings, nuciSettings);  // Throws on invalid

// 4. Register settings as Options
builder.Services.Configure<BillRelaySettings>(builder.Configuration);
builder.Services.Configure<NuciNotificationsSettings>(builder.Configuration.GetSection("NuciNotificationsSettings"));

// 5. Register services
RegisterServices(builder.Services);

// 6. Configure NuciLog
ConfigureLogging(builder.Services, builder.Configuration);

// 7. Build and run
var host = builder.Build();
await host.RunAsync();
```

## Configuration Validation

```csharp
private static void ValidateSettings(BillRelaySettings settings, NuciNotificationsSettings nuciSettings)
{
    // NuciNotifications
    if (!nuciSettings.BaseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("NuciNotifications BaseUrl must use HTTPS");
    if (string.IsNullOrWhiteSpace(nuciSettings.ApiKey))
        throw new InvalidOperationException("NuciNotifications ApiKey is required");
    if (string.IsNullOrWhiteSpace(nuciSettings.HmacSharedSecretKey))
        throw new InvalidOperationException("NuciNotifications HmacSharedSecretKey is required");

    // Storage directories
    if (string.IsNullOrWhiteSpace(settings.Storage.DownloadDirectory))
        throw new InvalidOperationException("Storage DownloadDirectory is required");
    if (string.IsNullOrWhiteSpace(settings.Storage.StateFilePath))
        throw new InvalidOperationException("Storage StateFilePath is required");

    // Delivery
    if (string.IsNullOrWhiteSpace(settings.Delivery.Recipient))
        throw new InvalidOperationException("Delivery Recipient is required");
}
```

**Fail-Fast:** Invalid config → exception → process exits → systemd restarts → alert.

## Service Registration

```csharp
private static void RegisterServices(IServiceCollection services)
{
    // State & Storage
    services.AddSingleton<IStateStore, FileStateStore>();

    // Processing Pipeline
    services.AddSingleton<IAttachmentProcessor, AttachmentProcessor>();
    services.AddSingleton<IAttachmentValidator, AttachmentValidator>();
    services.AddSingleton<IBillDelivery, BillDelivery>();
    services.AddSingleton<IBillSender, BillSender>();

    // HTTP Client for E.ON downloads (IHttpClientFactory; timeout applied per call)
    services.AddHttpClient();
    services.AddSingleton<IEOnBillDownloader, EOnBillDownloader>();

    // Worker
    services.AddHostedService<BillRelayWorker>();
}
```

**All Singletons:** Single-threaded worker → no concurrency issues.

## NuciLog Configuration

```csharp
private static void ConfigureLogging(IServiceCollection services, IConfiguration configuration)
{
    services.AddLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddNuciLog(options =>
        {
            options.MinimumLevel = LogLevel.Information;
            options.OperationTypes = [typeof(BillRelayOperation)];
            options.InfoKeyTypes = [typeof(BillRelayInfoKey)];
        });
    });
}
```

**Structured Logging:**
- Operations: `BillRelayOperation` enum (8 values)
- Info Keys: `BillRelayInfoKey` static class (20 keys)
- Output: Console (systemd journal)

## Settings Classes

All in `BillRelay/Configuration/`:

| Class | Section | Key Properties |
|-------|---------|----------------|
| `BillRelaySettings` | Root | `Imap`, `Polling`, `Filter`, `Attachments`, `Storage`, `Retention`, `Delivery` |
| `ImapSettings` | `Imap` | `Server`, `Port`, `Username`, `Password`, `UseSsl`, `ConnectionTimeoutSeconds` |
| `PollingSettings` | `Polling` | `PollingIntervalMinutes` |
| `FilterSettings` | `Filter` | `InitialProcessingDate` |
| `AttachmentSettings` | `Attachments` | `PermittedMimeTypes`, `MaximumSizeBytes` |
| `StorageSettings` | `Storage` | `DownloadDirectory`, `StateFilePath` |
| `RetentionSettings` | `Retention` | `MaximumAgeDays`, `MaximumCount` |
| `DeliverySettings` | `Delivery` | `Recipient` |
| `NuciNotificationsSettings` | `NuciNotificationsSettings` | `BaseUrl`, `ApiKey`, `HmacSharedSecretKey` |

## Environment Variable Mapping

Prefix: `BILLRELAY__` (double underscore)

| Setting | Env Var |
|---------|---------|
| `Imap:Password` | `BILLRELAY__IMAP__PASSWORD` |
| `NuciNotificationsSettings:ApiKey` | `BILLRELAY__NUCINOTIFICATIONSSETTINGS__APIKEY` |
| `Storage:DownloadDirectory` | `BILLRELAY__STORAGE__DOWNLOADDIRECTORY` |
| `Polling:PollingIntervalMinutes` | `BILLRELAY__POLLING__POLLINGINTERVALMINUTES` |

## Logging Output Example

```
info: BillRelay.Program[0] Starting Bill Relay v1.0.0
info: BillRelay.Program[0] Configuration loaded
info: BillRelay.Worker[0] PollInbox Start
info: BillRelay.Worker[0] ConnectImap Success Server=imap.example.com DurationMs=1250
info: BillRelay.Worker[0] ProcessMessage Success MessageUid=42 ProviderName=Vodafone AttachmentCount=1
info: BillRelay.Worker[0] SaveAttachment Success FilePath=/bills/abc123.pdf AttachmentId=sha256:abc123 DurationMs=45
info: BillRelay.Worker[0] SendEmail Success Recipient=user@example.com AttachmentId=sha256:abc123 DurationMs=890
info: BillRelay.Worker[0] SaveCheckpoint Success LastProcessedUid=42 DurationMs=12
info: BillRelay.Worker[0] PollInbox Success MessageCount=1 DurationMs=2340
```

## Testing

**Integration Tests:** `BillRelaySettingsTests` (IntegrationTests)
- JSON binding
- Environment variable overrides
- Validation logic

## Related Documentation

- [Configuration](../configuration.md) — All settings reference
- [BillRelayWorker](../components/bill-relay-worker.md) — Hosted service
- [Logging](../logging.md) — NuciLog setup
- [Security](../security.md#configuration-security) — Secrets handling
- [Architecture: Composition Root](../architecture.md#composition-root)