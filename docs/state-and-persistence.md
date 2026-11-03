# State and Persistence

## Overview

Bill Relay uses **file-based JSON persistence** for all state. No database.

```
┌─────────────────────────────────────────────────────────────┐
│                      File System                             │
├─────────────────────────────────────────────────────────────┤
│  DownloadDirectory/                                          │
│  ├── {sha256}.pdf          ← Bill attachments (content-addressed)│
│  └── ...                                                        │
│                                                              │
│  StateFilePath/                                              │
│  ├── billrelay.json        ← Processed attachments + deliveries│
│  └── billrelay.json.checkpoint ← IMAP checkpoint              │
└─────────────────────────────────────────────────────────────┘
```

## State File (billrelay.json)

### Schema

```json
{
  "ProcessedAttachments": [
    {
      "AttachmentId": "sha256:abc123...",
      "FileName": "Vodafone_VDF123.pdf",
      "ProviderName": "Vodafone",
      "ProcessedDate": "2024-01-15T10:30:00Z",
      "FileSizeBytes": 245760,
      "MimeType": "application/pdf"
    },
    {
      "AttachmentId": "delivery:sha256:abc123...",
      "FileName": "delivery:Vodafone_VDF123.pdf",
      "ProviderName": "Vodafone",
      "ProcessedDate": "2024-01-15T10:31:00Z",
      "FileSizeBytes": 0,
      "MimeType": "application/x-delivery-record"
    }
  ],
  "Checkpoint": {
    "UidValidity": 1234567890,
    "LastProcessedUid": 42,
    "LastProcessedDate": "2024-01-15T10:30:00Z"
  }
}
```

### Data Objects

**ProcessedAttachmentDataObject** (in `BillRelay/DataAccess/DataObjects/`):
```csharp
public class ProcessedAttachmentDataObject
{
    public string AttachmentId { get; set; }      // "sha256:..." or "delivery:sha256:..."
    public string FileName { get; set; }          // Original or "delivery:..."
    public string ProviderName { get; set; }      // Provider identifier
    public DateTimeOffset ProcessedDate { get; set; }
    public long FileSizeBytes { get; set; }       // 0 for delivery records
    public string MimeType { get; set; }          // "application/pdf" or "application/x-delivery-record"
}
```

**ImapCheckpoint** (in `BillRelay/Services/ImapCheckpoint.cs`):
```csharp
public class ImapCheckpoint
{
    public uint UidValidity { get; set; }
    public uint LastProcessedUid { get; set; }
    public DateTimeOffset LastProcessedDate { get; set; }
}
```

### Storage Mechanism

**NuciDAL.JsonRepository<T>** handles:
- Serialization: `System.Text.Json` (camelCase, indented)
- Atomic writes: Temp file → verify → rename
- Deserialization: On load, creates empty if missing

```csharp
// FileStateStore constructor
_repository = new JsonRepository<ProcessedAttachmentDataObject>(stateFilePath);
_checkpointRepository = new JsonRepository<ImapCheckpoint>(checkpointFilePath);
```

## Checkpoint File (billrelay.json.checkpoint)

### Purpose

Tracks IMAP processing position to avoid reprocessing.

### Fields

| Field | Type | Description |
|-------|------|-------------|
| `UidValidity` | `uint` | Mailbox UIDVALIDITY (resets on mailbox rebuild) |
| `LastProcessedUid` | `uint` | Highest UID successfully processed |
| `LastProcessedDate` | `DateTimeOffset` | Timestamp of last processed message |

### Lifecycle

```
Startup
  │
  ├─ Load checkpoint
  │   ├─ File exists → deserialize
  │   └─ File missing → new (UidValidity=0, LastProcessedUid=0)
  │
  ├─ Connect IMAP
  │   ├─ Get current UidValidity
  │   └─ Compare with checkpoint
  │
  ├─ If UidValidity changed
  │   └─ Reset: LastProcessedUid = 0, LastProcessedDate = InitialProcessingDate
  │
  └─ Process messages with UID > LastProcessedUid
      │
      ├─ For each message processed
      │   └─ Update LastProcessedUid = uid
      │
      └─ After cycle
          └─ Save checkpoint (atomic)
```

### UIDVALIDITY Handling

**Scenario:** Mailbox recreated, migrated, or corrupted → IMAP server assigns new UIDVALIDITY.

**Behavior:**
1. Detect mismatch on connect
2. Log warning: `UidValidity changed from {old} to {new}, resetting checkpoint`
3. Reset `LastProcessedUid = 0`
4. Set `LastProcessedDate = InitialProcessingDate` (or `DateTimeOffset.MinValue` if not set)
5. Process all messages matching search criteria (unseen + age filter)
6. Save new checkpoint with new UIDVALIDITY

**Result:** Full rescan of mailbox (bounded by age filter).

## Download Directory

### Structure

```
DownloadDirectory/
├── a1b2c3d4e5f6...pdf    ← SHA256 hash as filename
├── f7e8d9c0b1a2...pdf
└── ...
```

### Naming

- **Filename:** `{AttachmentId}.pdf` where `AttachmentId` = `sha256:{hex}`
- **Provider prefix:** Not in filename (stored in state only)
- **Display name:** Reconstructed from state when needed

### Content Verification

