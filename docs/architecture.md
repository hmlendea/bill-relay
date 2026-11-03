# Bill Relay Architecture

High-level architecture documentation complementing the root [ARCHITECTURE.md](../ARCHITECTURE.md).

## Purpose

This document elaborates on the architectural concepts introduced in the root ARCHITECTURE.md, providing deeper implementation detail for maintainers and contributors.

## System Boundary

Bill Relay is a single-process .NET 10 hosted worker. The process boundary encompasses:

- **IMAP client** — Connects to configured mailbox using MailKit
- **Message processor** — Classifies, validates, and extracts attachments
- **File persistence** — Saves validated PDFs and JSON state to local filesystem
- **Notification client** — Submits emails via NuciNotifications HTTPS API
- **Logging** — Structured logging via NuciLog

External systems (IMAP server, NuciNotifications API, SMTP) are outside the process boundary.

## Architectural Style

**Service-oriented pipeline** with explicit interface boundaries:

```
Program (composition root)
  └── BillRelayWorker (orchestration)
        ├── BillProviderIdentification (classification)
        ├── IAttachmentValidator (validation)
        ├── IAttachmentProcessor (file persistence)
        ├── IBillDelivery (delivery coordination)
        │     ├── IAttachmentProcessor
        │     ├── IBillSender (NuciNotifications)
        │     └── IStateStore (deduplication)
        └── IStateStore (checkpoint persistence)
```

Key characteristics:
- **Interface-segregated** — Each responsibility has a dedicated interface (`IStateStore`, `IBillDelivery`, `IBillSender`, `IAttachmentProcessor`, `IAttachmentValidator`, `IEOnBillDownloader`)
- **Singleton services** — All services registered as singletons in DI container
- **BackgroundService** — Worker runs as hosted service with cancellation support
- **No database** — Persistence is file-based (JSON + PDF files)

## Component Responsibilities

| Component | Responsibility | Key Types |
|-----------|---------------|-----------|
| `Program` | Composition root, configuration validation, DI registration | `Program` |
| `BillRelayWorker` | IMAP polling, message iteration, checkpoint management | `BillRelayWorker` |
| `BillProviderIdentification` | Provider classification via subject/link analysis | `BillProviderIdentification` |
| `AttachmentValidator` | MIME type, size, PDF signature validation | `AttachmentValidator` |
| `AttachmentProcessor` | Atomic file write, deduplication, sanitisation | `AttachmentProcessor` |
| `BillDelivery` | Deduplication check, process coordination | `BillDelivery` |
| `BillSender` | Email template rendering, NuciNotifications submission | `BillSender` |
| `FileStateStore` | JSON checkpoint + processed attachment ledger | `FileStateStore` |
| `EOnBillDownloader` | HTTP download for E.ON bills (link-based) | `EOnBillDownloader` |

## Data Flow

```
IMAP Message
    │
    ▼
BillProviderIdentification.IdentifyProvider()
    │
    ├── No match → Skip (log)
    │
    ▼
BillProviderIdentification.IsBillAttachment() per attachment
    │
    ├── Invalid → Skip (log rejection reason)
    │
    ▼
AttachmentValidator.GetFailureReason() — MIME, size, PDF signature
    │
    ├── Invalid → Skip (log rejection reason)
    │
    ▼
BillDelivery.DeliverAsync()
    │
    ├── Already processed (IStateStore.IsProcessedAsync) → Skip
    │
    ▼
AttachmentProcessor.ProcessAsync() — Atomic write to ./bills/
    │
    ▼
BillSender.SendAsync() — Template render → NuciNotifications
    │
    ▼
IStateStore.MarkProcessedAsync() — Record delivery ID
    │
    ▼
FileStateStore.SaveCheckpointAsync() — Update IMAP UID checkpoint
```

## Configuration Architecture

Configuration loaded from `appsettings.json` + environment variables (env vars override).

Settings classes (all in `BillRelay.Configuration`):
- `BillRelaySettings` — Root, aggregates all sections
- `ImapSettings` — Server, port, mailbox, credentials, SSL, cert validation
- `PollingSettings` — Interval, max email age, initial processing date
- `FilterSettings` — Enabled providers, sender/subject patterns (currently unused in code)
- `AttachmentSettings` — Permitted MIME types, max size, download timeout
- `StorageSettings` — Download directory, state file path
- `RetentionSettings` — Max age days, max files for state cleanup
- `DeliverySettings` — Recipient, sender name, template path
- `NuciNotificationsSettings` — Base URL, API key, HMAC secret (validated at startup)

## Persistence Architecture

Two independent file stores:

1. **Checkpoint file** (`{stateFilePath}.checkpoint`) — `ImapCheckpoint` JSON with `UidValidity`, `LastProcessedUid`, `LastProcessedDate`
2. **State file** (`stateFilePath`) — Array of `ProcessedAttachmentDataObject` (`Id`, `MessageId`, `ProcessedAt`)

Both use atomic write (temp file + move). `FileStateStore` wraps `NuciDAL.JsonRepository`.

Retention policy applied on-demand (not automatic): removes records older than `maxAgeDays` and excess beyond `maxFiles`.

## Integration Boundaries

| Boundary | Protocol | Direction | Contract |
|----------|----------|-----------|----------|
| IMAP Server | IMAP over TLS | Outbound | MailKit `ImapClient` |
| NuciNotifications | HTTPS + HMAC | Outbound | `INuciNotificationsClient.SendEmail()` |
| Local Filesystem | File I/O | Outbound | PDF + JSON files |
| Logging | Structured log events | Outbound | NuciLog `ILogger` |

## Concurrency Model

- Single worker thread (BackgroundService.ExecuteAsync)
- Sequential message processing per poll cycle
- CancellationToken propagated throughout
- No parallel processing of messages
- File operations use atomic write patterns

## Error Handling Strategy

- **Transient IMAP errors** — Caught, logged, checkpoint preserved, retry next cycle
- **Validation failures** — Per-attachment, logged, continue to next attachment
- **Delivery failures** — Exception logged, rethrown, checkpoint NOT advanced (message reprocessed next cycle)
- **E.ON download failures** — Logged, message skipped, checkpoint advanced
- **Cancellation** — Clean shutdown, checkpoint preserved

## Extension Points

1. **New providers** — Add regex patterns in `BillProviderIdentification`, add to `FilterSettings.Providers`
2. **New delivery channels** — Implement `IBillSender`, register in DI
3. **New attachment validators** — Implement `IAttachmentValidator`, register in DI
4. **New state stores** — Implement `IStateStore`, register in DI

## Related Documentation

- [Root ARCHITECTURE.md](../ARCHITECTURE.md) — System context, runtime flow diagrams, deployment
- [Components](./components/) — Per-component deep dives
- [Flows](./flows/) — End-to-end execution sequences
- [Data Model](./data-model.md) — Domain entities and persistence
- [Configuration](./configuration.md) — Configuration schema and precedence