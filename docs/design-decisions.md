# Design Decisions

## Overview

This document records significant architectural and design decisions, their rationale, and trade-offs. Each decision follows the format: **Context → Decision → Consequences**.

---

## 1. Worker Service over Web API

**Context:** Bill Relay processes emails from IMAP on a schedule. No HTTP endpoints needed.

**Decision:** Use .NET Generic Host with `BackgroundService` (worker pattern) instead of ASP.NET Core web host.

**Consequences:**
- ✅ Simpler deployment (no Kestrel, no ports, no reverse proxy)
- ✅ Lower memory footprint
- ✅ Natural fit for polling loop
- ❌ No built-in health endpoints (must implement separately)
- ❌ No metrics endpoint (Prometheus, etc.)

---

## 2. Sequential Polling Loop

**Context:** IMAP IDLE not used; polling interval configurable.

**Decision:** Single-threaded sequential processing in `BillRelayWorker.ExecuteAsync`:
1. Connect to IMAP
2. Search for unseen messages
3. Process each message sequentially
4. Disconnect
5. Wait `PollingIntervalMinutes`
6. Repeat

**Consequences:**
- ✅ Simple, predictable, easy to debug
- ✅ No concurrency complexity in IMAP session
- ✅ Natural backpressure (one message at a time)
- ❌ Throughput limited by slowest message
- ❌ Long-running message blocks entire cycle
- ❌ No parallel attachment downloads

**Mitigation:** Keep `PollingIntervalMinutes` reasonable (default 5). Consider `SemaphoreSlim` for future parallelization.

---

## 3. Checkpoint-Based IMAP Processing

**Context:** Must not reprocess emails; IMAP UIDs can reset (UIDVALIDITY change).

**Decision:** Persist checkpoint (`UidValidity`, `LastProcessedUid`, `LastProcessedDate`) in JSON state file via `FileStateStore`.

**Consequences:**
- ✅ Resumable after crash/restart
- ✅ Handles UIDVALIDITY reset (full rescan)
- ✅ No database required
- ❌ At-least-once delivery (checkpoint advances after processing, not after delivery)
- ❌ Single file = single point of failure (mitigated by atomic writes)

---

## 4. Content-Addressable Storage (SHA256)

**Context:** Deduplicate attachments across emails and providers.

**Decision:** Use SHA256 hash of attachment content as `AttachmentId`. Store files as `{AttachmentId}.pdf`.

**Consequences:**
- ✅ Automatic deduplication (same content = same file)
- ✅ Tamper-evident (hash mismatch = corruption)
- ✅ No filename collisions
- ❌ Filenames not human-readable (mitigated by provider prefix in display)
- ❌ Must read entire attachment to compute hash before saving

---

## 5. Provider-Specific Filename Regex

**Context:** Different providers use different naming conventions for bill PDFs.

**Decision:** Hardcoded regex patterns per provider in `BillProviderIdentification.IsBillAttachment`.

**Consequences:**
- ✅ Precise filtering per provider
- ✅ No false positives from generic "invoice" filenames
- ❌ New provider = code change + deploy
- ❌ Regex maintenance burden
- ❌ No runtime configurability

**Alternative Considered:** Configurable patterns in `appsettings.json` — rejected for v1 due to complexity.

---

## 6. E.ON Link-Based Download (Not Attachment)

**Context:** E.ON sends HTML email with link to PDF, not PDF attachment. The public invoice page at `www.eon.ro/descarca-factura` is fronted by a Cloudflare managed challenge that returns HTTP 403 to non-browser clients, including headless browsers.

**Decision:** Implement `IEOnBillDownloader` to extract the signed invoice token from the link in the HTML body and call the un-challenged invoice API (`POST https://api2.eon.ro/invoices/v1/guest/pdf`) directly, then decode the base64 PDF, validate, and wrap as `MimePart`.

**Consequences:**
- ✅ Handles E.ON's unique delivery method
- ✅ Avoids the Cloudflare challenge entirely (API host is not challenged)
- ✅ Reuses existing attachment pipeline
- ❌ Additional HTTP dependency (network, timeouts, TLS)
- ❌ Token extraction regex fragile to email template changes
- ❌ Depends on an undocumented E.ON API contract and public subscription key
- ❌ No attachment to validate upfront (download first, validate after)

---

## 7. At-Least-Once Delivery with Checkpoint Advancement

**Context:** Email delivery (NuciNotifications) can fail; IMAP checkpoint must advance to avoid infinite reprocessing.

**Decision:** Advance IMAP checkpoint (`LastProcessedUid`) after *processing* (attachment saved), not after *delivery*. Track delivery separately in state store with `delivery:` prefix.

**Consequences:**
- ✅ No stuck mailbox (checkpoint always advances)
- ✅ Delivery retries on next cycle (deduplication prevents duplicate sends)
- ❌ Possible duplicate delivery if crash between send and mark-processed
- ❌ Delivery failures not visible in IMAP state

**Flow:**
```
ProcessMessageAsync
  → Save attachment (FileStateStore.MarkProcessed "sha256:...")
  → DeliverAttachmentAsync
      → Send email (BillSender)
      → Mark delivery processed (FileStateStore.MarkProcessed "delivery:sha256:...")
  → Advance IMAP checkpoint (ImapCheckpoint.LastProcessedUid = uid)
```

---

## 8. Atomic File Writes (Temp + Verify + Move)

