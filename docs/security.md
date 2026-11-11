# Security

## Security Model

Bill Relay is a self-hosted worker service with the following trust boundaries:

```
┌─────────────────────────────────────────────────────────────┐
│                    Bill Relay Process                        │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐  │
│  │ IMAP Client │  │  Processor  │  │ Notification Client │  │
│  │ (MailKit)   │──▶│  Pipeline   │──▶│ (NuciNotifications) │  │
│  └──────┬──────┘  └──────┬──────┘  └──────────┬──────────┘  │
│         │                │                    │              │
│         ▼                ▼                    ▼              │
│  ┌─────────────────────────────────────────────────────┐    │
│  │              Local Filesystem                        │    │
│  │  ./bills/ (PDFs)  ./state/ (JSON + checkpoint)      │    │
│  └─────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────┘
         │                        │                    │
         ▼                        ▼                    ▼
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   IMAP Server   │    │  NuciNotifications│   │   Operator      │
│  (Credentials)  │    │     API          │    │  (Config/Secrets)│
└─────────────────┘    └─────────────────┘    └─────────────────┘
```

## Threat Model

### Assets to Protect

| Asset | Sensitivity | Threats |
|-------|-------------|---------|
| IMAP credentials | **High** | Config leakage, memory dump, log exposure |
| NuciNotifications API key + HMAC secret | **High** | Config leakage, memory dump, log exposure |
| Email content (subject, sender, body) | **Medium** | Log exposure, file system access |
| PDF attachments (bills) | **Medium** | File system access, unauthorized read |
| Recipient email address | **Medium** | Config leakage, log exposure |
| Processing state (checkpoint, delivery records) | **Low** | Integrity, availability |

### Attack Vectors

| Vector | Mitigation |
|--------|------------|
| Committed secrets in config | Environment variable overrides, `.gitignore` |
| Log exposure of PII/secrets | Structured logging, avoid logging sensitive fields |
| IMAP credential theft | TLS encryption, certificate validation |
| API credential theft | HTTPS enforcement, HMAC signing |
| File system access | OS permissions, dedicated service account |
| Malicious email content | MIME type validation, size limits, PDF signature check |
| State tampering | File permissions, atomic writes |
| Denial of service | Size limits, timeout, sequential processing |

## Authentication & Authorization

### IMAP Authentication

- **Method:** Username/password (no OAuth support)
- **Transport:** TLS (IMAPS, port 993 default)
- **Certificate Validation:** Enabled by default (`ImapSettings.IsCertificateValidationEnabled = true`)
- **Risk:** Password in memory and config; no token rotation

### NuciNotifications Authentication

- **Method:** API Key + HMAC-SHA256 request signing
- **Transport:** HTTPS (enforced at startup validation)
- **Client:** `NuciNotifications.Client` handles signing
- **Secrets:** `ApiKey` and `HmacSharedSecretKey` in config

### Authorization Model

**No authorization within Bill Relay** — single-tenant, operator-controlled.
- No multi-user support
- No role-based access
- No API endpoints (worker only)

## Data Protection

### In Transit

| Channel | Protection |
|---------|------------|
| IMAP | TLS 1.2+ (MailKit default) |
| NuciNotifications API | HTTPS (enforced) |
| SMTP (downstream) | Owned by NuciNotifications |

### At Rest

| Data | Location | Protection |
|------|----------|------------|
| PDF attachments | `Storage.DownloadDirectory` | OS file permissions |
| State JSON | `Storage.StateFilePath` | OS file permissions |
| Checkpoint JSON | `Storage.StateFilePath.checkpoint` | OS file permissions |
| Configuration | `appsettings.json` (output dir) | **Not encrypted** — use env vars for secrets |
| Logs | `NuciLoggerSettings.LogFilePath` | OS file permissions |

### In Memory

- Credentials held in `ImapSettings` and `NuciNotificationsSettings` objects
- PDF content decoded to `MemoryStream` for validation and Base64 encoding
- No explicit memory protection (no `SecureString`, no zeroing)

## Configuration Security

### Secrets Management

