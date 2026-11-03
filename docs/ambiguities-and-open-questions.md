# Ambiguities and Open Questions

## Purpose

Document known uncertainties, design tensions, and areas requiring future decisions. Not all have immediate resolutions.

---

## 1. Provider Extensibility

**Question:** Should provider identification be configurable (appsettings.json) instead of hardcoded?

**Current:** Hardcoded regex/keywords in `BillProviderIdentification.cs`

**Trade-offs:**
- Configurable: No deploy for new provider, but complex regex in JSON, validation needed
- Hardcoded: Type-safe, tested, but requires deploy

**Decision Deferred:** v1 scope is fixed providers. Revisit when >10 providers.

---

## 2. E.ON Link Extraction Fragility

**Question:** How to handle E.ON email template changes breaking link regex?

**Current:** Single regex pattern in `EOnBillDownloader.cs`

**Risks:**
- E.ON changes email format → silent failure (no attachment found)
- No alerting on extraction failure

**Options:**
- Multiple fallback regex patterns
- HTML parsing (AngleSharp) instead of regex
- Monitoring/alert on "No bill download link found" log
- Periodic test with saved `.eml` samples

**Decision Deferred:** Monitor failure rate first.

---

## 3. At-Least-Once vs Exactly-Once Delivery

**Question:** Is at-least-once delivery acceptable, or should we implement exactly-once?

**Current:** At-least-once (checkpoint advances before delivery confirmation)

**Impact:** Rare duplicate emails (crash between send and mark-delivered)

**Exactly-Once Options:**
- Transactional outbox pattern (state + delivery in same atomic write)
- Idempotency key in NuciNotifications API (if supported)
- Two-phase commit (overkill)

**Decision:** At-least-once accepted for v1. Duplicates rare, deduplication works.

---

## 4. State Store Scalability

**Question:** When does JSON file become a bottleneck?

**Current:** `NuciDAL.JsonRepository` loads entire file, rewrites on every save

**Thresholds:**
- ~10,000 entries: Noticeable latency
- ~50,000 entries: Memory pressure
- ~100,000 entries: Unusable

**Migration Path:** SQLite (EF Core) — schema compatible with `ProcessedAttachmentDataObject`

**Trigger:** Monitor state file size and `SaveCheckpoint` duration in logs.

---

## 5. Retention Policy Correctness

**Question:** Does current retention (age first, then count) match business requirements?

**Current:** Keep items younger than `MaximumAgeDays`, then keep newest `MaximumCount`

**Alternative:** Keep newest `MaximumCount` regardless of age, then apply age filter

**Difference:** Current protects recent items; alternative protects total count.

**Decision Deferred:** Confirm with stakeholders. Current is safer (never deletes young items).

---

## 6. IMAP IDLE vs Polling

**Question:** Should we use IMAP IDLE (push) instead of polling?

**Current:** Polling every 5 minutes

**IDLE Benefits:**
- Near-real-time processing
- Fewer IMAP connections
- Lower latency

**IDLE Drawbacks:**
- Long-lived connection (firewall/timeouts)
- Reconnection logic complexity
- Single connection = single thread still
- No benefit if processing is slow

**Decision:** Polling sufficient for v1. IDLE for v2 if latency becomes requirement.

---

## 7. Multi-Mailbox Support

**Question:** Should one instance poll multiple mailboxes?

**Current:** Single IMAP account per instance

**Options:**
- Multiple `BillRelayWorker` instances (separate processes)
- Single worker with multiple IMAP clients (complex state)
- Configuration-driven mailbox list

**Decision Deferred:** Deploy multiple instances for now (systemd units, Docker containers).

---

## 8. NuciNotifications Failover

**Question:** What if NuciNotifications API is down for extended period?

**Current:** Retry next cycle (delivery deduplication prevents duplicates)

**Risks:**
- Delivery backlog grows
- State file grows (delivery records not marked)
- No alerting on sustained failures

**Options:**
- Dead letter queue (persist failed deliveries separately)
- Fallback email provider (SMTP)
- Alert on consecutive failures
- Manual replay tool

**Decision Deferred:** Add alerting first, then evaluate.