```csharp
// AttachmentProcessor.ProcessAsync
var computedHash = ComputeSha256(content);
if (computedHash != expectedAttachmentId)
    throw new InvalidOperationException("Hash mismatch after write");
```

### Deduplication

```
Email 1: Attachment X (SHA256: abc123) → Save abc123.pdf, record in state
Email 2: Attachment X (SHA256: abc123) → IsProcessed("sha256:abc123") = true → Skip
Email 3: Attachment Y (SHA256: def456) → Save def456.pdf, record in state
```

## Retention Policy

### Configuration

```json
"Retention": {
  "MaximumAgeDays": 90,
  "MaximumCount": 10000
}
```

### Algorithm (FileStateStore.ApplyRetention)

```csharp
// 1. Filter by age (keep younger than MaximumAgeDays)
var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromDays(MaximumAgeDays);
var youngEnough = attachments.Where(a => a.ProcessedDate >= cutoff);

// 2. Sort by date descending (newest first)
var sorted = youngEnough.OrderByDescending(a => a.ProcessedDate);

// 3. Keep top MaximumCount
var toKeep = sorted.Take(MaximumCount).ToList();

// 4. Identify orphans (files on disk not in toKeep)
var keptIds = new HashSet<string>(toKeep.Select(a => a.AttachmentId));
var allFiles = Directory.GetFiles(DownloadDirectory, "*.pdf");
foreach (var file in allFiles)
{
    var id = "sha256:" + Path.GetFileNameWithoutExtension(file);
    if (!keptIds.Contains(id))
        File.Delete(file);  // Orphan cleanup
}

// 5. Save pruned list
_repository.SaveAll(toKeep);
```

### Execution

- **Trigger:** After each successful `SaveCheckpoint` (end of cycle)
- **Frequency:** Every polling cycle (~5 min)
- **Cost:** O(n) where n = total processed attachments

### Guarantees

| Guarantee | Mechanism |
|-----------|-----------|
| Recent preserved | Age filter first, then count |
| Young preserved | Age filter before count limit |
| Disk space bounded | Orphan file deletion |
| No gaps in deduplication | State pruned after file deletion |

## Atomicity and Durability

### Write Pattern (All Files)

```csharp
// NuciDAL.JsonRepository.SaveAll
var tempPath = filePath + ".tmp";
await File.WriteAllTextAsync(tempPath, json);
File.Move(tempPath, filePath, overwrite: true);  // Atomic on same volume
```

### Crash Scenarios

| Crash Point | State File | Checkpoint File | Download Files | Recovery |
|-------------|------------|-----------------|----------------|----------|
| During state write | `.tmp` exists, original intact | Unchanged | Unchanged | Ignore `.tmp`, original valid |
| After state, before checkpoint | Updated | Old | Updated | Reprocess last message (dedup skips) |
| After checkpoint | Updated | Updated | Updated | Clean resume |
| During file write | Unchanged | Unchanged | `.tmp` exists | Ignore `.tmp`, retry on next cycle |

### No Transactions

- State and checkpoint saved separately
- No rollback mechanism
- Idempotency handles duplicates

## Backup and Restore

### What to Backup

| Path | Criticality | Frequency |
|------|-------------|-----------|
| `StateFilePath/billrelay.json` | **Critical** (deduplication, deliveries) | Daily |
| `StateFilePath/billrelay.json.checkpoint` | **Critical** (IMAP position) | Daily |
| `DownloadDirectory/*.pdf` | Medium (re-downloadable for E.ON) | Weekly |

### Restore Procedure

```bash
# 1. Stop service
systemctl stop billrelay

# 2. Restore files
cp /backup/billrelay.json /opt/billrelay/state/
cp /backup/billrelay.json.checkpoint /opt/billrelay/state/
cp -r /backup/bills/* /opt/billrelay/bills/

# 3. Verify integrity
sha256sum /opt/billrelay/bills/*.pdf | while read hash file; do
    expected="sha256:$(basename "$file" .pdf)"
    if [[ "$hash" != "${expected#sha256:}" ]]; then
        echo "MISMATCH: $file"
    fi
done

# 4. Start service
systemctl start billrelay
```

### Point-in-Time Recovery

Not supported (no WAL, no snapshots). Restore from latest backup.

## Scaling Limits

| Dimension | Limit | Bottleneck |
|-----------|-------|------------|
| Processed attachments | ~10,000 | JSON rewrite O(n) per cycle |
| Download directory files | ~100,000 | Filesystem directory performance |
| State file size | ~50 MB | Memory during deserialization |
| Checkpoint frequency | Every cycle | Disk I/O |

### Mitigation for Growth

1. **Archive old state:** Move processed attachments to cold storage, prune state
2. **Partition by date:** Separate state files per month (requires code change)
3. **Migrate to SQLite:** Replace `JsonRepository` with EF Core SQLite (schema compatible)

## Related Documentation

- [Invariants](./invariants.md) — State invariants (I-1 through I-8, I-24, I-25)
- [Design Decisions](./design-decisions.md) — File-based state decision
- [FileStateStore](../components/file-state-store.md) — Component details
- [Concurrency and Scheduling](./concurrency-and-scheduling.md) — Single-writer model
- [Architecture: State and Persistence](../architecture.md#state-and-persistence)