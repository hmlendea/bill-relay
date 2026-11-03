# Configuration

## Configuration Schema

Bill Relay uses `Microsoft.Extensions.Configuration` with two sources (in precedence order):

1. **`appsettings.json`** — Base configuration (copied to output directory)
2. **Environment variables** — Override any setting (double-underscore `__` for nesting)

All settings are bound to strongly-typed POCOs in `BillRelay.Configuration` namespace.

## Settings Classes

### BillRelaySettings (Root)

```csharp
public sealed class BillRelaySettings
{
    public DeliverySettings Delivery { get; set; } = new();
    public ImapSettings Imap { get; set; } = new();
    public PollingSettings Polling { get; set; } = new();
    public FilterSettings Filters { get; set; } = new();
    public AttachmentSettings Attachments { get; set; } = new();
    public StorageSettings Storage { get; set; } = new();
    public RetentionSettings Retention { get; set; } = new();
}
```

### ImapSettings

| Property | JSON Key | Type | Default | Description |
|----------|----------|------|---------|-------------|
| `Server` | `server` | `string` | `""` | IMAP server hostname |
| `Port` | `port` | `int` | `993` | IMAP server port |
| `Mailbox` | `mailbox` | `string` | `"INBOX"` | Mailbox folder name |
| `Username` | `username` | `string` | `""` | IMAP username |
| `Password` | `password` | `string` | `""` | IMAP password |
| `IsSslEnabled` | `useSsl` | `bool` | `true` | Use SSL/TLS |
| `IsCertificateValidationEnabled` | `validateCertificates` | `bool` | `true` | Validate server certificate |

### PollingSettings

| Property | JSON Key | Type | Default | Description |
|----------|----------|------|---------|-------------|
| `IntervalSeconds` | `intervalSeconds` | `int` | `300` | Polling interval in seconds |
| `MaximumEmailAgeDays` | `maximumEmailAgeDays` | `int` | `30` | Max age of emails to process |
| `InitialProcessingDate` | `initialProcessingDate` | `DateTimeOffset?` | `null` | Earliest date to process (overrides age) |

### FilterSettings

| Property | JSON Key | Type | Default | Description |
|----------|----------|------|---------|-------------|
| `Providers` | `providers` | `string[]` | `[]` | Enabled provider names |
| `SenderPatterns` | `senderPatterns` | `string[]` | `[]` | Sender email patterns (unused) |
| `SubjectPatterns` | `subjectPatterns` | `string[]` | `[]` | Subject patterns (unused) |

**Note:** `SenderPatterns` and `SubjectPatterns` are defined but not currently used in message filtering logic.

### AttachmentSettings

| Property | JSON Key | Type | Default | Description |
|----------|----------|------|---------|-------------|
| `PermittedMimeTypes` | `permittedMimeTypes` | `string[]` | `["application/pdf"]` | Allowed MIME types |
| `MaximumSizeBytes` | `maxSizeBytes` | `long` | `25_000_000` | Max attachment size (25 MB) |
| `DownloadTimeoutSeconds` | `downloadTimeoutSeconds` | `int` | `30` | HTTP download timeout for E.ON |

### StorageSettings

| Property | JSON Key | Type | Default | Description |
|----------|----------|------|---------|-------------|
| `DownloadDirectory` | `downloadDirectory` | `string` | `"./bills"` | Directory for saved PDFs |
| `StateFilePath` | `stateFilePath` | `string` | `"./state/billrelay.json"` | Path to processed attachments JSON |

### RetentionSettings

| Property | JSON Key | Type | Default | Description |
|----------|----------|------|---------|-------------|
| `MaximumAgeDays` | `maxAgeDays` | `int` | `365` | Max age of state records |
| `MaximumFiles` | `maxFiles` | `int` | `10000` | Max number of state records |

### DeliverySettings

| Property | JSON Key | Type | Default | Description |
|----------|----------|------|---------|-------------|
| `Recipient` | `recipient` | `string` | `""` | Email recipient address |
| `SenderName` | `senderName` | `string` | `"BillRelay"` | Sender display name |
| `TemplatePath` | `templatePath` | `string` | `"email-template.xml"` | Path to email template |

### NuciNotificationsSettings (Validated at Startup)

| Property | JSON Key | Type | Required | Description |
|----------|----------|------|----------|-------------|
| `BaseUrl` | `baseUrl` | `string` | Yes | HTTPS base URL for API |
| `ApiKey` | `apiKey` | `string` | Yes | API key for authentication |
| `HmacSharedSecretKey` | `hmacSharedSecretKey` | `string` | Yes | HMAC signing secret |

