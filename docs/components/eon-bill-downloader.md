# EOnBillDownloader Component

## Overview

**Location:** `BillRelay/Services/Attachments/EOnBillDownloader.cs`

**Interface:** `IEOnBillDownloader`

**Lifetime:** Singleton

**Purpose:** Downloads E.ON bill PDFs from links embedded in email body (E.ON sends download links, not attachments).

## Responsibilities

1. **Link Extraction** — Find E.ON download URLs in HTML email body
2. **HTTP Download** — Fetch PDF from extracted URL with timeout
3. **Content Validation** — Verify response is `application/pdf` and non-empty
4. **MIME Part Creation** — Wrap downloaded bytes as `MimePart` for downstream processing

## Key Methods

### DownloadBillAsync

```csharp
public async Task<MimePart?> DownloadBillAsync(
    MimeMessage message,
    string messageId,
    CancellationToken cancellationToken)
```

**Algorithm:**
1. Extract HTML body from message
2. Match `EOnDownloadLinkPattern` regex: `https://www\.eon\.ro/descarcare-factura\?[^""'\s<>]+`
3. If no match → log debug, return null
4. Create `HttpClient` via `IHttpClientFactory`
5. Set timeout from `AttachmentSettings.DownloadTimeoutSeconds` (default 30s)
6. GET download URL
7. Validate response:
   - Success status code
   - Content-Type = `application/pdf`
   - Non-empty content
8. Create `MimePart` with:
   - MIME type: `application/pdf`
   - Filename: `E.ON_Factura_{messageId}.pdf` (colons replaced)
   - Content: Downloaded bytes as `MemoryStream`
   - Content-Disposition: Attachment
   - Content-Transfer-Encoding: Base64
9. Return `MimePart` for downstream validation/processing

## Regex Patterns

| Pattern | Purpose |
|---------|---------|
| `EOnDownloadLinkPattern` | Primary download link: `https://www.eon.ro/descarcare-factura?...` |
| `EOnClickTrackingPattern` | Click tracking links (currently unused): `http://noutati.myline-eon.ro/click/...` |

## Configuration

Controlled via `AttachmentSettings`:
- `DownloadTimeoutSeconds` — HTTP request timeout (default 30s)

## Error Handling

| Failure | Behavior |
|---------|----------|
| No download link found | Log debug, return null |
| HTTP error status | Log error, return null |
| Non-PDF content type | Log error, return null |
| Empty PDF content | Log error, return null |
| Network/timeout exception | Log error, return null |
| Cancellation | Rethrow `OperationCanceledException` |

**All failures return null** — Caller (`BillRelayWorker`) treats as "no valid attachment" and skips message.

## Integration Flow

```
BillRelayWorker.ProcessMessageAsync
    │
    ├─► IdentifyProvider → "E.ON"
    │
    ├─► GetValidAttachmentAsync
    │       │
    │       └─► EOnBillDownloader.DownloadBillAsync(message, messageId)
    │               │
    │               ├─► Extract link from HTML body
    │               ├─► HTTP GET with timeout
    │               ├─► Validate PDF response
    │               └─► Return MimePart
    │
    ├─► AttachmentValidator.GetFailureReason (MIME type, size, PDF signature)
    │
    └─► BillDelivery.DeliverAsync (process + send)
```

## Testing

**Unit Tests:** None directly (integration tested via `ExampleEmailTests`)

**Integration Tests:** `BillRelay.IntegrationTests.Services.ExampleEmailTests`
- Requires `BILLRELAY_EXAMPLE_DIRECTORY` with `.eml` samples
- Validates end-to-end E.ON download and processing

## Extension Points

Implement `IEOnBillDownloader` for:
- Different E.ON URL patterns
- Alternative download mechanisms (authenticated, redirects, etc.)
- Other providers with link-based bills (implement new `IProviderBillDownloader`)

## Logging

Uses `BillRelayOperation.ProcessAttachment`:
- `InProgress` — Link found, downloading
- `Success` — PDF downloaded, MimePart created
- `Failure` — Link not found, HTTP error, invalid content, exception

Uses `BillRelayInfoKey`:
- `MessageId` — IMAP Message-ID for correlation
- `Url` — Download URL
- `FileName` — Generated filename
- `ContentLength` — PDF byte count
- `Reason` — Failure description