---

## 9. Configuration Hot Reload

**Question:** Should configuration changes apply without restart?

**Current:** No — requires restart

**Options:**
- `IOptionsMonitor` + `ChangeToken` for settings
- SIGHUP handler
- Not supported (simpler)

**Decision:** Restart required. Worker is stateless enough that restart is fast.

---

## 10. Metrics and Observability

**Question:** What metrics should be exposed?

**Current:** Structured logs only (NuciLog)

**Needed:**
- Prometheus metrics endpoint
- Health check endpoint
- Cycle duration histogram
- Queue depth (unseen count)
- Delivery success rate
- State file size

**Decision Deferred:** Add when monitoring infrastructure exists.

---

## 11. Time Zone Handling

**Question:** How are email dates and cutoff times handled across time zones?

**Current:** `DateTimeOffset` throughout, IMAP dates converted to UTC

**Ambiguity:**
- `InitialProcessingDate` in config — assumed UTC?
- Email `Date` header — parsed by MimeKit to `DateTimeOffset`
- Checkpoint `LastProcessedDate` — stored as UTC

**Risk:** DST transitions, server time zone misconfiguration

**Verification Needed:** Test with emails across DST boundary.

---

## 12. Attachment Filename Collisions

**Question:** Can two different attachments have same SHA256? (Collision)

**Current:** SHA256 used as filename and deduplication key

**Probability:** Negligible (2^256)

**But:** Malicious collision attack theoretically possible

**Mitigation:** Not a practical concern. SHA256 collision resistance unbroken.

---

## 13. Partial Message Processing

**Question:** What if message has multiple bill attachments from different providers?

**Current:** `ProcessMessageAsync` processes all attachments, identifies provider per attachment

**Behavior:** Each attachment gets its own provider prefix, delivery

**Edge Case:** One email with Vodafone + Electrica attachments → two deliveries

**Acceptable:** Yes, each bill delivered separately.

---

## 14. Encrypted Emails (S/MIME, PGP)

**Question:** How to handle encrypted bill emails?

**Current:** Not supported — `MimeKit` can decrypt if certificates configured

**Requirements:**
- Private key in configuration
- Passphrase management
- Certificate rotation

**Decision Deferred:** Not in scope. Most bill emails are unencrypted.

---

## 15. Large Attachment Streaming

**Question:** Should large PDFs be streamed instead of loaded fully in memory?

**Current:** `MimePart.Content.Stream` → `MemoryStream` → byte[] → file

**Memory:** Entire attachment in memory during processing

**Limit:** `MaximumSizeBytes` (default 25MB) bounds it

**Streaming Option:** `FileStream` directly, compute hash incrementally

**Decision:** Current limit acceptable. Streaming for >100MB future.

---

## Tracking

| ID | Question | Priority | Owner | Target |
|----|----------|----------|-------|--------|
| AQ-1 | Provider configurability | Low | - | v2 |
| AQ-2 | E.ON regex fragility | Medium | - | v1.1 |
| AQ-3 | Exactly-once delivery | Low | - | v2 |
| AQ-4 | State store scalability | Medium | - | v1.5 |
| AQ-5 | Retention policy | Low | - | v1.1 |
| AQ-6 | IMAP IDLE | Low | - | v2 |
| AQ-7 | Multi-mailbox | Low | - | v2 |
| AQ-8 | NuciNotifications failover | Medium | - | v1.1 |
| AQ-9 | Hot reload | Low | - | Never |
| AQ-10 | Metrics | Medium | - | v1.5 |
| AQ-11 | Time zones | Medium | - | v1.1 |
| AQ-12 | SHA256 collision | None | - | Never |
| AQ-13 | Multi-provider email | None | - | Verified |
| AQ-14 | Encrypted emails | Low | - | v2 |
| AQ-15 | Large attachment streaming | Low | - | v2 |

---

## Related Documentation

- [Design Decisions](./design-decisions.md) — Decisions made
- [Change Guide](./change-guide.md) — Process for resolving
- [Troubleshooting](./troubleshooting.md) — Related issues
- [Architecture: Open Questions](../architecture.md#open-questions)