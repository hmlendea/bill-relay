# Concurrency and Scheduling

## Concurrency Model

Bill Relay is **single-threaded by design**. All processing occurs on the `BackgroundService` thread pool thread.

```
┌─────────────────────────────────────────────────────────────┐
│                    BackgroundService                         │
│  (Single thread from ThreadPool)                             │
└──────────────────────────┬──────────────────────────────────┘
                           │
           ┌───────────────┼───────────────┐
           ▼               ▼               ▼
    ┌─────────────┐ ┌─────────────┐ ┌─────────────┐
    │ IMAP Client │ │ HTTP Client │ │ File System │
    │ (MailKit)   │ │ (HttpClient)│ │ (Sync I/O)  │
    └─────────────┘ └─────────────┘ └─────────────┘
           │               │               │
           └───────────────┼───────────────┘
                           ▼
                  ┌─────────────────┐
                  │  State Store    │
                  │ (JsonRepository)│
                  └─────────────────┘
```

## Thread Safety

### Not Thread-Safe (Single-Writer Assumption)

| Component | Reason |
|-----------|--------|
| `FileStateStore` | `JsonRepository` rewrites entire file; no locking |
| `AttachmentProcessor` | File moves, temp files; no concurrent write coordination |
| `BillRelayWorker` | Single `ExecuteAsync` loop; no parallel message processing |

### Thread-Safe (Immutable/Stateless)

| Component | Reason |
|-----------|--------|
| `BillProviderIdentification` | Static, pure functions |
| `AttachmentValidator` | Stateless validation |
| `EmailAgePolicy` | Stateless calculation |
| `IEOnBillDownloader` | Per-call `HttpClient` (via `IHttpClientFactory`) |
| `BillSender` | Stateless, uses `HttpClient` per call |

### DI Registration (All Singletons)

```csharp
// Program.cs
services.AddSingleton<IStateStore, FileStateStore>();
services.AddSingleton<IAttachmentProcessor, AttachmentProcessor>();
services.AddSingleton<IAttachmentValidator, AttachmentValidator>();
services.AddSingleton<IEOnBillDownloader, EOnBillDownloader>();
services.AddSingleton<IBillDelivery, BillDelivery>();
services.AddSingleton<IBillSender, BillSender>();
services.AddHostedService<BillRelayWorker>();
```

**Implication:** Single instance shared across all processing. Must be stateless or single-threaded.

## Scheduling

### Polling Loop

```csharp
// BillRelayWorker.ExecuteAsync
while (!stoppingToken.IsCancellationRequested)
{
    await ProcessInboxAsync(stoppingToken);
    await Task.Delay(TimeSpan.FromMinutes(settings.Polling.PollingIntervalMinutes), stoppingToken);
}
```

**Default Interval:** 5 minutes (`PollingSettings.PollingIntervalMinutes = 5`)

**Configurable:** `appsettings.json` → `Polling:PollingIntervalMinutes`

### Cycle Timeline

```
T=0s          T=30s         T=60s         T=5min        T=5min+30s
│             │             │             │             │
├─Connect IMAP│             │             │             │
├─Search      │             │             │             │
├─For each msg:              │             │             │
│  ├─Fetch    │             │             │             │
│  ├─Identify │             │             │             │
│  ├─Process  │             │             │             │
│  └─Deliver  │             │             │             │
├─Disconnect  │             │             │             │
├─Save chkpt  │             │             │             │
└─Delay ──────┴─────────────┴─────────────┴─────────────┘
```

### Timing Characteristics

| Phase | Typical Duration | Max (Config) |
|-------|------------------|--------------|
| IMAP Connect | 1-3s | Network timeout |
| Search Unseen | <1s | IMAP server |
| Fetch Message | 0.5-5s/message | Message size |
| Provider ID | <1ms | CPU |
| Attachment Download | 1-30s | `EOnBillDownloader` timeout (30s) |
| File Save | 10-100ms | Disk I/O |
| Email Send | 1-10s | NuciNotifications API |
| Checkpoint Save | 10-50ms | State size |

**Worst Case Cycle:** ~2-3 minutes (many large attachments, slow network)

## Cancellation

### Graceful Shutdown

```csharp
// BackgroundService.StopAsync
public override async Task StopAsync(CancellationToken cancellationToken)
{
    _logger.Info(BillRelayOperation.Shutdown, "Stopping worker...");
    await base.StopAsync(cancellationToken);
}
```

**Behavior:**
1. `ExecuteAsync` receives `stoppingToken`
2. Current `ProcessInboxAsync` completes (checks token at loop boundaries)
3. `Task.Delay` exits early
4. Loop exits, method returns
5. `StopAsync` completes

