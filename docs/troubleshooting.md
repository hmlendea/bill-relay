# Troubleshooting

## Quick Diagnosis

| Symptom | Likely Cause | Check |
|---------|--------------|-------|
| Service won't start | Config validation failed | `journalctl -u billrelay -n 50` |
| No emails processed | IMAP auth/connection | Logs: "Connected to IMAP" |
| Emails processed but not delivered | NuciNotifications failure | Logs: "SendAsync" errors |
| Duplicate emails | Delivery not marked processed | Check state file for `delivery:` entries |
| Old emails reprocessed | UIDVALIDITY changed | Logs: "UidValidity changed" |
| Disk full | Retention not running | Check state file size, retention config |
| PDFs not saved | Attachment validation failed | Logs: "Attachment validation failed" |

---

## Common Issues

### 1. Service Fails to Start

**Error:** `InvalidOperationException: BaseUrl must use HTTPS`

**Cause:** `NuciNotificationsSettings.BaseUrl` uses `http://`

**Fix:** Update config to `https://`

---

**Error:** `InvalidOperationException: ApiKey required`

**Cause:** `NuciNotificationsSettings.ApiKey` not set

**Fix:** Set `BILLRELAY__NUCINOTIFICATIONSSETTINGS__APIKEY` env var

---

**Error:** `DirectoryNotFoundException: DownloadDirectory`

**Cause:** `Storage:DownloadDirectory` path doesn't exist

**Fix:** Create directory or update config:
```bash
mkdir -p /var/lib/billrelay/bills
chown billrelay:billrelay /var/lib/billrelay/bills
```

---

### 2. IMAP Connection Issues

**Error:** `MailKit.Security.SslHandshakeException`

**Cause:** TLS certificate validation failed (self-signed, expired, hostname mismatch)

**Fix Options:**
- Use valid certificate on IMAP server
- For testing: `ImapSettings.UseSsl = false` (NOT for production)
- Add certificate to system trust store

---

**Error:** `AuthenticationFailedException`

**Cause:** Wrong username/password, or password auth disabled

**Fix:**
- Verify credentials
- Use App Password (Gmail, Outlook, etc.)
- Enable IMAP in email provider settings

---

**Error:** `ImapProtocolException: UIDVALIDITY`

**Cause:** Mailbox rebuilt/migrated

**Behavior:** Automatic — worker logs warning, resets checkpoint, rescans

**No Action Needed** — self-healing

---

### 3. No Emails Processed

**Checklist:**
1. **Unseen messages exist?** Check email web UI
2. **Age filter too restrictive?** `InitialProcessingDate` in config
3. **Polling interval?** Default 5 min, check `Polling:PollingIntervalMinutes`
4. **IMAP search working?** Logs show "Found X unseen messages"

**Debug:** Set log level to Debug temporarily:
```json
"Logging": {
  "LogLevel": {
    "BillRelay": "Debug"
  }
}
```

---

### 4. Attachments Not Saved

**Log:** `Attachment validation failed: Invalid MIME type`

**Cause:** Attachment not `application/pdf`

**Fix:** Check `AttachmentSettings.PermittedMimeTypes` — add if legitimate (rare)

---

**Log:** `Attachment validation failed: File size exceeds maximum`

**Cause:** PDF larger than `AttachmentSettings.MaximumSizeBytes` (default 25MB)

**Fix:** Increase limit or ask sender to compress

---

**Log:** `Attachment validation failed: Invalid PDF signature`

**Cause:** File doesn't start with `%PDF-`

**Fix:** Corrupted download or not a PDF — check sender

---

### 5. E.ON Bills Not Downloaded

**Log:** `E.ON download link not found in email body` or `E.ON download link does not contain a token`

**Cause:** HTML body format changed, regex no longer matches, or the link lost its `token` parameter

**Fix:** Update the patterns in `EOnBillDownloader.cs`:
```csharp
// Current pattern (may need adjustment)
private static readonly Regex EOnDownloadLinkPattern = new(
    @"https://www\.eon\.ro/descarc(?:are|a)-factura\?[^""'\s<>]+",
    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
```

**Test:** Save `.eml`, run `ExampleEmailTests` with `BILLRELAY_EXAMPLE_DIRECTORY`

---

### 6. Duplicate Emails Delivered

**Cause:** Crash between `BillSender.SendAsync` and `MarkProcessed("delivery:...")`

**Behavior:** At-least-once delivery — next cycle detects duplicate via state store

**Verify:** Check state file for `delivery:sha256:...` entries

