# Invariants

## System Invariants

These invariants must hold at all times during correct operation. Violations indicate bugs or data corruption.

---

## 1. State Store Invariants

### I-1: Processed Attachment Uniqueness
**Invariant:** Each `AttachmentId` (SHA256) appears at most once in `ProcessedAttachments`.

**Enforced by:** `FileStateStore.MarkProcessed` checks `IsProcessed` before adding.

**Violation Impact:** Duplicate file entries, wasted disk space, potential delivery duplicates.

---

### I-2: Delivery Record Uniqueness
**Invariant:** Each `delivery:{AttachmentId}` appears at most once in `ProcessedAttachments`.

**Enforced by:** `BillDelivery.DeliverAsync` checks `IsProcessed("delivery:" + attachmentId)` before sending.

**Violation Impact:** Duplicate emails sent for same attachment.

---

### I-3: Checkpoint Monotonicity
**Invariant:** `ImapCheckpoint.LastProcessedUid` never decreases for a given `UidValidity`.

**Enforced by:** `FileStateStore.SaveCheckpoint` only updates if `uid > LastProcessedUid`.

**Violation Impact:** Reprocessing old emails, infinite loops, duplicate deliveries.

---

### I-4: UID Validity Consistency
**Invariant:** If `UidValidity` changes, `LastProcessedUid` resets to 0.

**Enforced by:** `BillRelayWorker.ProcessMailboxAsync` detects mismatch and creates new checkpoint.

**Violation Impact:** Processing gaps or duplicates after mailbox migration/rebuild.

---

### I-5: State File Atomicity
**Invariant:** State file (`billrelay.json`) is always valid JSON after any write.

**Enforced by:** `NuciDAL.JsonRepository` writes to temp file + atomic rename.

**Violation Impact:** Corrupt state, startup failure, data loss.

---

## 2. File Storage Invariants

### I-6: File-Content Hash Match
**Invariant:** For every file `{AttachmentId}.pdf` in `DownloadDirectory`, `SHA256(file) == AttachmentId`.

**Enforced by:** `AttachmentProcessor.ProcessAsync` verifies hash after write.

**Violation Impact:** Corrupted files, failed deduplication, tampering undetected.

---

### I-7: No Orphan Files
**Invariant:** Every file in `DownloadDirectory` has a corresponding entry in `ProcessedAttachments` (or is a stale `.tmp` file).

**Enforced by:** `AttachmentProcessor` only creates files after `MarkProcessed`; retention cleans both.

**Violation Impact:** Disk space leak, inconsistent state.

---

### I-8: Atomic File Creation
**Invariant:** No partial PDF files visible in `DownloadDirectory`.

**Enforced by:** Write to `.tmp`, verify, then `File.Move` (atomic on POSIX/Windows same volume).

**Violation Impact:** Readers see incomplete files, PDF validation fails.

---

## 3. IMAP Processing Invariants

### I-9: Message Processing Idempotency
**Invariant:** Processing the same message UID twice produces same outcome (no side effects on second run).

**Enforced by:** `ProcessMessageAsync` → `DeliverAttachmentAsync` checks `IsProcessed` for both attachment and delivery.

**Violation Impact:** Duplicate file saves, duplicate emails.

---

### I-10: Checkpoint Advances After Processing
**Invariant:** `LastProcessedUid` is updated to the highest successfully processed UID in each cycle.

**Enforced by:** `ProcessMailboxAsync` updates checkpoint after each message (or batch).

**Violation Impact:** Reprocessing on restart, duplicate work.

---

### I-11: Age Filter Consistency
**Invariant:** Messages older than `InitialProcessingDate` (or checkpoint date) are never processed.

**Enforced by:** `EmailAgePolicy.IsEligibleForProcessing` used in `ProcessMailboxAsync` search filter.

**Violation Impact:** Processing historical emails on first run, unexpected volume.

---

## 4. Attachment Processing Invariants

### I-12: Only PDF Attachments Stored
**Invariant:** Every saved file has MIME type `application/pdf` and valid PDF signature (`%PDF-`).

**Enforced by:** `AttachmentValidator.GetFailureReason` checks MIME type allowlist and PDF signature.

**Violation Impact:** Non-PDF files in storage, downstream processing failures.

---

### I-13: Size Limit Enforced
**Invariant:** No attachment larger than `AttachmentSettings.MaximumSizeBytes` is saved.

**Enforced by:** `AttachmentValidator.GetFailureReason` checks `Content.Length`.

**Violation Impact:** Disk exhaustion, memory pressure during processing.

---

### I-14: Provider Identification Deterministic
**Invariant:** Same (subject, sender, links) → same provider (or Unknown).

**Enforced by:** `BillProviderIdentification.IdentifyProvider` is pure function.

**Violation Impact:** Inconsistent provider prefixes, routing errors.

---

### I-15: Filename Sanitization Safe
**Invariant:** Sanitized filename contains only `[A-Za-z0-9._-]` and ends with `.pdf`.

**Enforced by:** `AttachmentProcessor.SanitizeFileName` regex replacement.

**Violation Impact:** Path traversal, filesystem errors, injection.