Validation in `Program.ValidateNotifications()`:
- `BaseUrl` must be absolute HTTPS URL
- `ApiKey` and `HmacSharedSecretKey` must be non-empty

### NuciLoggerSettings

| Property | JSON Key | Type | Default | Description |
|----------|----------|------|---------|-------------|
| `MinimumLevel` | `minimumLevel` | `string` | `"Debug"` | Minimum log level |
| `LogFilePath` | `logFilePath` | `string` | `"logfile.log"` | Log file path |
| `IsFileOutputEnabled` | `isFileOutputEnabled` | `bool` | `true` | Enable file logging |

## Environment Variable Overrides

Use double underscore for nesting: `BillRelay__Imap__Server=imap.example.com`

Examples:
```bash
BILLRELAY__IMAP__SERVER=imap.gmail.com
BILLRELAY__IMAP__USERNAME=user@gmail.com
BILLRELAY__IMAP__PASSWORD=app-password
BILLRELAY__NUCINOTIFICATIONSSETTINGS__BASEURL=https://api.example.com
BILLRELAY__NUCINOTIFICATIONSSETTINGS__APIKEY=key
BILLRELAY__NUCINOTIFICATIONSSETTINGS__HMACSHAREDSECRETKEY=secret
BILLRELAY__DELIVERY__RECIPIENT=recipient@example.com
BILLRELAY__STORAGE__DOWNLOADDIRECTORY=/data/bills
BILLRELAY__STORAGE__STATEFILEPATH=/data/state/billrelay.json
```

## Configuration Loading

In `Program.cs`:
```csharp
builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddEnvironmentVariables();
```

- `optional: false` — File must exist
- `reloadOnChange: false` — No hot reload (worker restart required)
- Base path set to `AppContext.BaseDirectory` (output directory)

## Validation

| Setting | Validation | Location |
|---------|------------|----------|
| `Polling.IntervalSeconds` | > 0 | `BillRelayWorker.ExecuteAsync` |
| `Polling.MaximumEmailAgeDays` | > 0 | `EmailAgePolicy.GetCutoff` |
| `Imap.Server` | Non-empty | Implicit (connection fails) |
| `Imap.Username` | Non-empty | Implicit (auth fails) |
| `Imap.Password` | Non-empty | Implicit (auth fails) |
| `Delivery.Recipient` | Valid email | `BillSender.ValidateConfiguration` |
| `Delivery.TemplatePath` | File exists, valid XML | `BillSender.ValidateConfiguration` |
| `NuciNotifications.BaseUrl` | Absolute HTTPS | `Program.ValidateNotifications` |
| `NuciNotifications.ApiKey` | Non-empty | `Program.ValidateNotifications` |
| `NuciNotifications.HmacSharedSecretKey` | Non-empty | `Program.ValidateNotifications` |

## Template File

`email-template.xml` (copied to output directory):
```xml
<EmailTemplate>
  <Subject>New bill from {ProviderName}</Subject>
  <Body><![CDATA[Your bill from {ProviderName} is attached.]]></Body>
</EmailTemplate>
```

Placeholders:
- `{ProviderName}` — Replaced with identified provider name

## Example Complete Configuration

```json
{
  "delivery": {
    "recipient": "user@example.com",
    "senderName": "BillRelay",
    "templatePath": "email-template.xml"
  },
  "nuciNotificationsSettings": {
    "baseUrl": "https://notifications.example.com",
    "apiKey": "api-key-here",
    "hmacSharedSecretKey": "hmac-secret-here"
  },
  "imap": {
    "server": "imap.gmail.com",
    "port": 993,
    "mailbox": "INBOX",
    "username": "user@gmail.com",
    "password": "app-password",
    "useSsl": true,
    "validateCertificates": true
  },
  "polling": {
    "maximumEmailAgeDays": 21,
    "intervalSeconds": 300,
    "initialProcessingDate": null
  },
  "filters": {
    "providers": ["E.ON", "Vodafone", "Electrica Furnizare", "E-bloc", "Hidroelectrica"],
    "senderPatterns": ["billing@", "invoices@", "noreply@"],
    "subjectPatterns": ["invoice", "bill", "statement", "payment"]
  },
  "attachments": {
    "permittedMimeTypes": ["application/pdf"],
    "maxSizeBytes": 25000000,
    "downloadTimeoutSeconds": 30
  },
  "storage": {
    "downloadDirectory": "./bills",
    "stateFilePath": "./state/billrelay.json"
  },
  "retention": {
    "maxAgeDays": 365,
    "maxFiles": 10000
  },
  "nuciLoggerSettings": {
    "minimumLevel": "Debug",
    "logFilePath": "logfile.log",
    "isFileOutputEnabled": true
  }
}
```