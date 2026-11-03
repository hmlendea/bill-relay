# Integrations

## Overview

Bill Relay integrates with three external systems:

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│   IMAP Server   │     │ NuciNotifications│     │  File System    │
│  (Email Source) │     │  (Email Delivery)│     │  (Persistence)  │
└────────┬────────┘     └────────┬────────┘     └────────┬────────┘
         │                       │                       │
         ▼                       ▼                       ▼
┌─────────────────────────────────────────────────────────────────┐
│                        Bill Relay Worker                         │
└─────────────────────────────────────────────────────────────────┘
```

---

## 1. IMAP Server Integration

### Protocol

- **Protocol:** IMAP over TLS (IMAPS, port 993)
- **Library:** MailKit 4.12.0
- **Authentication:** Username/password (APP password recommended)

### Configuration

```json
"Imap": {
  "Server": "imap.example.com",
  "Port": 993,
  "Username": "bills@example.com",
  "Password": "app-password",
  "UseSsl": true,
  "ConnectionTimeoutSeconds": 30
}
```

### Operations

| Operation | MailKit Method | Timeout |
|-----------|----------------|---------|
| Connect | `ImapClient.ConnectAsync` | `ConnectionTimeoutSeconds` |
| Authenticate | `ImapClient.AuthenticateAsync` | Included in connect |
| Select Inbox | `ImapClient.Inbox.OpenAsync` | Default |
| Search Unseen | `Folder.SearchAsync(SearchQuery.NotSeen)` | Default |
| Fetch Message | `Folder.GetMessageAsync(uid)` | Default |
| Disconnect | `ImapClient.DisconnectAsync(true)` | Default |

### Search Criteria

```csharp
// BillRelayWorker.ProcessMailboxAsync
var query = SearchQuery.NotSeen
    .And(SearchQuery.DeliveredAfter(ageCutoff));  // EmailAgePolicy cutoff
```

### UID Handling

- **UIDVALIDITY:** Tracked in checkpoint; reset triggers full rescan
- **UID Range:** Process `LastProcessedUid + 1` to `maxUid`
- **Message Fetch:** Full message (headers + body + attachments)

### Error Handling

| Error | Response |
|-------|----------|
| Connection failed | Log, retry next cycle |
| Authentication failed | Log, retry next cycle (alert) |
| UIDVALIDITY changed | Reset checkpoint, full rescan |
| Message fetch failed | Log, skip message, continue |
| Folder open failed | Log, retry next cycle |

### Security

- **TLS Required:** `UseSsl = true` enforced
- **Certificate Validation:** Default (system trust store)
- **Credentials:** Environment variable `BILLRELAY__IMAP__PASSWORD`
- **No STARTTLS:** Direct IMAPS only

---

## 2. NuciNotifications Integration

### Purpose

Send bill emails with PDF attachments via internal notification service.

### Protocol

- **Transport:** HTTPS (REST API)
- **Authentication:** HMAC-SHA256 signed requests
- **Library:** `NuciNotifications.Client` 1.2.0

### Configuration

```json
"NuciNotificationsSettings": {
  "BaseUrl": "https://api.notifications.example.com",
  "ApiKey": "api-key",
  "HmacSharedSecretKey": "hmac-secret"
}
```

### Validation (Program.cs)

```csharp
// Required at startup
if (!settings.BaseUrl.StartsWith("https://"))
    throw new InvalidOperationException("BaseUrl must use HTTPS");
if (string.IsNullOrEmpty(settings.ApiKey))
    throw new InvalidOperationException("ApiKey required");
if (string.IsNullOrEmpty(settings.HmacSharedSecretKey))
    throw new InvalidOperationException("HmacSharedSecretKey required");
```

### Request Flow

```
BillSender.SendAsync
    │
    ├─ Load email-template.xml
    ├─ Replace {ProviderName}
    ├─ Read PDF → Base64
    ├─ Build NotificationRequest
    │   ├─ To: recipient
    │   ├─ Subject: "Bill from {ProviderName}"
    │   ├─ Body: template
    │   └─ Attachments: [{FileName, ContentBase64, MimeType}]
    │
    ├─ NuciNotificationsClient.SendAsync(request)
    │   ├─ Serialize JSON
    │   ├─ Compute HMAC-SHA256(body, secret)
    │   ├─ Headers: X-Api-Key, X-Signature, X-Timestamp
    │   └─ POST /api/notifications/email
    │
    └─ Response: Success/Failure
```

### Email Template

**File:** `email-template.xml` (root, copied to output)

```xml
<EmailTemplate>
  <Subject>Bill from {ProviderName}</Subject>
  <Body>
    <![CDATA[
    <html>
      <body>
        <p>Please find attached your bill from <strong>{ProviderName}</strong>.</p>
        <p>This email was sent automatically by Bill Relay.</p>
      </body>
    </html>
    ]]>
  </Body>
