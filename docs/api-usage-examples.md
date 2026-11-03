# API Usage Examples

## Overview

Bill Relay has **no public API**. This document shows usage patterns for internal service interfaces and external integrations.

---

## Internal Service Usage

### IStateStore

```csharp
// Inject IStateStore
public class MyService(IStateStore stateStore)
{
    public async Task<bool> IsDuplicateAsync(string attachmentId, CancellationToken ct)
    {
        return await stateStore.IsProcessedAsync(attachmentId, ct);
    }

    public async Task RecordAsync(ProcessedAttachmentDataObject data, CancellationToken ct)
    {
        await stateStore.MarkProcessedAsync(data, ct);
    }

    public async Task<ImapCheckpoint> GetPositionAsync(CancellationToken ct)
    {
        return await stateStore.GetCheckpointAsync(ct);
    }
}
```

### IAttachmentProcessor

```csharp
// Inject IAttachmentProcessor
public class MyService(IAttachmentProcessor processor)
{
    public async Task<string> SaveAttachmentAsync(MimePart attachment, string id, CancellationToken ct)
    {
        await processor.ProcessAsync(attachment, id, ct);
        // File saved to DownloadDirectory as {id}.pdf
        return id;
    }
}
```

### IBillDelivery

```csharp
// Inject IBillDelivery
public class MyService(IBillDelivery delivery)
{
    public async Task DeliverBillAsync(string recipient, string provider, string filePath, string fileName, CancellationToken ct)
    {
        await delivery.DeliverAsync(recipient, provider, filePath, fileName, ct);
    }
}
```

### IBillSender

```csharp
// Inject IBillSender
public class MyService(IBillSender sender)
{
    public async Task SendEmailAsync(string recipient, string provider, string filePath, string fileName, CancellationToken ct)
    {
        await sender.SendAsync(recipient, provider, filePath, fileName, ct);
    }
}
```

---

## External Integration Usage

### IMAP (MailKit)

```csharp
using MailKit.Net.Imap;
using MailKit.Search;
using MimeKit;

var client = new ImapClient();
await client.ConnectAsync("imap.example.com", 993, true, ct);
await client.AuthenticateAsync("user@example.com", "password", ct);

var inbox = client.Inbox;
await inbox.OpenAsync(FolderAccess.ReadWrite, ct);

var uids = await inbox.SearchAsync(SearchQuery.NotSeen, ct);
foreach (var uid in uids)
{
    var message = await inbox.GetMessageAsync(uid, ct);
    // Process message
}

await client.DisconnectAsync(true, ct);
```

### NuciNotifications Client

```csharp
using NuciNotifications.Client;

var client = new NuciNotificationsClient(
    baseUrl: "https://api.notifications.example.com",
    apiKey: "api-key",
    hmacSecret: "hmac-secret");

var request = new NotificationRequest
{
    To = "recipient@example.com",
    Subject = "Test",
    Body = "<p>Test</p>",
    IsHtml = true,
    Attachments =
    [
        new AttachmentDto
        {
            FileName = "bill.pdf",
            ContentBase64 = Convert.ToBase64String(pdfBytes),
            MimeType = "application/pdf"
        }
    ]
};

var response = await client.SendAsync(request, ct);
if (!response.Success)
{
    // Handle error
}
```

### File State (NuciDAL)

```csharp
using NuciDAL;

var repo = new JsonRepository<ProcessedAttachmentDataObject>("/path/to/state.json");

// Load all
var items = repo.LoadAll();

// Add
items.Add(new ProcessedAttachmentDataObject { ... });

// Save (atomic)
repo.SaveAll(items);
```

---

## Configuration Binding

```csharp
// In Program.cs or any service
var settings = builder.Configuration.Get<BillRelaySettings>();

// Or with Options pattern
services.Configure<ImapSettings>(builder.Configuration.GetSection("Imap"));

// In service
public class MyService(IOptions<ImapSettings> imapOptions)
{
    private readonly ImapSettings _settings = imapOptions.Value;
}
```

---

## Testing Patterns

### Mock IStateStore

```csharp
var mockStore = new Mock<IStateStore>();
mockStore.Setup(s => s.IsProcessedAsync("sha256:abc", It.IsAny<CancellationToken>()))
         .ReturnsAsync(false);
mockStore.Setup(s => s.MarkProcessedAsync(It.IsAny<ProcessedAttachmentDataObject>(), It.IsAny<CancellationToken>()))
         .Returns(Task.CompletedTask);
```

### Mock IBillSender

```csharp
var mockSender = new Mock<IBillSender>();
mockSender.Setup(s => s.SendAsync(
    "test@example.com", "Vodafone", "path.pdf", "bill.pdf", It.IsAny<CancellationToken>()))
    .Returns(Task.CompletedTask);
```

---

## Related Documentation

- [API Reference Index](./api-reference/INDEX.md)
- [Components](../components/) — Service implementations
- [Integrations](../integrations.md) — External integration details