# FileStateStore

## Overview

`FileStateStore` implements `IStateStore` using `NuciDAL.JsonRepository` for JSON file persistence. Manages processed attachments, delivery records, and IMAP checkpoint.

**File:** `BillRelay/Services/FileStateStore.cs`

## Responsibilities

- Load/save processed attachments (deduplication)
- Load/save IMAP checkpoint (position tracking)
- Apply retention policy (age + count)
- Atomic writes via temp file + rename

## Key Methods

### InitializeAsync
```csharp
public async Task InitializeAsync(CancellationToken cancellationToken)
```
- Loads state file and checkpoint file
- Creates empty if missing
- Validates JSON structure

### IsProcessedAsync
```csharp
public async Task<bool> IsProcessedAsync(string attachmentId, CancellationToken cancellationToken)
```
- Checks if `attachmentId` exists in `ProcessedAttachments`
- Used for both content deduplication (`sha256:...`) and delivery deduplication (`delivery:sha256:...`)

### MarkProcessedAsync
```csharp
public async Task MarkProcessedAsync(ProcessedAttachmentDataObject data, CancellationToken cancellationToken)
```
- Adds entry to `ProcessedAttachments`
- Saves entire list (rewrite)
- Called for both attachment save and delivery confirmation

### GetCheckpointAsync
```csharp
public async Task<ImapCheckpoint> GetCheckpointAsync(CancellationToken cancellationToken)
```
- Returns current `ImapCheckpoint` (UidValidity, LastProcessedUid, LastProcessedDate)

### SaveCheckpointAsync
```csharp
public async Task SaveCheckpointAsync(ImapCheckpoint checkpoint, CancellationToken cancellationToken)
```
- Updates checkpoint file atomically
- Triggers retention after save

### ApplyRetention
```csharp
private void ApplyRetention()
```
- Filters by age (`MaximumAgeDays`)
- Keeps newest `MaximumCount`
- Deletes orphan PDF files from `DownloadDirectory`
- Saves pruned list

## Data Structures

### ProcessedAttachmentDataObject
```csharp
// BillRelay/DataAccess/DataObjects/ProcessedAttachmentDataObject.cs
public class ProcessedAttachmentDataObject
{
    public string AttachmentId { get; set; }      // "sha256:..." or "delivery:sha256:..."
    public string FileName { get; set; }          // Original filename or "delivery:..."
    public string ProviderName { get; set; }      // Provider identifier
    public DateTimeOffset ProcessedDate { get; set; }
    public long FileSizeBytes { get; set; }       // 0 for delivery records
    public string MimeType { get; set; }          // "application/pdf" or "application/x-delivery-record"
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

## Configuration

```json
"Storage": {
  "DownloadDirectory": "./bills",
  "StateFilePath": "./state/billrelay.json"
},
"Retention": {
  "MaximumAgeDays": 90,
  "MaximumCount": 10000
}
```

**Settings Classes:**
- `StorageSettings` → `DownloadDirectory`, `StateFilePath`
- `RetentionSettings` → `MaximumAgeDays`, `MaximumCount`

## Dependencies

| Dependency | Purpose |
|------------|---------|
| `NuciDAL.JsonRepository` | JSON file persistence |
| `IOptions<StorageSettings>` | Directory paths |
| `IOptions<RetentionSettings>` | Retention policy |
| `ILogger` | Structured logging |

## Logging

| Operation | Info Keys |
|-----------|-----------|
| `InitializeStateStore` | `StateFilePath`, `CheckpointFilePath` |
| `LoadCheckpoint` | `UidValidity`, `LastProcessedUid` |
| `SaveCheckpoint` | `LastProcessedUid`, `DurationMs` |
| `MarkProcessed` | `AttachmentId`, `ProviderName` |
| `IsProcessed` | `AttachmentId`, `Result` |
| `ApplyRetention` | `RemovedCount`, `FreedBytes`, `RemainingCount` |

## Invariants

- **I-1:** Processed attachment uniqueness
- **I-2:** Delivery record uniqueness
- **I-3:** Checkpoint monotonicity
- **I-4:** UID validity consistency
- **I-5:** State file atomicity
- **I-7:** No orphan files
- **I-24:** Retention preserves recent
- **I-25:** Retention preserves young

## Testing

**Unit Tests:** `FileStateStoreIntegrationTests` (in IntegrationTests project)
- Initialize
- Checkpoint load/save
- IsProcessed / MarkProcessed
- Retention (age + count + orphan cleanup)

**Test Pattern:**
```csharp
[Test]
public async Task GivenExistingState_WhenMarkProcessed_ThenEntryAdded()
{
    var store = CreateStore();
    await store.InitializeAsync(CancellationToken.None);

    var data = new ProcessedAttachmentDataObject
    {
        AttachmentId = "sha256:abc123",
        FileName = "test.pdf",
        ProviderName = "Vodafone",
        ProcessedDate = DateTimeOffset.UtcNow,
        FileSizeBytes = 1024,
        MimeType = "application/pdf"
    };

    await store.MarkProcessedAsync(data, CancellationToken.None);

    var exists = await store.IsProcessedAsync("sha256:abc123", CancellationToken.None);
    Assert.That(exists, Is.True);
}
```

## Related Documentation

- [State and Persistence](../state-and-persistence.md) — Persistence model
- [Invariants](../invariants.md) — State invariants
- [Configuration](../configuration.md#storage-settings) — Settings reference
- [Architecture: State Management](../architecture.md#state-management)