# BillRelayWorker Component

## Overview

**Location:** `BillRelay/Services/BillRelayWorker.cs`

**Type:** `BackgroundService` (hosted service)

**Lifetime:** Singleton, runs for application lifetime

**Purpose:** Orchestrates the complete bill processing pipeline — IMAP polling, message classification, attachment validation, file storage, delivery, and state persistence.

## Responsibilities

1. **Polling Loop** — Executes `ProcessInboxAsync` at configured interval
2. **IMAP Connection Management** — Connect, authenticate, select mailbox
3. **Message Processing** — Iterate messages, apply age filter, classify provider
4. **Attachment Pipeline** — Validate, process, deliver, persist
5. **Checkpoint Management** — Track IMAP position for incremental processing
6. **Error Handling** — Log failures, continue processing other messages
7. **Retention** — Apply state cleanup policy periodically

## Key Methods

### ExecuteAsync (BackgroundService Entry)

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    while (!stoppingToken.IsCancellationRequested)
    {
        try
        {
            await ProcessInboxAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing inbox");
        }

        await Task.Delay(TimeSpan.FromSeconds(_pollingSettings.IntervalSeconds), stoppingToken);
    }
}
```

- Runs until cancellation
- Catches all exceptions to keep loop alive
- Respects `PollingSettings.IntervalSeconds`

### ProcessInboxAsync

```csharp
private async Task ProcessInboxAsync(CancellationToken cancellationToken)
{
    using var client = new ImapClient();
    await client.ConnectAsync(_imapSettings.Server, _imapSettings.Port, _imapSettings.IsSslEnabled, cancellationToken);
    
    if (!_imapSettings.IsCertificateValidationEnabled)
        client.ServerCertificateValidationCallback = (_, _, _, _) => true;

    await client.AuthenticateAsync(_imapSettings.Username, _imapSettings.Password, cancellationToken);
    
    var mailbox = await client.GetFolderAsync(_imapSettings.Mailbox, cancellationToken);
    await mailbox.OpenAsync(FolderAccess.ReadWrite, cancellationToken);

    await ProcessMailboxAsync(mailbox, cancellationToken);

    await mailbox.CloseAsync(true, cancellationToken);
    await client.DisconnectAsync(true, cancellationToken);
}
```

- Creates new IMAP connection per cycle
- Configures certificate validation from settings
- Opens mailbox in ReadWrite (needed for flags if used)
- Delegates to `ProcessMailboxAsync`

### ProcessMailboxAsync

```csharp
private async Task ProcessMailboxAsync(IMailFolder mailbox, CancellationToken cancellationToken)
{
    var checkpoint = await _stateStore.GetCheckpointAsync();
    
    // Validate UID validity
    if (checkpoint.UidValidity != mailbox.UidValidity.ToString())
    {
        checkpoint = new ImapCheckpoint { UidValidity = mailbox.UidValidity.ToString() };
    }

    var query = SearchQuery.DeliveredAfter(_emailAgePolicy.GetCutoff(_pollingSettings));
    var uids = await mailbox.SearchAsync(query, cancellationToken);

    foreach (var uid in uids.OrderBy(u => u.Id))
    {
        if (uid.Id <= checkpoint.LastProcessedUid) continue;

        var message = await mailbox.GetMessageAsync(uid, cancellationToken);
        await ProcessMessageAsync(message, uid, cancellationToken);

        checkpoint.LastProcessedUid = uid.Id;
        checkpoint.LastProcessedDate = message.Date.UtcDateTime;
        await _stateStore.SaveCheckpointAsync(checkpoint);
    }
}
```

- Loads checkpoint, validates `UidValidity`
- Searches messages newer than age cutoff
- Processes in UID order (oldest first)
- Updates checkpoint after each message

### ProcessMessageAsync

```csharp
private async Task ProcessMessageAsync(MimeMessage message, UniqueId uid, CancellationToken cancellationToken)
{
    var provider = BillProviderIdentification.IdentifyProvider(message);
    if (provider == Provider.Unknown) return;

    var attachment = await GetValidAttachmentAsync(message, provider, cancellationToken);
    if (attachment == null) return;

    await DeliverAttachmentAsync(attachment, provider, message.MessageId, cancellationToken);
}
```

- Identifies provider, skips unknown
- Gets valid attachment (handles E.ON download)
- Delivers attachment

### GetValidAttachmentAsync

```csharp
private async Task<MimePart?> GetValidAttachmentAsync(MimeMessage message, Provider provider, CancellationToken cancellationToken)
{
    if (provider == Provider.EOn)
    {
        return await _eOnBillDownloader.DownloadBillAsync(message, cancellationToken);
    }

    foreach (var attachment in message.Attachments.OfType<MimePart>())
    {
        if (await _attachmentValidator.ValidateAsync(attachment, provider, cancellationToken))
            return attachment;
    }
    return null;
}
```

- E.ON: Downloads from link in body via `EOnBillDownloader`
- Others: Validates each attachment via `AttachmentValidator`

### DeliverAttachmentAsync

```csharp
private async Task DeliverAttachmentAsync(MimePart attachment, Provider provider, string messageId, CancellationToken cancellationToken)
{
    using var stream = new MemoryStream();
    await attachment.Content.DecodeToAsync(stream, cancellationToken);
    stream.Position = 0;

    var attachmentId = await _attachmentProcessor.ProcessAsync(stream, attachment.FileName, provider, cancellationToken);
    
    if (await _stateStore.IsProcessedAsync(attachmentId)) return;

    await _billDelivery.DeliverAsync(attachmentId, provider, messageId, cancellationToken);
    await _stateStore.MarkProcessedAsync(new ProcessedAttachmentDataObject
    {
        AttachmentId = attachmentId,
        FileName = attachment.FileName,
        FilePath = Path.Combine(_storageSettings.DownloadDirectory, $"{provider}_{attachment.FileName}"),
        ProviderName = provider.ToString(),
        MessageId = messageId,
        ProcessedAt = DateTimeOffset.UtcNow,
        FileSizeBytes = stream.Length
    });
}
```

- Decodes attachment to memory stream
- Processes via `AttachmentProcessor` (returns SHA256 `attachmentId`)
- Checks deduplication via `StateStore`
- Delivers via `BillDelivery`
- Marks processed in state store

## Dependencies (Injected)

| Dependency | Interface | Purpose |
|------------|-----------|---------|
| `ILogger<BillRelayWorker>` | `ILogger` | Structured logging |
| `ImapSettings` | `IOptions<ImapSettings>` | IMAP configuration |
| `PollingSettings` | `IOptions<PollingSettings>` | Polling configuration |
| `StorageSettings` | `IOptions<StorageSettings>` | Storage paths |
| `EmailAgePolicy` | `EmailAgePolicy` | Age cutoff calculation |
| `IAttachmentValidator` | `IAttachmentValidator` | Attachment validation |
| `IEOnBillDownloader` | `IEOnBillDownloader` | E.ON bill download |
| `IAttachmentProcessor` | `IAttachmentProcessor` | File storage |
| `IBillDelivery` | `IBillDelivery` | Delivery coordination |
| `IStateStore` | `IStateStore` | State persistence |

## Concurrency Model

- **Single-threaded** — BackgroundService executes sequentially
- **One IMAP connection per cycle** — Not pooled
- **One message at a time** — No parallel processing
- **State store** — Single-writer, no locking (assumes single instance)

## Error Handling

| Failure Point | Behavior |
|---------------|----------|
| IMAP connect/auth | Logged, cycle aborted, retry next interval |
| Mailbox open | Logged, cycle aborted |
| Message fetch | Logged, continue to next message |
| Provider unknown | Skip message silently |
| Attachment validation fail | Skip attachment, try next |
| E.ON download fail | Logged, skip message |
| File write fail | Logged, skip delivery |
| Delivery fail | Logged, state not marked processed (retry next cycle) |
| Checkpoint save fail | Logged, may reprocess on restart |

## Extension Points

- **Custom Validators** — Implement `IAttachmentValidator`
- **Custom Downloaders** — Implement `IEOnBillDownloader` (provider-specific)
- **Custom Processors** — Implement `IAttachmentProcessor`
- **Custom Delivery** — Implement `IBillDelivery`
- **Custom State Store** — Implement `IStateStore`

## Logging Operations

Uses `BillRelayOperation`:
- `RunWorker` — Top-level cycle
- `ProcessInbox` — IMAP connection and mailbox processing
- `ProcessAttachment` — Per-attachment processing
- `ValidateAttachment` — Validation step
- `WriteAttachment` — File write step
- `SaveAttachment` — File move step
- `InitializeStateStore` — State store setup
- `ApplyRetention` — Retention cleanup