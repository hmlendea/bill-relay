# Repository Overview

## Purpose

Bill Relay is a .NET 10 worker service that monitors an IMAP inbox for supported utility bills, validates their PDF attachments, saves them locally, and forwards them through the NuciNotifications API for email delivery.

## Scope

**In scope:**
- IMAP mailbox polling and message retrieval
- Provider identification (Vodafone, Electrica Furnizare, E-bloc, Hidroelectrica, E.ON)
- PDF attachment validation (MIME type, size, PDF signature)
- Local file storage with atomic writes and deduplication
- Email delivery via NuciNotifications HTTPS API
- Checkpoint and processed-attachment state persistence
- Retention policy for state records

**Out of scope:**
- IMAP IDLE/push notifications (polling only)
- SMTP delivery (owned by NuciNotifications)
- Web UI or API (worker-only)
- Database persistence (file-based only)
- Multi-mailbox support (single configured mailbox)

## Entry Points

| Entry Point | Location | Purpose |
|-------------|----------|---------|
| `Main` | `BillRelay/Program.cs` | Composition root, DI registration, host startup |
| `ExecuteAsync` | `BillRelay/Services/BillRelayWorker.cs` | BackgroundService entry, polling loop |
| `ProcessInboxAsync` | `BillRelay/Services/BillRelayWorker.cs` | Per-cycle mailbox processing |

## Runtime Topology

```
Bill Relay Process
  Program (Composition)
    BillRelayWorker (Orchestration)
      Service Pipeline (Validation, Delivery, State)
         IMAP Server
         Filesystem (PDF/JSON)
         NuciNotifications API
```

## Principal Capabilities

1. **IMAP Polling** — Configurable interval, searches messages newer than cutoff
2. **Provider Classification** — Subject keywords + sender/link domain matching
3. **Attachment Validation** — Provider-specific filename regex, MIME type, size limit, PDF signature
4. **E.ON Special Handling** — Downloads PDF from link in email body (not attachment)
5. **Atomic File Storage** — Temp file hash compare move, with deduplication
6. **Delivery Deduplication** — SHA256-based attachment ID, persisted processed ledger
7. **Checkpoint Persistence** — IMAP UID validity + last processed UID/date
8. **Retention Cleanup** — Age-based and count-based state pruning
9. **Structured Logging** — NuciLog with operation/status/info key taxonomy

## Project Structure

```
BillRelay.slnx
BillRelay/                    # Main worker project
  Program.cs                  # Composition root
  appsettings.json            # Configuration template
  Configuration/              # Settings classes
  Services/                   # Core services
    Attachments/              # Attachment-specific services
  Logging/                    # NuciLog operation/info keys
  DataAccess/DataObjects/     # XML-serialised data objects
BillRelay.UnitTests/          # Unit tests (NUnit + Moq)
BillRelay.IntegrationTests/   # Integration tests (file system, real components)
BillRelay.ArchitectureTests/  # ArchUnit.NET architecture tests
email-template.xml            # Email template (copied to output)
release.sh                    # Release script
.github/workflows/dotnet.yml  # CI pipeline
```

## Key Technologies

- **.NET 10** — Target framework
- **MailKit/MimeKit** — IMAP and MIME parsing
- **NuciNotifications.Client** — Notification API client
- **NuciLog** — Structured logging
- **NuciDAL** — JSON repository for state persistence
- **NUnit 4.x + Moq** — Unit testing
- **ArchUnit.NET** — Architecture fitness tests
- **GitHub Actions** — CI/CD

## Operational Model

- **Deployment** — Self-hosted, single process, file-based state
- **Configuration** — `appsettings.json` + environment variables
- **Credentials** — IMAP password, NuciNotifications API key/HMAC in config
- **Logs** — File + console via NuciLog
- **State** — `./state/billrelay.json` + `./state/billrelay.json.checkpoint`
- **Downloads** — `./bills/` directory