**Required Secrets (never commit):**
- `ImapSettings.Password`
- `NuciNotificationsSettings.ApiKey`
- `NuciNotificationsSettings.HmacSharedSecretKey`
- `Delivery.Recipient` (PII)

**Recommended Practice:**
```bash
# Environment variables (deployment)
BILLRELAY__IMAP__PASSWORD="app-specific-password"
BILLRELAY__NUCINOTIFICATIONSSETTINGS__APIKEY="api-key"
BILLRELAY__NUCINOTIFICATIONSSETTINGS__HMACSHAREDSECRETKEY="hmac-secret"
BILLRELAY__DELIVERY__RECIPIENT="user@example.com"
```

### Configuration Validation

Startup validation in `Program.ValidateNotifications()`:
- `BaseUrl` must be absolute HTTPS
- `ApiKey` non-empty
- `HmacSharedSecretKey` non-empty

**Missing Validation:**
- IMAP credentials not validated at startup (fail on first connect)
- Recipient email format validated only at send time
- Template file existence validated only at send time

### Certificate Validation

```csharp
// Program.cs / BillRelayWorker
if (!_imapSettings.IsCertificateValidationEnabled)
    client.ServerCertificateValidationCallback = (_, _, _, _) => true;
```

**Default:** `true` (validate)
**Risk:** Setting `false` disables TLS certificate validation — MITM vulnerable.

## Input Validation

### Email Processing

| Input | Validation |
|-------|------------|
| Subject | Normalized (NFD + remove diacritics), keyword match |
| Sender address | Domain suffix match against known providers |
| Body links | HTTPS URL regex, domain extraction |
| Attachments | MIME type allowlist, size limit, PDF signature (`%PDF-`) |
| E.ON invoice link | Regex match `https://www.eon.ro/descarc(are)?-factura?...`; only the `token` query parameter is used |

### File Operations

| Operation | Validation |
|-----------|------------|
| Filename sanitization | Replace invalid chars with `_`, trim trailing `.`/` ` |
| Path traversal | `Path.Combine` with sanitized name, no user-controlled paths |
| Atomic write | Temp file → hash verify → move |
| Hash verification | SHA256 of written content matches source |

### API Requests

- NuciNotifications client handles request signing
- Attachment Base64 encoded from validated PDF bytes
- No user input in API request beyond validated data

## Logging Security

### Current State

**No sensitive field masking** — all `LogInfo` values logged as-is.

**Fields containing sensitive data:**
- `Subject` — May contain account numbers, names
- `Sender` — Email address (PII)
- `Recipient` — Email address (PII)
- `MessageId` — May correlate to user

### Recommendations

1. Mark PII fields as sensitive in NuciLog (if supported)
2. Redact `Subject` at Info level (log hash instead)
3. Avoid logging `Recipient` at Info level
4. Ensure log file permissions restrict access

## File System Security

### Permissions

**Recommended deployment:**
- Dedicated service user (e.g., `billrelay`)
- `DownloadDirectory`: `700` (owner read/write/execute)
- `StateFilePath` directory: `700`
- Log file: `600`
- Config file: `600` (if secrets in file)

### Atomic Writes

All state writes use temp-file + move pattern:
- Prevents partial reads
- Prevents corruption on crash
- No file locking (single-writer assumption)

## Network Security

### Outbound Connections

| Destination | Protocol | Port | Validation |
|-------------|----------|------|------------|
| IMAP Server | IMAPS | 993 (config) | TLS cert validation (default) |
| NuciNotifications | HTTPS | 443 | HTTPS enforced, cert validation |
| E.ON Download | HTTPS | 443 | URL regex restricted to `www.eon.ro` |

### Inbound Connections

**None** — Bill Relay has no listening ports.

## Dependency Security

### NuGet Packages (Runtime)

| Package | Version | Purpose | Risk |
|---------|---------|---------|------|
| MailKit | 4.12.0 | IMAP/MIME | Mature, widely used |
| MimeKit | 4.12.0 | MIME parsing | Mature, widely used |
| NuciDAL | 3.2.1 | JSON repository | Internal (hmlendea) |
| NuciLog | 1.2.1 | Logging | Internal (hmlendea) |
| NuciNotifications.Client | 1.2.0 | API client | Internal (hmlendea) |

