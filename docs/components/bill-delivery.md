# BillDelivery Component

## Overview

**Location:** `BillRelay/Services/BillDelivery.cs`

**Interface:** `IBillDelivery`

**Lifetime:** Singleton

**Purpose:** Coordinates the delivery pipeline — deduplication check, attachment processing, and email sending.

## Responsibilities

1. **Deduplication Gate** — Check `IStateStore.IsProcessedAsync` before processing
2. **Pipeline Orchestration** — Call `IAttachmentProcessor` then `IBillSender`
3. **Error Boundaries** — Ensure state only marked processed on full success

## Key Methods

### DeliverAsync

```csharp
public async Task DeliverAsync(string attachmentId, Provider provider, string messageId, CancellationToken cancellationToken)
{
    // 1. Deduplication check
    if (await _stateStore.IsProcessedAsync(attachmentId))
    {
        _logger.LogInformation("Attachment already processed, skipping delivery: {AttachmentId}", attachmentId);
        return;
    }

    // 2. Process attachment (file storage)
    // Note: AttachmentProcessor.ProcessAsync is called by BillRelayWorker before DeliverAsync
    // This method assumes file is already stored and attachmentId is known

    // 3. Send email
    await _billSender.SendAsync(attachmentId, provider, messageId, cancellationToken);

    // 4. Mark processed (only on success)
    await _stateStore.MarkProcessedAsync(new ProcessedAttachmentDataObject
    {
        AttachmentId = attachmentId,
        ProviderName = provider.ToString(),
        MessageId = messageId,
        ProcessedAt = DateTimeOffset.UtcNow
        // FileName, FilePath, FileSizeBytes populated by caller
    });
}
```

**Note:** Current implementation in `BillRelayWorker.DeliverAttachmentAsync` calls `AttachmentProcessor.ProcessAsync` first, then `BillDelivery.DeliverAsync`. The `BillDelivery` implementation shown above is the interface contract; actual coordination happens in the worker.

## Dependencies (Injected)

| Dependency | Interface | Purpose |
|------------|-----------|---------|
| `ILogger<BillDelivery>` | `ILogger` | Structured logging |
| `IStateStore` | `IStateStore` | Processed attachment ledger |
| `IBillSender` | `IBillSender` | Email sending |

## Delivery Flow

```
BillRelayWorker.DeliverAttachmentAsync
    │
    ├─► AttachmentProcessor.ProcessAsync(stream, fileName, provider)
    │       └─► Returns attachmentId (SHA256)
    │
    ├─► StateStore.IsProcessedAsync(attachmentId)
    │       └─► If true: return (skip)
    │
    ├─► BillDelivery.DeliverAsync(attachmentId, provider, messageId)
    │       │
    │       ├─► BillSender.SendAsync(attachmentId, provider, messageId)
    │       │       └─► NuciNotifications API
    │       │
    │       └─► StateStore.MarkProcessedAsync(dataObject)
    │               └─► Persist to JSON ledger
    │
    └─► StateStore.SaveCheckpointAsync(checkpoint)
            └─► Update IMAP position
```

## Error Handling

| Failure Point | State Marked Processed? | Retry Behavior |
|---------------|------------------------|----------------|
| `IsProcessedAsync` fails | N/A | Exception bubbles, message reprocessed next cycle |
| `SendAsync` fails | **No** | Exception bubbles, message reprocessed next cycle |
| `MarkProcessedAsync` fails | N/A | Exception bubbles, delivery succeeded but not recorded (duplicate risk) |

**Critical:** State is only marked processed after successful email send. If `MarkProcessedAsync` fails after send, the attachment may be delivered again on next cycle (at-least-once delivery).

## Concurrency

- **Single-threaded** — Called from `BillRelayWorker` sequentially
- **State store** — Single-writer, no locking

## Logging

Uses `BillRelayOperation`:
- `ProcessAttachment` — Delivery coordination

Uses `BillRelayInfoKey`:
- `AttachmentId` — SHA256 hash
- `ProviderName` — Provider enum
- `MessageId` — IMAP Message-ID

## Extension Points

Implement `IBillDelivery` for custom delivery logic:
- Multiple delivery channels (email + webhook + SMS)
- Batch delivery
- Priority queuing
- Dead letter handling

## Testing

**Unit Tests:** `BillRelay.UnitTests.Services.BillDeliveryTests`
- Deduplication skip
- Successful delivery marks processed
- Send failure does not mark processed
- State store failure propagation

**Integration Tests:** `BillRelay.IntegrationTests.Services.FileStateStoreIntegrationTests` (indirect)