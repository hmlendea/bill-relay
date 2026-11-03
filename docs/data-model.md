# Data Model

## Core Data Objects

### ProcessedAttachmentDataObject

**Location:** `BillRelay/DataAccess/DataObjects/ProcessedAttachmentDataObject.cs`

**Purpose:** Represents a processed attachment in the persistent state ledger. Used for deduplication and audit trail.

```csharp
public sealed class ProcessedAttachmentDataObject
{
    public string AttachmentId { get; set; } = string.Empty;      // SHA256 hash of PDF content
    public string FileName { get; set; } = string.Empty;          // Original filename
    public string FilePath { get; set; } = string.Empty;          // Local filesystem path
    public string ProviderName { get; set; } = string.Empty;      // Identified provider
    public string MessageId { get; set; } = string.Empty;         // IMAP Message-ID header
    public DateTimeOffset ProcessedAt { get; set; }               // Processing timestamp
    public long FileSizeBytes { get; set; }                       // File size in bytes
}
```

**Persistence:** JSON array in `StorageSettings.StateFilePath` (default `./state/billrelay.json`)

**Deduplication Key:** `AttachmentId` (SHA256 of file content)

**Retention:** Governed by `RetentionSettings.MaxAgeDays` and `RetentionSettings.MaxFiles`

---

### EmailTemplateDataObject

**Location:** `BillRelay/DataAccess/DataObjects/EmailTemplateDataObject.cs`

**Purpose:** Deserialised email template from `email-template.xml`

```csharp
public sealed class EmailTemplateDataObject
{
    public string Subject { get; set; } = string.Empty;  // Contains {ProviderName} placeholder
    public string Body { get; set; } = string.Empty;     // Contains {ProviderName} placeholder
}
```

**XML Structure:**
```xml
<EmailTemplate>
  <Subject>New bill from {ProviderName}</Subject>
  <Body><![CDATA[Your bill from {ProviderName} is attached.]]></Body>
</EmailTemplate>
```

---

### ImapCheckpoint

**Location:** `BillRelay/Services/ImapCheckpoint.cs`

**Purpose:** Tracks IMAP processing position for incremental polling. Persisted separately from attachment ledger.

```csharp
public sealed class ImapCheckpoint
{
    public string UidValidity { get; set; } = string.Empty;   // IMAP UIDVALIDITY value
    public uint LastProcessedUid { get; set; }                // Highest UID processed
    public DateTimeOffset LastProcessedDate { get; set; }     // Date of last processed message
}
```

**Persistence:** JSON file at `{StateFilePath}.checkpoint` (default `./state/billrelay.json.checkpoint`)

**Usage:**
- On startup: Load checkpoint, validate `UidValidity` matches current mailbox
- On each cycle: Update `LastProcessedUid` and `LastProcessedDate` after successful processing
- If `UidValidity` mismatch: Full mailbox rescan (safety mechanism)

---

## Provider Identification

### BillProviderIdentification

**Location:** `BillRelay/Services/BillProviderIdentification.cs`

**Purpose:** Static classifier that identifies utility provider from email metadata.

**Algorithm:**
1. Check subject against provider-specific keywords
2. Check sender email domain against provider domains
3. For E.ON: Check body for download link domain
4. Return `Provider` enum value or `Provider.Unknown`

### Provider Enum

```csharp
public enum Provider
{
    Unknown,
    Vodafone,
    ElectricaFurnizare,
    EBloc,
    Hidroelectrica,
    EOn
}
```

### Provider Patterns

| Provider | Subject Keywords | Sender Domains | Body Link Domains | Filename Regex |
|----------|------------------|----------------|-------------------|----------------|
| **Vodafone** | "factura", "invoice", "bill" | vodafone.ro, vodafone.com | — | `^VDF.*\.pdf$` |
| **Electrica Furnizare** | "factura", "invoice", "bill" | electrifica.ro, electrificafurnizare.ro | — | `^DII.*P.*\.pdf$` |
| **E-bloc** | "nota de plata", "payment note" | e-bloc.ro, ebloc.ro | — | `^Nota de plată\.pdf$` |
| **Hidroelectrica** | "factura", "invoice", "bill" | hidroelectrica.ro | — | `^FX-.*\.pdf$` |
| **E.ON** | "factura", "invoice", "bill" | eon.ro, eon-energia.ro | eon.ro/descarcare-factura | `^.*\.pdf$` (downloaded) |

### Identification Logic (Simplified)

