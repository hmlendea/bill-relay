# API Reference Index

## Overview

Bill Relay is a **worker service** with no HTTP API, no controllers, and no public API endpoints. This section documents the internal service interfaces for developers extending the codebase.

## Internal Service Interfaces

| Interface | Implementation | Documentation |
|-----------|----------------|---------------|
| `IStateStore` | `FileStateStore` | [FileStateStore](../components/file-state-store.md) |
| `IAttachmentProcessor` | `AttachmentProcessor` | [AttachmentProcessor](../components/attachment-processor.md) |
| `IAttachmentValidator` | `AttachmentValidator` | [AttachmentValidator](../components/attachment-validator.md) |
| `IEOnBillDownloader` | `EOnBillDownloader` | [EOnBillDownloader](../components/eon-bill-downloader.md) |
| `IBillDelivery` | `BillDelivery` | [BillDelivery](../components/bill-delivery.md) |
| `IBillSender` | `BillSender` | [BillSender](../components/bill-sender.md) |

## Static Utilities

| Class | Documentation |
|-------|---------------|
| `BillProviderIdentification` | [BillProviderIdentification](../components/bill-provider-identification.md) |
| `EmailAgePolicy` | [EmailAgePolicy](../components/email-age-policy.md) |

## Data Models

| Model | Documentation |
|-------|---------------|
| `ProcessedAttachmentDataObject` | [Integration Models](../components/integration-models.md#file-system-models) |
| `ImapCheckpoint` | [Integration Models](../components/integration-models.md#file-system-models) |
| `BillProvider` enum | [Integration Models](../components/integration-models.md#provider-identification-models) |
| Configuration classes | [Configuration](../configuration.md) |

## External Integrations

| Integration | Client Library | Documentation |
|-------------|----------------|---------------|
| IMAP | MailKit / MimeKit | [Integrations: IMAP](../integrations.md#1-imap-server-integration) |
| NuciNotifications | NuciNotifications.Client | [Integrations: NuciNotifications](../integrations.md#2-nucinotifications-integration) |
| File System | System.IO / NuciDAL | [Integrations: File System](../integrations.md#3-file-system-integration) |

## No Public API

Bill Relay does not expose:
- REST endpoints
- GraphQL
- gRPC
- WebSocket
- Message queue consumers
- Scheduled job triggers (internal timer only)

## Extending with an API

If a public API is needed in the future:

1. Add ASP.NET Core to `BillRelay.csproj`
2. Add controllers in `BillRelay/Controllers/`
3. Register `AddControllers()` in `Program.cs`
4. Map endpoints: `app.MapControllers()`
5. Document in this directory (one file per controller)

## Related Documentation

- [Components](../components/) — Service implementations
- [Integration Models](../components/integration-models.md) — DTOs and models
- [Architecture: No Public API](../architecture.md#no-public-api)