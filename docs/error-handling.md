# Error Handling

## Error Taxonomy

| Category | Examples | Handling Strategy |
|----------|----------|-------------------|
| **Startup Validation** | Invalid config, missing files, bad URLs | Fail fast — process exits |
| **Transient External** | IMAP network blip, API timeout, DNS | Log, continue, retry next cycle |
| **Validation Failure** | Bad MIME type, oversized, invalid PDF | Log reason, skip attachment |
| **Processing Failure** | File write error, hash mismatch | Log, rethrow, checkpoint advances |
| **Delivery Failure** | NuciNotifications API error, SMTP failure | Log, rethrow, checkpoint advances |
| **State Persistence Failure** | JSON write error, disk full | Log, rethrow, may lose checkpoint |
| **Cancellation** | Host shutdown, Ctrl+C | Clean shutdown, preserve state |

## Handling Patterns

### 1. Fail Fast (Startup)

```csharp
// Program.cs
static void ValidateNotifications(NuciNotificationsSettings settings)
{
    if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https")
        throw new InvalidOperationException("BaseUrl must be absolute HTTPS");
    if (string.IsNullOrWhiteSpace(settings.ApiKey))
        throw new InvalidOperationException("ApiKey required");
    if (string.IsNullOrWhiteSpace(settings.HmacSharedSecretKey))
        throw new InvalidOperationException("HmacSharedSecretKey required");
}
```

- Throws `InvalidOperationException` before host starts
- Prevents running with invalid configuration

### 2. Poll-Level Resilience (Worker Loop)

```csharp
// BillRelayWorker.ExecuteAsync
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
```

- Catches ALL exceptions from `ProcessInboxAsync`
- Logs error, continues to next poll cycle
- Ensures worker survives transient failures

### 3. Per-Message Resilience

```csharp
// BillRelayWorker.ProcessMailboxAsync
foreach (var uid in uids.OrderBy(u => u.Id))
{
    try
    {
        var message = await mailbox.GetMessageAsync(uid, cancellationToken);
        await ProcessMessageAsync(message, uid, cancellationToken);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error processing message {Uid}", uid.Id);
    }
    finally
    {
        checkpoint.LastProcessedUid = uid.Id;
        checkpoint.LastProcessedDate = message.Date.UtcDateTime;
        await _stateStore.SaveCheckpointAsync(checkpoint, cancellationToken);
    }
}
```

- Each message processed independently
- Checkpoint advances in `finally` — **even on failure**
- Failed message not retried automatically

### 4. Validation with Descriptive Reasons

```csharp
// AttachmentValidator.GetFailureReason
public string? GetFailureReason(MimeEntity attachment)
{
    if (attachment is not MimePart part)
        return "The attachment is not a MIME part.";

    if (!settings.Value.Attachments.PermittedMimeTypes.Contains(part.ContentType.MimeType))
        return $"The MIME type '{part.ContentType.MimeType}' is not permitted.";

    // ... size check ...

    if (!HasValidPdfSignature(part))
        return "The attachment does not start with the required PDF signature '%PDF-'.";

    return null; // Success
}
```

- Returns `string?` — null = valid, string = failure reason
- Caller logs reason, continues to next attachment

### 5. Delivery with State Consistency

```csharp
// BillDelivery.DeliverAsync
public async Task DeliverAsync(...)
{
    string deliveryId = "delivery:" + attachmentId;

    if (await _stateStore.IsProcessedAsync(deliveryId, ct))
        return; // Already delivered

    try
    {
        await _attachmentProcessor.ProcessAsync(...);
        await _billSender.SendAsync(...);
        await _stateStore.MarkProcessedAsync(deliveryId, messageId, CancellationToken.None);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        throw; // Don't swallow cancellation
    }
    catch (Exception ex)
    {
        _logger.Error(..., ex, ...);
        throw; // Rethrow — worker catches, advances checkpoint
    }
}
```

**Critical Invariant:** State marked processed ONLY after successful send.

### 6. Atomic File Operations

```csharp
// AttachmentProcessor.FinalizeFileAsync
var tempPath = Path.GetTempFileName();
try
{
    await WriteToTempFileAsync(...);
    VerifyHash(tempPath);
    File.Move(tempPath, targetPath, overwrite: true);
}
finally
{
    if (File.Exists(tempPath)) File.Delete(tempPath);
}
```

- Temp file in system temp directory
- Hash verified before move
- Cleanup in `finally` block
- Atomic move prevents partial writes

## Failure Semantics by Component

### IMAP Operations