**No mid-message cancellation** — current message finishes processing.

### Cancellation Points

| Location | Checks Token |
|----------|--------------|
| `ExecuteAsync` loop | Yes (while condition) |
| `Task.Delay` | Yes (built-in) |
| `ProcessInboxAsync` | No (runs to completion) |
| `ProcessMailboxAsync` | No |
| `ProcessMessageAsync` | No |
| `DeliverAttachmentAsync` | No |
| `AttachmentProcessor.ProcessAsync` | **Yes** (passed through) |
| `EOnBillDownloader.DownloadBillAsync` | **Yes** (passed to HTTP) |
| `BillSender.SendAsync` | **Yes** (passed to HTTP) |

**Design Decision:** Coarse-grained cancellation (cycle-level) for simplicity. Fine-grained would require plumbing token through all layers.

## Concurrency Hazards

### Avoided by Design

| Hazard | Prevention |
|--------|------------|
| Multiple workers same mailbox | Single process deployment |
| Concurrent state file writes | Single writer (singleton) |
| IMAP session conflicts | Single thread, sequential |
| File system races | Sequential processing, atomic moves |
| Duplicate deliveries | Deduplication in state store |

### Not Addressed (Known Limitations)

| Hazard | Impact | Mitigation |
|--------|--------|------------|
| External process modifies state file | Corruption | File permissions, single deployment |
| External process modifies download dir | Orphan files | File permissions |
| Clock skew | Age filter errors | NTP on host |
| Long message blocks cycle | Delayed processing | Keep messages small, monitor cycle time |

## Parallelization Opportunities (Future)

### Message-Level Parallelism

```csharp
// Potential future: SemaphoreSlim for bounded parallelism
var semaphore = new SemaphoreSlim(maxConcurrency);
var tasks = messages.Select(async msg => {
    await semaphore.WaitAsync(token);
    try { await ProcessMessageAsync(msg, token); }
    finally { semaphore.Release(); }
});
await Task.WhenAll(tasks);
```

**Requirements:**
- Thread-safe `FileStateStore` (locking or concurrent dictionary + batch save)
- Per-message IMAP fetch (separate connections or pipelining)
- Correlation ID per task for logging

### Attachment-Level Parallelism

```csharp
// Within single message: download multiple attachments in parallel
var downloadTasks = attachments.Select(a => downloader.DownloadAsync(a, token));
var results = await Task.WhenAll(downloadTasks);
```

**Requirements:**
- `IEOnBillDownloader` thread-safe (stateless, uses `IHttpClientFactory`)
- `AttachmentProcessor` thread-safe (per-file atomic writes already safe)

## Resource Management

### IMAP Connection

- **Lifecycle:** Per-cycle (connect → process → disconnect)
- **Pooling:** None (MailKit doesn't pool)
- **Timeout:** `ImapSettings.ConnectionTimeoutSeconds` (default 30s)

### HTTP Client

- **Lifecycle:** `IHttpClientFactory` → pooled handlers
- **Configuration:** `Program.cs` adds `HttpClient` with timeout
- **E.ON Downloader:** Uses named client `EOnBillDownloader`

```csharp
// Program.cs
services.AddHttpClient<IEOnBillDownloader, EOnBillDownloader>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
```

### File Handles

- **Pattern:** Open → Read/Write → Close (using `using` / `FileStream`)
- **No long-lived handles** — each operation opens/closes

## Monitoring Concurrency

### Key Metrics (via Logs)

| Metric | Log Operation | Info Keys |
|--------|---------------|-----------|
| Cycle duration | `PollInbox` | `DurationMs` |
| Messages per cycle | `PollInbox` | `MessageCount` |
| Processing time/msg | `ProcessMessage` | `DurationMs`, `AttachmentCount` |
| Queue depth (unseen) | `PollInbox` | `UnseenCount` |

### Alerting Rules (Recommended)

```yaml
# Alert if cycle exceeds polling interval (backlog growing)
- alert: BillRelayCycleOverrun
  expr: billrelay_cycle_duration_seconds > billrelay_polling_interval_seconds
  for: 2m

# Alert if no cycles for 2x interval (worker stuck/dead)
- alert: BillRelayNoCycles
  expr: time() - billrelay_last_cycle_timestamp > 2 * billrelay_polling_interval_seconds
  for: 5m
```

## Related Documentation

- [Design Decisions](./design-decisions.md) — Sequential polling decision
- [Invariants](./invariants.md) — Concurrency invariants (I-22, I-23)
- [State and Persistence](./state-and-persistence.md) — State store concurrency
- [Architecture: Concurrency Model](../architecture.md#concurrency-model)
- [BillRelayWorker](../components/bill-relay-worker.md) — Worker implementation