**Context:** Partial writes on crash leave corrupt files.

**Decision:** Write to `.tmp`, verify (size, PDF signature), then `File.Move` (atomic on same filesystem).

**Consequences:**
- ✅ No partial files visible to readers
- ✅ Crash during write = no file (clean retry)
- ✅ Verification catches download corruption
- ❌ Requires same filesystem for atomic move
- ❌ Double disk space during write (tmp + final)

---

## 9. NuciNotifications for Email Delivery

**Context:** Need reliable, signed email API with attachment support.

**Decision:** Use `NuciNotifications.Client` (internal package) with HMAC-signed requests.

**Consequences:**
- ✅ Centralized email infrastructure
- ✅ HMAC authentication (no passwords in email)
- ✅ Template support with Base64 attachments
- ❌ Internal dependency (single maintainer)
- ❌ Requires HTTPS endpoint, API key, HMAC secret
- ❌ No fallback email provider (SMTP, SendGrid, etc.)

---

## 10. File-Based State (NuciDAL JsonRepository)

**Context:** Simple persistence for processed attachments and IMAP checkpoint.

**Decision:** Use `NuciDAL.JsonRepository<ProcessedAttachmentDataObject>` with single JSON file.

**Consequences:**
- ✅ Zero infrastructure (no DB server)
- ✅ Human-readable/debuggable
- ✅ Easy backup (copy file)
- ❌ No concurrent access (single writer assumed)
- ❌ Full file rewrite on every save (O(n) growth)
- ❌ No transactions (partial write = corruption risk, mitigated by atomic write in NuciDAL)

**Scaling Limit:** ~10,000 records before performance degrades.

---

## 11. Diacritic-Normalized Keyword Matching

**Context:** Romanian provider names contain diacritics (România, Electrica, etc.).

**Decision:** Normalize both subject and keywords to ASCII (remove diacritics) before matching in `BillProviderIdentification.HasBillingSubject`.

**Consequences:**
- ✅ Matches "România" and "Romania" equally
- ✅ Handles encoding variations
- ❌ False positives possible (e.g., "factura" matches "facturare")
- ❌ Loses linguistic precision

---

## 12. Configuration via appsettings.json + Environment Variables

**Context:** 12-factor app configuration; secrets must not be in config files.

**Decision:** `ConfigurationBuilder` with JSON file + environment variables (prefix `BILLRELAY__`). Validation in `Program.cs` at startup.

**Consequences:**
- ✅ Standard .NET pattern
- ✅ Secrets via env vars (Docker, systemd, Kubernetes compatible)
- ✅ Startup validation fails fast
- ❌ No hot reload (restart required)
- ❌ No secret manager integration (Azure Key Vault, AWS Secrets Manager)

---

## 13. Structured Logging with NuciLog

**Context:** Need correlation IDs, operation tracking, structured fields for observability.

**Decision:** Use `NuciLog` with `BillRelayOperation` (8 operations) and `BillRelayInfoKey` (20 keys).

**Consequences:**
- ✅ Consistent operation names across components
- ✅ Structured fields enable querying/filtering
- ✅ Correlation via `CorrelationId` per message
- ❌ Custom logging library (not Serilog/NLog)
- ❌ No built-in log aggregation (ELK, Loki, etc.)

---

## 14. No Database

**Context:** Simple state: processed attachments (deduplication), IMAP checkpoint.

**Decision:** File-based JSON persistence only.

**Consequences:**
- ✅ Zero operational overhead
- ✅ Trivial backup/restore
- ✅ Works on any filesystem
- ❌ No query capability
- ❌ No horizontal scaling
- ❌ Single-writer assumption

---

## 15. Single Assembly (No Plugin Architecture)

**Context:** Fixed set of providers, fixed pipeline.

**Decision:** All providers, validators, downloaders in single assembly. No MEF, no plugins.

**Consequences:**
- ✅ Simple deployment (one file)
- ✅ No versioning complexity
- ✅ Compiler-enforced contracts
- ❌ New provider = rebuild + redeploy
- ❌ No third-party extensibility

---

## Decision Log

| Date | Decision | Author | Status |
|------|----------|--------|--------|
| 2024 | Worker service pattern | Team | Accepted |
| 2024 | Sequential polling | Team | Accepted |
| 2024 | Checkpoint-based IMAP | Team | Accepted |
| 2024 | SHA256 content addressing | Team | Accepted |
| 2024 | Provider regex patterns | Team | Accepted |
| 2024 | E.ON link download | Team | Accepted |
| 2024 | At-least-once delivery | Team | Accepted |
| 2024 | Atomic file writes | Team | Accepted |
| 2024 | NuciNotifications email | Team | Accepted |
| 2024 | File-based JSON state | Team | Accepted |
| 2024 | Diacritic normalization | Team | Accepted |
| 2024 | appsettings + env vars | Team | Accepted |
| 2024 | NuciLog structured logging | Team | Accepted |
| 2024 | No database | Team | Accepted |
| 2024 | Single assembly | Team | Accepted |

---

## Related Documentation

- [Architecture: Design Decisions](../architecture.md#design-decisions)
- [Invariants](./invariants.md) — System invariants derived from these decisions
- [Concurrency and Scheduling](./concurrency-and-scheduling.md) — Concurrency model
- [State and Persistence](./state-and-persistence.md) — Persistence design
- [Integrations](./integrations.md) — External integration decisions