| Operation | Failure | Result |
|-----------|---------|--------|
| Connect | Network, TLS, cert | Exception → worker catches → retry next poll |
| Authenticate | Bad credentials | Exception → worker catches → retry next poll |
| Search | Network, mailbox | Exception → worker catches → retry next poll |
| Fetch message | Network, UID gone | Exception → worker catches → skip message, checkpoint advances |

### Attachment Validation

| Check | Failure | Result |
|-------|---------|--------|
| Not MimePart | Malformed message | Log reason, try next attachment |
| MIME type | Not in permitted list | Log reason, try next attachment |
| Size | > MaximumSizeBytes | Log reason, try next attachment |
| PDF signature | Not `%PDF-` | Log reason, try next attachment |

### E.ON Download

| Failure | Result |
|---------|--------|
| No download link in body | Log debug, return null → message skipped |
| Link without token | Log debug, return null → message skipped |
| HTTP error (4xx, 5xx) | Log error, return null → message skipped |
| Missing/empty base64 `file` | Log error, return null → message skipped |
| Non-PDF content type | Log error, return null → message skipped |
| Empty content | Log error, return null → message skipped |
| Timeout | Log error, return null → message skipped |
| Network exception | Log error, return null → message skipped |

### File Storage

| Failure | Result |
|---------|--------|
| Directory creation | Exception → rethrow → worker catches |
| Temp file write | Exception → cleanup → rethrow |
| Hash mismatch after write | `InvalidOperationException` → cleanup → rethrow |
| Move fails (permissions, disk) | Exception → cleanup → rethrow |

### Email Sending (NuciNotifications)

| Failure | Result |
|---------|--------|
| API authentication | Exception → rethrow → worker catches |
| API validation error | Exception → rethrow → worker catches |
| API server error (5xx) | Exception → rethrow → worker catches |
| Network/timeout | Exception → rethrow → worker catches |
| **All send failures** | **Checkpoint advances, no auto-retry** |

### State Persistence

| Operation | Failure | Result |
|-----------|---------|--------|
| Initialize (dir/file create) | Permissions, disk | Exception → worker stops |
| GetCheckpoint | File read, JSON parse | Exception → worker catches |
| SaveCheckpoint | File write, JSON serialize | Exception → worker catches |
| IsProcessed | Repository read | Exception → rethrow |
| MarkProcessed | Repository write | Exception → rethrow (send succeeded, duplicate risk) |
| ApplyRetention | Repository write | Exception → rethrow |

## Recovery Procedures

### Failed Email Delivery

1. Check logs for `BillRelayOperation.ProcessAttachment` with `OperationStatus.Failure`
2. Locate saved PDF in `Storage.DownloadDirectory` (named `{Provider}_{filename}`)
3. Manually send via NuciNotifications API or email client
4. Optionally: Add `delivery:<sha256>` to state JSON to prevent future retry

### Corrupted State File

1. Stop worker
2. Backup `stateFilePath` and `stateFilePath.checkpoint`
3. Restore from backup or delete (causes full rescan within age window)
4. Restart worker

### UID Validity Change

- Detected automatically on next poll
- Checkpoint reset to new `UidValidity`
- Full rescan of messages within age window
- No manual intervention needed

### Disk Full

1. Free space in `DownloadDirectory` and state directory
2. Worker will retry on next poll cycle
3. Consider retention cleanup: call `FileStateStore.ApplyRetentionAsync` manually

## Error Logging Standards

All errors logged with:
- `BillRelayOperation` — Categorizes the operation
- `OperationStatus.Failure` — Filterable status
- `Exception` — Full exception with stack trace
- `LogInfo` keys — Structured context (AttachmentId, ProviderName, MessageId, Reason, etc.)

## Gaps and Known Issues

1. **No automatic retry for send failures** — Checkpoint advances, message lost from pipeline
2. **No dead letter queue** — Failed deliveries not persisted for later retry
3. **No circuit breaker** — Repeated API failures hammer the endpoint
4. **MarkProcessedAsync uses `CancellationToken.None`** — May persist after cancellation
5. **Retention not automatically invoked** — State grows unbounded unless manually triggered
6. **No PII masking in logs** — Subject, Sender, Recipient logged at Info level

## Related Documentation

- [Components: BillRelayWorker](./components/bill-relay-worker.md)
- [Components: BillDelivery](./components/bill-delivery.md)
- [Components: AttachmentProcessor](./components/attachment-processor.md)
- [Components: FileStateStore](./components/file-state-store.md)
- [Logging](./logging.md) — Error logging format
- [Flows: Attachment Delivery](./flows/attachment-delivery.md) — Failure scenarios
- [Architecture: Error Handling](../architecture.md#error-handling)