```csharp
public static Provider IdentifyProvider(MimeMessage message)
{
    var subject = message.Subject?.ToLowerInvariant() ?? "";
    var from = message.From.Mailboxes.FirstOrDefault()?.Address?.ToLowerInvariant() ?? "";
    var body = message.TextBody?.ToLowerInvariant() ?? "";

    // Vodafone
    if (subject.Contains("factura") && from.Contains("vodafone")) return Provider.Vodafone;

    // Electrica Furnizare
    if (subject.Contains("factura") && (from.Contains("electrifica") || from.Contains("electrificafurnizare")))
        return Provider.ElectricaFurnizare;

    // E-bloc
    if (subject.Contains("nota de plata") && (from.Contains("e-bloc") || from.Contains("ebloc")))
        return Provider.EBloc;

    // Hidroelectrica
    if (subject.Contains("factura") && from.Contains("hidroelectrica"))
        return Provider.Hidroelectrica;

    // E.ON (subject + sender OR body link)
    if (subject.Contains("factura") && (from.Contains("eon.ro") || from.Contains("eon-energia")))
        return Provider.EOn;
    if (body.Contains("eon.ro/descarcare-factura"))
        return Provider.EOn;

    return Provider.Unknown;
}
```

---

## Attachment Validation

### AttachmentValidator

**Location:** `BillRelay/Services/Attachments/AttachmentValidator.cs`

**Validation Rules:**

| Check | Implementation | Failure Result |
|-------|----------------|----------------|
| MIME Type | `part.ContentType.MimeType == "application/pdf"` | Rejected |
| Size | `part.ContentStream.Length <= AttachmentSettings.MaximumSizeBytes` | Rejected |
| PDF Signature | First 5 bytes == `%PDF-` | Rejected |

**Provider-Specific Filename Validation:**
After provider identification, filename matched against provider's `GeneratedRegex` pattern.

---

## Email Age Policy

### EmailAgePolicy

**Location:** `BillRelay/Services/EmailAgePolicy.cs`

**Purpose:** Determines if an email is within the processing window.

```csharp
public static DateTimeOffset GetCutoff(PollingSettings settings)
{
    if (settings.InitialProcessingDate.HasValue)
        return settings.InitialProcessingDate.Value;
    return DateTimeOffset.UtcNow.AddDays(-settings.MaximumEmailAgeDays);
}

public static bool IsWithinAgeLimit(MimeMessage message, PollingSettings settings)
{
    var cutoff = GetCutoff(settings);
    var date = message.Date.UtcDateTime;
    return date >= cutoff;
}
```

**Priority:** `InitialProcessingDate` (explicit) > `MaximumEmailAgeDays` (relative)

---

## State Persistence

### FileStateStore

**Location:** `BillRelay/Services/FileStateStore.cs`

**Wraps:** `NuciDAL.JsonRepository<ProcessedAttachmentDataObject>`

**Files:**
- **State file:** `StorageSettings.StateFilePath` — JSON array of `ProcessedAttachmentDataObject`
- **Checkpoint file:** `{StateFilePath}.checkpoint` — `ImapCheckpoint`

**Operations:**
| Method | Purpose |
|--------|---------|
| `IsProcessedAsync(attachmentId)` | Check if SHA256 exists in state |
| `MarkProcessedAsync(dataObject)` | Append to state array, persist |
| `GetCheckpointAsync()` | Load `ImapCheckpoint` |
| `SaveCheckpointAsync(checkpoint)` | Persist `ImapCheckpoint` |
| `ApplyRetentionAsync()` | Remove records older than `MaxAgeDays` or exceeding `MaxFiles` |

**Concurrency:** Single-writer (worker process), file-based locking not implemented (assumes single instance)

---

## Data Flow Summary

```
IMAP Message
    │
    ├─► EmailAgePolicy.IsWithinAgeLimit() ──► Skip if too old
    │
    ├─► BillProviderIdentification.IdentifyProvider() ──► Provider enum
    │
    ├─► AttachmentValidator.ValidateAsync() ──► Valid MimePart or null
    │       │
    │       ├─ MIME type check
    │       ├─ Size check
    │       ├─ PDF signature check
    │       └─ Filename regex (provider-specific)
    │
    ├─► AttachmentProcessor.ProcessAsync() ──► Local file path
    │       │
    │       ├─ SHA256 hash (AttachmentId)
    │       ├─ Sanitise filename
    │       ├─ Prefix with provider name
    │       ├─ Atomic write (temp → hash compare → move)
    │       └─ Deduplication (skip if identical content exists)
    │
    ├─► FileStateStore.IsProcessedAsync(AttachmentId) ──► Skip if already processed
    │
    ├─► BillSender.SendAsync() ──► NuciNotifications API
    │       │
    │       ├─ Load email-template.xml
    │       ├─ Render {ProviderName}
    │       ├─ Base64 encode PDF
    │       └─ POST /api/email with HMAC auth
    │
    └─► FileStateStore.MarkProcessedAsync() ──► Persist to state ledger
        │
        └─► FileStateStore.SaveCheckpointAsync() ──► Update IMAP checkpoint
```