**No Fix Needed** — deduplication works. Monitor frequency.

---

### 7. State File Corruption

**Error:** `JsonException` on startup

**Cause:** Partial write, disk full, kill -9 during write

**Recovery:**
```bash
# 1. Stop service
systemctl stop billrelay

# 2. Restore from backup
cp /backup/billrelay.json /opt/billrelay/state/
cp /backup/billrelay.json.checkpoint /opt/billrelay/state/

# 3. Start service
systemctl start billrelay
```

**Prevention:** Monitor disk space, use atomic writes (already implemented)

---

### 8. Retention Not Working

**Symptom:** State file grows unbounded, old files not deleted

**Check:**
1. `RetentionSettings.MaximumAgeDays` > 0
2. `RetentionSettings.MaximumCount` > 0
3. Logs show "Applying retention" each cycle

**Debug:** Add temporary log in `FileStateStore.ApplyRetention`

---

### 9. High Memory Usage

**Cause:** Large state file loaded entirely into memory

**Threshold:** > 50 MB state file (~100k entries)

**Mitigation:**
- Reduce `Retention.MaximumCount`
- Archive old state (manual)
- Future: Migrate to SQLite

---

### 10. Slow Polling Cycles

**Symptom:** Cycle duration > polling interval

**Causes:**
- Many unseen messages
- Slow IMAP server
- Large attachments (download time)
- Slow NuciNotifications API

**Mitigation:**
- Increase `PollingIntervalMinutes`
- Optimize IMAP server
- Monitor per-phase timing in logs

---

## Log Analysis

### Key Log Operations

| Operation | Phase | Key Info Keys |
|-----------|-------|---------------|
| `PollInbox` | Cycle start/end | `MessageCount`, `DurationMs`, `UnseenCount` |
| `ConnectImap` | IMAP connect | `Server`, `DurationMs` |
| `ProcessMessage` | Per message | `MessageUid`, `ProviderName`, `AttachmentCount` |
| `DownloadAttachment` | E.ON download | `Url`, `DurationMs`, `SizeBytes` |
| `SaveAttachment` | File write | `FilePath`, `AttachmentId`, `DurationMs` |
| `SendEmail` | NuciNotifications | `Recipient`, `AttachmentId`, `DurationMs` |
| `SaveCheckpoint` | State persist | `LastProcessedUid`, `DurationMs` |
| `ApplyRetention` | Cleanup | `RemovedCount`, `FreedBytes` |

### Query Examples (if using structured log storage)

```sql
-- Slow cycles
SELECT * FROM logs WHERE Operation = 'PollInbox' AND DurationMs > 300000;

-- Failed deliveries
SELECT * FROM logs WHERE Operation = 'SendEmail' AND Status = 'Failure';

-- E.ON download failures
SELECT * FROM logs WHERE Operation = 'DownloadAttachment' AND Status = 'Failure';
```

---

## Debugging Techniques

### 1. Dry Run with Test Email

```bash
# Save .eml to test directory
export BILLRELAY_EXAMPLE_DIRECTORY="/tmp/test-emails"
dotnet test BillRelay.IntegrationTests --filter "ExampleEmailTests"
```

### 2. Manual IMAP Test

```bash
# Using openssl
openssl s_client -connect imap.example.com:993 -crlf
# Then: A001 LOGIN user pass
# A002 SELECT INBOX
# A003 SEARCH UNSEEN
```

### 3. Manual NuciNotifications Test

```bash
curl -X POST https://api.notifications.example.com/api/notifications/email \
  -H "X-Api-Key: your-key" \
  -H "Content-Type: application/json" \
  -d '{"to":"test@example.com","subject":"Test","body":"Test","attachments":[]}'
```

### 4. Inspect State File

```bash
cat /opt/billrelay/state/billrelay.json | jq '.ProcessedAttachments | length'
cat /opt/billrelay/state/billrelay.json | jq '.Checkpoint'
```

---

## Escalation

| Issue | Escalation Path |
|-------|-----------------|
| IMAP server down | Email admin / hosting provider |
| NuciNotifications API down | Platform team / API owner |
| Disk full | Sysadmin / increase volume |
| Certificate expiry | Cert management process |
| Unknown crash | Check logs, create issue with logs |

---

## Related Documentation

- [Error Handling](./error-handling.md) — Error patterns
- [Logging](./logging.md) — Log structure
- [State and Persistence](./state-and-persistence.md) — State file details
- [Integrations](./integrations.md) — Integration details
- [Build and Deployment](./build-and-deployment.md) — Deployment issues