---

## 5. Delivery Invariants

### I-16: Delivery Only After Storage
**Invariant:** `BillSender.SendAsync` called only after attachment saved to `DownloadDirectory`.

**Enforced by:** `BillDelivery.DeliverAsync` calls `AttachmentProcessor.ProcessAsync` first.

**Violation Impact:** Email sent with missing attachment, broken links.

---

### I-17: Template Placeholder Replaced
**Invariant:** Email body contains no `{ProviderName}` placeholder after template processing.

**Enforced by:** `BillSender.SendAsync` replaces `{ProviderName}` with identified provider.

**Violation Impact:** Raw placeholder in delivered email.

---

### I-18: Attachment Base64 Encoded
**Invariant:** Email attachment payload is valid Base64 of PDF content.

**Enforced by:** `BillSender.SendAsync` reads file → `Convert.ToBase64String`.

**Violation Impact:** Corrupted attachment in email, recipient cannot open.

---

## 6. Configuration Invariants

### I-19: Required Settings Present
**Invariant:** All required configuration sections bind without error at startup.

**Enforced by:** `Program.cs` validates `BillRelaySettings` and `NuciNotificationsSettings` before DI registration.

**Violation Impact:** Startup failure (fail-fast), no partial operation.

---

### I-20: NuciNotifications HTTPS Only
**Invariant:** `NuciNotificationsSettings.BaseUrl` uses `https://` scheme.

**Enforced by:** `Program.cs` validation throws if not HTTPS.

**Violation Impact:** Credentials sent in cleartext, MITM vulnerability.

---

### I-21: HMAC Secret Non-Empty
**Invariant:** `NuciNotificationsSettings.HmacSharedSecretKey` is not null/empty.

**Enforced by:** `Program.cs` validation.

**Violation Impact:** Unsigned requests rejected by API, delivery fails.

---

## 7. Concurrency Invariants

### I-22: Single Writer to State File
**Invariant:** Only one `BillRelay` process writes to `StateFilePath` at a time.

**Enforced by:** Deployment model (single instance). No file locking.

**Violation Impact:** Lost updates, corrupt JSON, checkpoint regression.

---

### I-23: Sequential Message Processing
**Invariant:** Messages processed one at a time within a single IMAP session.

**Enforced by:** `BillRelayWorker.ProcessMailboxAsync` uses `foreach` (no `Parallel.ForEach`).

**Violation Impact:** IMAP protocol violations, race conditions in state store.

---

## 8. Retention Invariants

### I-24: Retention Preserves Recent
**Invariant:** After retention, at least `RetentionSettings.MaximumCount` most recent entries remain (if more exist).

**Enforced by:** `FileStateStore.ApplyRetention` orders by `ProcessedDate` descending, keeps top N.

**Violation Impact:** Premature deletion of recent records, duplicate processing.

---

### I-25: Retention Preserves Young
**Invariant:** After retention, no entry younger than `RetentionSettings.MaximumAgeDays` is deleted.

**Enforced by:** `FileStateStore.ApplyRetention` filters by age before count.

**Violation Impact:** Loss of deduplication for recent emails, duplicate deliveries.

---

## 9. Logging Invariants

### I-26: Correlation ID Per Message
**Invariant:** All log entries for a single email message share the same `CorrelationId`.

**Enforced by:** `BillRelayWorker.ProcessMessageAsync` creates scope with `CorrelationId = uid.ToString()`.

**Violation Impact:** Cannot trace message flow in logs.

---

### I-27: Operation Names Consistent
**Invariant:** Every log entry uses a `BillRelayOperation` enum value.

**Enforced by:** All services use `BillRelayOperation.*` constants.

**Violation Impact:** Unqueryable logs, broken dashboards.

---

## Invariant Verification

### Startup Checks (Program.cs)
- Configuration validation (I-19, I-20, I-21)
- State file readability (I-5)
- Directory writability (I-6, I-7)

### Runtime Assertions (Debug Builds)
```csharp
// In AttachmentProcessor
Debug.Assert(ComputeSha256(filePath) == attachmentId, "I-6 violated");

// In FileStateStore
Debug.Assert(IsValidJson(stateFilePath), "I-5 violated");
```

### Integration Test Coverage
| Invariant | Test |
|-----------|------|
| I-1, I-2 | `FileStateStoreIntegrationTests` |
| I-3, I-4 | `BillRelayWorker` integration test (manual) |
| I-6, I-8 | `AttachmentProcessorTests` |
| I-12, I-13 | `AttachmentValidator` tests |
| I-16, I-17, I-18 | `BillDeliveryTests`, `BillSenderTests` |
| I-24, I-25 | `FileStateStoreIntegrationTests.Retention` |

---

## Related Documentation

- [Design Decisions](./design-decisions.md) — Decisions that establish these invariants
- [State and Persistence](./state-and-persistence.md) — Persistence model
- [Concurrency and Scheduling](./concurrency-and-scheduling.md) — Concurrency model
- [Testing](./testing.md) — Test coverage for invariants
- [Architecture: Invariants and Guarantees](../architecture.md#invariants-and-guarantees)