# Logging

## Logging Framework

**Library:** NuciLog (via `NuciLog.Core` and `NuciLog` packages)

**Configuration Source:** `NuciLoggerSettings` section in `appsettings.json` + environment variables

**Integration:** `builder.Services.AddLogging(logging => logging.AddNuciLog())` in `Program.cs`

## Log Levels

| Level | Usage | Examples |
|-------|-------|----------|
| **Debug** | Detailed diagnostics, rejection reasons | Subject/sender/provider mismatch details, individual attachment validation failures, checkpoint load/save |
| **Info** | Operational milestones | Poll started, message count, provider identified, attachment saved, email sent, retention applied |
| **Warning** | Recoverable anomalies | (Currently not used explicitly) |
| **Error** | Failures requiring attention | IMAP connection/auth failures, file write failures, NuciNotifications API errors, template load failures |
| **Critical** | Process-threatening failures | (Currently not used explicitly) |

## Structured Fields

Every log entry includes standard properties via `LogInfo` key-value pairs:

### Operation Context (`BillRelayOperation`)

| Operation | Description |
|-----------|-------------|
| `RunWorker` | Worker lifecycle |
| `ProcessInbox` | Poll cycle |
| `ProcessAttachment` | End-to-end attachment handling |
| `ValidateAttachment` | MIME/size/PDF validation |
| `WriteAttachment` | Temp file write |
| `SaveAttachment` | Atomic move to final location |
| `InitializeStateStore` | State store setup |
| `ApplyRetention` | State cleanup |

### Information Keys (`BillRelayInfoKey`)

| Key | Type | Description | Sensitivity |
|-----|------|-------------|-------------|
| `AttachmentId` | string | SHA256 hash of PDF content | Low |
| `FilePath` | string | Local filesystem path | Low |
| `FileName` | string | Original attachment filename | Low |
| `StateFilePath` | string | Path to state JSON | Low |
| `MessageCount` | int | Messages in mailbox | Low |
| `MessageId` | string | IMAP Message-ID header | Low |
| `Subject` | string | Email subject | **Medium** (may contain PII) |
| `Sender` | string | Sender email address | **Medium** (PII) |
| `ProviderName` | string | Identified provider | Low |
| `Recipient` | string | Delivery recipient | **Medium** (PII) |
| `AttachmentCount` | int | Attachments in message | Low |
| `CandidateCount` | int | Messages matching search | Low |
| `EligibleMessageCount` | int | Messages passing filters | Low |
| `RecordCount` | int | State records affected | Low |
| `ContentLength` | long | Decoded attachment bytes | Low |
| `EncodedContentLength` | long | Base64 attachment bytes | Low |
| `Url` | string | E.ON download URL | Low |
| `MaximumSizeBytes` | long | Configured size limit | Low |
| `Reason` | string | Failure/rejection reason | Low |

## Correlation

**Current Implementation:** No explicit correlation IDs (trace IDs, span IDs) are propagated across components.

**Implicit Correlation:**
- `AttachmentId` (SHA256) links file storage, delivery, and state records
- `MessageId` (IMAP Message-ID) links message processing to delivery record
- `ProviderName` groups related operations

**Gap:** No distributed tracing across IMAP → processing → API boundaries.

## Sinks/Destinations

Configured via `NuciLoggerSettings`:

| Setting | Default | Description |
|---------|---------|-------------|
| `MinimumLevel` | `"Debug"` | Minimum log level |
| `LogFilePath` | `"logfile.log"` | File output path |
| `IsFileOutputEnabled` | `true` | Enable file sink |

**Actual sinks depend on NuciLog implementation** — typically:
- Console (stdout/stderr)
- File (`logfile.log` in working directory)

## Redaction/Masking

**NuciLog provides sensitive-field masking** via `LogInfoKey` mechanism.

**Current Usage:** No fields are explicitly marked as sensitive in `BillRelayInfoKey`. All fields logged as-is.

**Risk:** `Subject`, `Sender`, `Recipient` may contain PII and are logged at Info/Debug level.

**Recommendation:** Mark PII fields with sensitive flag if NuciLog supports it, or avoid logging at Info level.

## Retention

**No automatic log retention/rotation** implemented in Bill Relay.

**Operator Responsibility:**
- Configure external log rotation (logrotate, systemd, etc.)
- Monitor disk usage for `logfile.log`
- Archive/prune based on operational policy

## Sampling

**No log sampling implemented.** All events at or above `MinimumLevel` are emitted.

## Performance Considerations

- **Sync vs Async:** NuciLog behavior depends on configuration; verify async file writing
- **Structured Logging:** Key-value pairs avoid string interpolation overhead
- **Debug Level:** Verbose; consider `MinimumLevel: "Information"` in production
- **File I/O:** Each log entry may cause file write; batch/buffer if high volume

## Error Logging

**Exception Logging Pattern:**
```csharp
logger.Error(
    BillRelayOperation.ProcessAttachment,
    OperationStatus.Failure,
    exception,  // Full exception with stack trace
    new LogInfo(BillRelayInfoKey.AttachmentId, attachmentId),
    new LogInfo(BillRelayInfoKey.Reason, "Description"));
```

- Includes full exception (type, message, stack trace)
- Structured context via `LogInfo` keys
- `OperationStatus.Failure` for filtering

## Audit Logging

**No separate audit log.** Security-relevant events logged at Info level:
- IMAP authentication (success/failure)
- Email sent (recipient, provider)
- State changes (checkpoint, delivery records)

**Gap:** No tamper-evident audit trail for compliance.

## Configuration

**Location:** `appsettings.json` → `NuciLoggerSettings` section

```json
{
  "nuciLoggerSettings": {
    "minimumLevel": "Debug",
    "logFilePath": "logfile.log",
    "isFileOutputEnabled": true
  }
}
```

**Environment Override:**
```bash
BILLRELAY__NUCILOGGERSETTINGS__MINIMUMLEVEL=Information
BILLRELAY__NUCILOGGERSETTINGS__LOGFILEPATH=/var/log/billrelay.log
BILLRELAY__NUCILOGGERSETTINGS__ISFILEOUTPUTENABLED=true
```

**Runtime Level Change:** Not supported (requires restart).

## Related Documentation

- [Components: BillRelayWorker](./components/bill-relay-worker.md) — Logging in worker
- [Components: AttachmentProcessor](./components/attachment-processor.md) — File operation logging
- [Components: BillDelivery](./components/bill-delivery.md) — Delivery logging
- [Architecture: Observability](../architecture.md#observability)
- [Error Handling](./error-handling.md) — Exception logging patterns