**Supply Chain:** Internal packages from hmlendea; verify provenance in deployment.

### Development Dependencies

| Package | Purpose |
|---------|---------|
| NUnit | Testing |
| Moq | Mocking |
| ArchUnit.NET | Architecture tests |

## Operational Security

### Deployment Checklist

- [ ] Run as dedicated non-root user
- [ ] Secrets via environment variables (not config file)
- [ ] `ImapSettings.IsCertificateValidationEnabled = true`
- [ ] `NuciNotificationsSettings.BaseUrl` is HTTPS
- [ ] File permissions: `700` on data directories, `600` on files
- [ ] Log file permissions: `600`
- [ ] Firewall: Outbound only to IMAP server and NuciNotifications API
- [ ] Monitor logs for authentication failures
- [ ] Rotate IMAP password and API keys periodically

### Incident Response

| Incident | Response |
|----------|----------|
| IMAP credential compromise | Rotate password, review logs for unauthorized access |
| API key compromise | Rotate API key + HMAC secret in NuciNotifications, update env vars |
| Log exposure | Rotate secrets, restrict log access, audit log content |
| State file tampering | Restore from backup, verify delivery records |
| PDF file exposure | Review file permissions, consider encryption at rest |

## Compliance Considerations

### GDPR / Personal Data

**Personal data processed:**
- Email addresses (sender, recipient)
- Names in email subject/body
- Bill content (account numbers, usage data)

**Lawful basis:** Legitimate interest (operator's bill management)

**Data subject rights:**
- Access: Operator can view stored PDFs and state
- Rectification: Not applicable (source is email)
- Erasure: Delete PDFs and state files
- Portability: PDFs and JSON are portable formats

**Retention:** Controlled by `RetentionSettings` (max age, max files) — **not automatically enforced**.

### Security Standards

- No encryption at rest (file system only)
- No encryption in memory
- TLS for all external connections
- No authentication for local access (relies on OS)

## Known Security Gaps

1. **No automatic secret rotation** — Manual env var update + restart
2. **No encryption at rest** — PDFs and state readable by OS user
3. **No PII masking in logs** — Subject, sender, recipient logged
4. **No rate limiting** — Could hammer IMAP or API on misconfiguration
5. **No integrity verification** — State files not signed/verified
6. **Single-threaded** — No parallel processing, but also no resource exhaustion from concurrency
7. **No health/readiness endpoints** — Cannot integrate with orchestration health checks

## CI/CD Security Scanning

This project uses GitHub Actions for automated security scanning:

### Security Scan Workflow (`.github/workflows/security.yml`)

**Triggers:**
- Push to `master` branch
- Pull requests targeting `master` branch
- Weekly schedule (Sunday 00:00 UTC)

**Scans Performed:**

| Scan | Tool | Purpose |
|------|------|---------|
| CodeQL Static Analysis | `github/codeql-action` | Detects code vulnerabilities in C# |
| Vulnerable Package Detection | `dotnet list package --vulnerable --include-transitive` | Identifies vulnerable NuGet dependencies (including transitive) |

**Permissions:** `security-events: write` (required for CodeQL SARIF upload), `contents: read`

**Results:** Published to GitHub Code Scanning — viewable in the repository's **Security** tab.

### Build & Test Workflow (`.github/workflows/dotnet.yml`)

**Triggers:** Push/PR to `master`

**Security-Relevant Steps:**
- `dotnet restore` — Restores dependencies with lock file integrity
- `dotnet build` — Compiles with warnings as errors
- `dotnet test` — Runs unit, integration, and architecture tests

## Related Documentation

- [Root SECURITY.md](../SECURITY.md) — Vulnerability reporting, supported versions
- [Configuration](./configuration.md) — Settings and environment variables
- [Logging](./logging.md) — Log fields and sensitivity
- [Error Handling](./error-handling.md) — Failure logging
- [Architecture: Security and Privacy](../architecture.md#security-and-privacy)