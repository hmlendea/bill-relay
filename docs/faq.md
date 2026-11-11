# Frequently Asked Questions

## General

### What is Bill Relay?
Bill Relay is a .NET worker service that polls an IMAP mailbox for bill emails, extracts PDF attachments, validates them, stores them locally, and forwards them via email using NuciNotifications.

### What providers are supported?
- Vodafone
- Electrica Furnizare
- E-bloc
- Hidroelectrica
- E.ON (via link download, not attachment)

Adding providers requires code changes (see [Change Guide](./change-guide.md#adding-a-new-provider)).

### Is there a web UI?
No. Bill Relay is a headless worker service. Monitoring via logs and systemd.

### Can it run in Docker?
Yes. See [Build and Deployment: Docker](./build-and-deployment.md#docker-optional).

---

## Configuration

### Where do I put secrets?
Environment variables with prefix `BILLRELAY__`:
```bash
export BILLRELAY__IMAP__PASSWORD="app-password"
export BILLRELAY__NUCINOTIFICATIONSSETTINGS__APIKEY="api-key"
export BILLRELAY__NUCINOTIFICATIONSSETTINGS__HMACSHAREDSECRETKEY="hmac-secret"
```

### How do I change the polling interval?
```json
"Polling": {
  "PollingIntervalMinutes": 10
}
```
Or env var: `BILLRELAY__POLLING__POLLINGINTERVALMINUTES=10`

### What is InitialProcessingDate?
First run processes emails newer than this date. After that, checkpoint date is used.
```json
"Filter": {
  "InitialProcessingDate": "2024-01-01T00:00:00Z"
}
```

### Can I use a different email template?
Yes. Replace `email-template.xml` in the deployment directory. Must contain `{ProviderName}` placeholder.

---

## Operation

### How do I know it's working?
```bash
# Check service status
systemctl status billrelay

# Follow logs
journalctl -u billrelay -f

# Look for successful cycle
# "PollInbox" operation with Status=Success, MessageCount > 0
```

### Why are old emails being processed?
- First run: `InitialProcessingDate` controls cutoff
- UIDVALIDITY changed: Full rescan (bounded by age filter)
- Checkpoint corrupted: Reset to beginning

### Why are emails not being processed?
1. Check IMAP connection in logs
2. Verify unseen messages exist in mailbox
3. Check age filter (`InitialProcessingDate` or checkpoint date)
4. Verify `PollingIntervalMinutes` not too high

### How does deduplication work?
- Attachment content → SHA256 hash → `AttachmentId`
- State store tracks `sha256:{hash}` and `delivery:sha256:{hash}`
- Same content = same hash = skipped on subsequent emails

### Can I reprocess an email?
Delete the `sha256:{hash}` and `delivery:sha256:{hash}` entries from state file, then restart. Or wait for retention to expire them.

---

## E.ON Specific

### Why doesn't E.ON work like other providers?
E.ON sends HTML email with a **download link**, not a PDF attachment. Bill Relay extracts the signed token from the link, calls the E.ON invoice API to download the PDF, validates it, then processes normally.

### E.ON download fails — what to check?
1. Link regex in `EOnBillDownloader.cs` matches current email format
2. Network connectivity to E.ON domain
3. PDF validation (signature, size, MIME type)
4. 30-second timeout not exceeded

### Can I test E.ON without real emails?
Save a real E.ON email as `.eml`, set `BILLRELAY_EXAMPLE_DIRECTORY`, run `ExampleEmailTests`.

---

## Storage and State

### Where are PDFs stored?
`Storage:DownloadDirectory` (default: `./bills` relative to executable)

Files named `{sha256}.pdf` — content-addressed.

### Where is state stored?
`Storage:StateFilePath` directory contains:
- `billrelay.json` — Processed attachments + deliveries
- `billrelay.json.checkpoint` — IMAP position

### How do I backup?
```bash
# Critical: state files
cp /opt/billrelay/state/billrelay.json /backup/
cp /opt/billrelay/state/billrelay.json.checkpoint /backup/

# Optional: PDFs (re-downloadable for E.ON)
cp -r /opt/billrelay/bills /backup/
```

### How do I clean up old files?
Automatic via retention policy:
```json
"Retention": {
  "MaximumAgeDays": 90,
  "MaximumCount": 10000
}
```
Runs every polling cycle.

---

## Troubleshooting

### "BaseUrl must use HTTPS" on startup
Set `NuciNotificationsSettings.BaseUrl` to `https://...`

### "ApiKey required" on startup
Set `BILLRELAY__NUCINOTIFICATIONSSETTINGS__APIKEY`

### IMAP authentication failed
- Use App Password (not account password)
- Enable IMAP in email provider settings
- Check username format (full email vs username)

### Duplicate emails received
Normal (at-least-once delivery). Deduplication prevents duplicate processing. Check frequency — if excessive, investigate NuciNotifications reliability.

### Disk full
1. Check retention config
2. Manually prune old state entries
3. Increase disk space

---

## Development

### How do I run locally?
```bash
# Debug
dotnet run --project BillRelay/BillRelay.csproj

# With custom config
dotnet run --project BillRelay/BillRelay.csproj -- --config ./my-appsettings.json
```

### How do I run tests?
```bash
# All tests
dotnet test BillRelay.slnx

# Unit only
dotnet test BillRelay.UnitTests/

# Integration (with example emails)
BILLRELAY_EXAMPLE_DIRECTORY=/path/to/emls dotnet test BillRelay.IntegrationTests/
```

### How do I add a new provider?
See [Change Guide: Adding a New Provider](./change-guide.md#adding-a-new-provider).

### Where are logs written?
- Console (systemd journal)
- Structured via NuciLog (operation + info keys)
- No separate log file by default

---

## Security

### Are credentials logged?
No. Passwords, API keys, HMAC secrets are never logged.

### Is email content logged?
Only metadata: provider name, attachment hash, file size. Not email body or attachment content.

### Is the state file encrypted?
No. File permissions (600) protect it. Encrypt disk for higher security.

### Can I use a different email provider?
Code change required. `BillSender` uses `NuciNotifications.Client`. Replace with SMTP/SendGrid/etc. implementation of `IBillSender`.

---

## Related Documentation

- [Configuration](./configuration.md) — All settings
- [Troubleshooting](./troubleshooting.md) — Detailed diagnostics
- [Change Guide](./change-guide.md) — Modification procedures
- [Security](./security.md) — Security model
- [Build and Deployment](./build-and-deployment.md) — Deployment options