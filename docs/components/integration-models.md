# Integration Models

## Overview

External types and DTOs used for integration with NuciNotifications, IMAP, and file system.

## NuciNotifications DTOs

### NotificationRequest
```csharp
// NuciNotifications.Client
public class NotificationRequest
{
    public string To { get; set; }
    public string Subject { get; set; }
    public string Body { get; set; }
    public bool IsHtml { get; set; }
    public List<AttachmentDto> Attachments { get; set; }
}
```

### AttachmentDto
```csharp
public class AttachmentDto
{
    public string FileName { get; set; }
    public string ContentBase64 { get; set; }
    public string MimeType { get; set; }
}
```

### NotificationResponse
```csharp
public class NotificationResponse
{
    public bool Success { get; set; }
    public string MessageId { get; set; }
    public string Error { get; set; }
}
```

## IMAP Models (MailKit)

### ImapClient
- `ConnectAsync(host, port, useSsl, cancellationToken)`
- `AuthenticateAsync(username, password, cancellationToken)`
- `Inbox` → `IMailFolder`
- `DisconnectAsync(quit, cancellationToken)`

### IMailFolder
- `OpenAsync(FolderAccess, cancellationToken)`
- `SearchAsync(query, cancellationToken)` → `IList<UniqueId>`
- `GetMessageAsync(uid, cancellationToken)` → `MimeMessage`

### MimeMessage
- `MessageId`, `Subject`, `From`, `To`, `Date`
- `Body` → `MimeEntity` (multipart/alternative, etc.)
- `Attachments` → `IEnumerable<MimePart>`

### MimePart
- `ContentType` → `ContentType` (MediaType, Name)
- `FileName`
- `Content` → `MimeContent` (Stream)
- `ContentDisposition`

## File System Models

### ProcessedAttachmentDataObject
```csharp
// BillRelay/DataAccess/DataObjects/ProcessedAttachmentDataObject.cs
public class ProcessedAttachmentDataObject
{
    public string AttachmentId { get; set; }      // "sha256:..." or "delivery:sha256:..."
    public string FileName { get; set; }
    public string ProviderName { get; set; }
    public DateTimeOffset ProcessedDate { get; set; }
    public long FileSizeBytes { get; set; }
    public string MimeType { get; set; }
}
```

### ImapCheckpoint
```csharp
// BillRelay/Services/ImapCheckpoint.cs
public class ImapCheckpoint
{
    public uint UidValidity { get; set; }
    public uint LastProcessedUid { get; set; }
    public DateTimeOffset LastProcessedDate { get; set; }
}
```

## Configuration Models

All in `BillRelay/Configuration/`:

| Class | JSON Section |
|-------|--------------|
| `BillRelaySettings` | Root |
| `ImapSettings` | `Imap` |
| `PollingSettings` | `Polling` |
| `FilterSettings` | `Filter` |
| `AttachmentSettings` | `Attachments` |
| `StorageSettings` | `Storage` |
| `RetentionSettings` | `Retention` |
| `DeliverySettings` | `Delivery` |
| `NuciNotificationsSettings` | `NuciNotificationsSettings` |

## Provider Identification Models

```csharp
// BillRelay/Services/BillProviderIdentification.cs
public enum BillProvider
{
    Unknown,
    Vodafone,
    ElectricaFurnizare,
    Ebloc,
    Hidroelectrica,
    EOn
}
```

## Logging Models

### BillRelayOperation
```csharp
// BillRelay/Logging/BillRelayOperation.cs
public static class BillRelayOperation
{
    public static readonly Operation Startup = new("Startup");
    public static readonly Operation Shutdown = new("Shutdown");
    public static readonly Operation PollInbox = new("PollInbox");
    public static readonly Operation ConnectImap = new("ConnectImap");
    public static readonly Operation ProcessMessage = new("ProcessMessage");
    public static readonly Operation DownloadAttachment = new("DownloadAttachment");
    public static readonly Operation SaveAttachment = new("SaveAttachment");
    public static readonly Operation SendEmail = new("SendEmail");
    public static readonly Operation SaveCheckpoint = new("SaveCheckpoint");
    public static readonly Operation ApplyRetention = new("ApplyRetention");
}
```

### BillRelayInfoKey
```csharp
// BillRelay/Logging/BillRelayInfoKey.cs
public static class BillRelayInfoKey
{
    public static readonly LogInfoKey CorrelationId = new("CorrelationId");
    public static readonly LogInfoKey MessageUid = new("MessageUid");
    public static readonly LogInfoKey ProviderName = new("ProviderName");
    public static readonly LogInfoKey AttachmentId = new("AttachmentId");
    public static readonly LogInfoKey FileName = new("FileName");
    public static readonly LogInfoKey FileSizeBytes = new("FileSizeBytes");
    public static readonly LogInfoKey FilePath = new("FilePath");
    public static readonly LogInfoKey Recipient = new("Recipient");
    public static readonly LogInfoKey DurationMs = new("DurationMs");
    public static readonly LogInfoKey Status = new("Status");
    public static readonly LogInfoKey ErrorMessage = new("ErrorMessage");
    public static readonly LogInfoKey UidValidity = new("UidValidity");
    public static readonly LogInfoKey LastProcessedUid = new("LastProcessedUid");
    public static readonly LogInfoKey MessageCount = new("MessageCount");
    public static readonly LogInfoKey UnseenCount = new("UnseenCount");
    public static readonly LogInfoKey AttachmentCount = new("AttachmentCount");
    public static readonly LogInfoKey RemovedCount = new("RemovedCount");
    public static readonly LogInfoKey FreedBytes = new("FreedBytes");
    public static readonly LogInfoKey RemainingCount = new("RemainingCount");
}
```

## Related Documentation

- [Components: BillSender](../components/bill-sender.md) — NuciNotifications usage
- [Components: FileStateStore](../components/file-state-store.md) — State models
- [Integrations](../integrations.md) — External integration details
- [Logging](../logging.md) — Logging models