</EmailTemplate>
```

### Attachment Encoding

```csharp
// BillSender.SendAsync
var pdfBytes = await File.ReadAllBytesAsync(filePath);
var base64 = Convert.ToBase64String(pdfBytes);
request.Attachments.Add(new AttachmentDto
{
    FileName = displayName,
    ContentBase64 = base64,
    MimeType = "application/pdf"
});
```

### Error Handling

| Error | Response |
|-------|----------|
| HTTP 401/403 | Invalid credentials → Log, don't mark delivery processed |
| HTTP 429 | Rate limited → Log, don't mark delivery processed |
| HTTP 5xx | Server error → Log, don't mark delivery processed |
| Network timeout | Log, don't mark delivery processed |
| HMAC mismatch | Should not happen (client computes) |

**Retry:** Next polling cycle (delivery deduplication prevents duplicates)

### Security

- **HTTPS Enforced:** Startup validation
- **HMAC Signing:** Request body + timestamp signed with shared secret
- **API Key:** Header authentication
- **No Credentials in Email:** Only API key + HMAC

---

## 3. File System Integration

### Purpose

Persistent storage for:
- Bill PDF attachments (content-addressed)
- State (processed attachments, deliveries, IMAP checkpoint)

### Directories

| Directory | Config Key | Purpose |
|-----------|------------|---------|
| `DownloadDirectory` | `Storage:DownloadDirectory` | Bill PDFs |
| `StateFilePath` directory | `Storage:StateFilePath` | JSON state + checkpoint |

### Permissions

```
DownloadDirectory:  700 (drwx------)  Owner: billrelay user
StateFilePath dir:  700 (drwx------)  Owner: billrelay user
State files:        600 (-rw-------)  Owner: billrelay user
PDF files:          600 (-rw-------)  Owner: billrelay user
```

### Operations

| Operation | Method | Atomicity |
|-----------|--------|-----------|
| Write PDF | `AttachmentProcessor` → temp → verify → move | Atomic (rename) |
| Read PDF | `BillSender` → `File.ReadAllBytesAsync` | N/A |
| Write State | `JsonRepository.SaveAll` → temp → rename | Atomic |
| Read State | `JsonRepository.LoadAll` | N/A |
| Write Checkpoint | `JsonRepository.Save` → temp → rename | Atomic |
| Read Checkpoint | `JsonRepository.Load` | N/A |
| Delete Orphans | `File.Delete` (retention) | Immediate |

### Disk Space

**Monitoring:**
```bash
# Alert if > 80% full
df -h /opt/billrelay/bills
df -h /opt/billrelay/state
```

**Cleanup:** Retention policy (age + count) runs every cycle.

---

## 4. HTTP Client (E.ON Download)

### Purpose

Download bill PDFs from E.ON links in HTML emails.

### Configuration

```csharp
// Program.cs
services.AddHttpClient<IEOnBillDownloader, EOnBillDownloader>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
```

### Flow

```
EOnBillDownloader.DownloadBillAsync
    │
    ├─ Extract URL from HTML body (regex)
    ├─ HTTP GET with timeout (30s)
    ├─ Validate response
    │   ├─ Status 200
    │   ├─ Content-Type: application/pdf
    │   ├─ PDF signature (%PDF-)
    │   └─ Size ≤ MaximumSizeBytes
    │
    └─ Wrap as MimePart for AttachmentProcessor
```

### Error Handling

| Error | Response |
|-------|----------|
| No URL found | Return null (not E.ON bill) |
| HTTP error (4xx/5xx) | Log, return null |
| Timeout | Log, return null |
| Not PDF | Log, return null |
| Too large | Log, return null |

**Result:** Null → `BillProviderIdentification` treats as no attachment → message skipped.

---

## Integration Health Checks

### No Built-in Health Endpoints

**Monitor via:**

| Integration | Check |
|-------------|-------|
| IMAP | `systemctl status billrelay` + logs show "Connected to IMAP" |
| NuciNotifications | Logs show "Email sent successfully" |
| File System | `df -h` + write test |
| E.ON HTTP | Logs show "Downloaded bill from E.ON" |

### Recommended: Synthetic Monitoring

```bash
# Cron job every 10 min
#!/bin/bash
if ! systemctl is-active --quiet billrelay; then
    alert "BillRelay service down"
fi

# Check recent log for successful cycle
if ! journalctl -u billrelay --since "15 minutes ago" | grep -q "PollInbox.*Success"; then
    alert "BillRelay no successful cycle in 15 min"
fi
```

---

## Integration Version Compatibility

| Integration | Version Tested | Compatibility |
|-------------|----------------|---------------|
| IMAP (Dovecot) | 2.3+ | Standard IMAP |
| IMAP (Exchange) | 2016+ | Standard IMAP |
| IMAP (Gmail) | Current | Requires App Password |
| NuciNotifications API | 1.x | Client 1.2.0 |
| File System | ext4, xfs, NTFS | POSIX + Windows |

---

## Adding New Integrations

### Checklist

- [ ] Define interface in `BillRelay/Services/`
- [ ] Implement with configuration section
- [ ] Register in `Program.cs` DI
- [ ] Add validation in `Program.cs`
- [ ] Document in this file
- [ ] Add architecture test if new namespace
- [ ] Update `dependencies.md`

### Pattern

```csharp
// 1. Interface
public interface INewIntegration
{
    Task<Result> DoSomethingAsync(CancellationToken ct);
}

// 2. Settings
public class NewIntegrationSettings
{
    public string Endpoint { get; set; }
    public string ApiKey { get; set; }
}

// 3. Implementation
public class NewIntegration : INewIntegration
{
    private readonly NewIntegrationSettings _settings;
    private readonly HttpClient _http;

    public NewIntegration(IOptions<NewIntegrationSettings> settings, HttpClient http) { ... }

    public async Task<Result> DoSomethingAsync(CancellationToken ct) { ... }
}

// 4. Registration
services.AddHttpClient<INewIntegration, NewIntegration>();
services.Configure<NewIntegrationSettings>(config.GetSection("NewIntegration"));
```

---

## Related Documentation

- [Configuration](./configuration.md) — Integration settings
- [Security](./security.md) — Integration security
- [Components: BillRelayWorker](../components/bill-relay-worker.md) — IMAP integration
- [Components: BillSender](../components/bill-sender.md) — NuciNotifications integration
- [Components: EOnBillDownloader](../components/eon-bill-downloader.md) — HTTP integration
- [Components: FileStateStore](../components/file-state-store.md) — File system integration
- [Architecture: External Integrations](../architecture.md#external-integrations)