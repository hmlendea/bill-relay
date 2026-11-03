# Application Services

## Overview

The `BillRelay.Services` namespace contains the core business logic services. All are registered as singletons and designed for single-threaded use.

## Service Layer Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    BillRelayWorker                           │
│  (BackgroundService - orchestrates the pipeline)             │
└──────────────────────────┬──────────────────────────────────┘
                           │
        ┌──────────────────┼──────────────────┐
        ▼                  ▼                  ▼
┌───────────────┐  ┌───────────────┐  ┌───────────────┐
│ IBillDelivery │  │IAttachmentProc│  │IStateStore    │
│ BillDelivery  │  │AttachmentProc │  │FileStateStore │
└───────┬───────┘  └───────┬───────┘  └───────┬───────┘
        │                  │                  │
        ▼                  ▼                  ▼
┌───────────────┐  ┌───────────────┐  ┌───────────────┐
│ IBillSender   │  │IAttachmentVal │  │JsonRepository │
│ BillSender    │  │AttachmentVal  │  │(NuciDAL)      │
└───────────────┘  └───────────────┘  └───────────────┘
        │                  ▲
        ▼                  │
┌───────────────┐  ┌───────────────┐
│NuciNotifications│  │IEOnBillDownloader│
│Client         │  │EOnBillDownloader │
└───────────────┘  └───────────────┘
```

## Service Interfaces and Implementations

| Interface | Implementation | Purpose |
|-----------|----------------|---------|
| `IStateStore` | `FileStateStore` | Persistence (attachments, deliveries, checkpoint) |
| `IAttachmentProcessor` | `AttachmentProcessor` | Save attachments, deduplication, hashing |
| `IAttachmentValidator` | `AttachmentValidator` | Validate MIME, size, PDF signature |
| `IEOnBillDownloader` | `EOnBillDownloader` | Download E.ON bills from links |
| `IBillDelivery` | `BillDelivery` | Orchestrate process → send → mark delivered |
| `IBillSender` | `BillSender` | Send email via NuciNotifications |

## Cross-Cutting Concerns

### Logging
All services use `ILogger` with `BillRelayOperation` and `BillRelayInfoKey`.

### Cancellation
`CancellationToken` plumbed through all async methods (except `BillRelayWorker` internal methods).

### Error Handling
- Validation errors → return failure reason / throw
- Transient errors (network) → log, throw → caller retries next cycle
- No internal retry logic

## Pipeline Flow

```
BillRelayWorker.ExecuteAsync
    │
    ├─ ProcessInboxAsync
    │   ├─ Connect IMAP
    │   ├─ Search unseen
    │   └─ ProcessMailboxAsync
    │       ├─ Load checkpoint
    │       ├─ For each message:
    │       │   └─ ProcessMessageAsync
    │       │       ├─ Identify provider (BillProviderIdentification)
    │       │       ├─ Get attachments
    │       │       ├─ For each attachment:
    │       │       │   ├─ Validate (IAttachmentValidator)
    │       │       │   ├─ If E.ON link: Download (IEOnBillDownloader)
    │       │       │   ├─ Process (IAttachmentProcessor)
    │       │       │   └─ Deliver (IBillDelivery)
    │       │       │       ├─ Check delivery dedup (IStateStore)
    │       │       │       ├─ Process attachment (IAttachmentProcessor)
    │       │       │       ├─ Send email (IBillSender)
    │       │       │       └─ Mark delivery processed (IStateStore)
    │       │       └─ Update checkpoint (IStateStore)
    │       └─ Save checkpoint (IStateStore)
    │           └─ Apply retention (IStateStore)
    └─ Delay (PollingIntervalMinutes)
```

## Static Utilities

| Class | Purpose |
|-------|---------|
| `BillProviderIdentification` | Provider detection from subject/sender/links |
| `EmailAgePolicy` | Eligibility cutoff calculation |

## Dependency Injection

All services registered as **singletons** in `Program.cs`:

```csharp
services.AddSingleton<IStateStore, FileStateStore>();
services.AddSingleton<IAttachmentProcessor, AttachmentProcessor>();
services.AddSingleton<IAttachmentValidator, AttachmentValidator>();
services.AddSingleton<IEOnBillDownloader, EOnBillDownloader>();
services.AddSingleton<IBillDelivery, BillDelivery>();
services.AddSingleton<IBillSender, BillSender>();
services.AddHostedService<BillRelayWorker>();
```

**Rationale:** Single-threaded worker, no concurrency, shared state (FileStateStore) must be single instance.

## Testing

Each service has:
- **Unit Tests:** `BillRelay.UnitTests` (mocked dependencies)
- **Integration Tests:** `BillRelay.IntegrationTests` (real filesystem, real NuciDAL)

## Related Documentation

- [Components](../components/) — Individual service docs
- [Flows](../flows/) — Pipeline flows
- [Host and Composition](../components/host-and-composition.md) — DI registration
- [Architecture: Service Layer](../architecture.md#service-layer)