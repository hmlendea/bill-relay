# EOnBillDownloader Component

## Overview

**Location:** `BillRelay/Services/Attachments/EOnBillDownloader.cs`

**Interface:** `IEOnBillDownloader`

**Lifetime:** Singleton

**Purpose:** Downloads E.ON bill PDFs from links embedded in email body (E.ON sends download links, not attachments).

## Responsibilities

1. **Link Extraction** — Find E.ON download URLs in HTML email body
2. **Token Extraction** — Read the signed invoice token and API subscription key from the link and body
3. **Invoice API Download** — `POST https://api2.eon.ro/invoices/v1/guest/pdf` with the token, timeout-bounded
4. **Content Validation** — Verify the response is JSON containing a non-empty base64 `file`
5. **MIME Part Creation** — Wrap decoded PDF bytes as `MimePart` for downstream processing

> The public `www.eon.ro/descarca-factura` page is protected by a Cloudflare managed challenge that blocks non-browser clients. The downloader therefore never fetches that page; it calls the un-challenged `api2.eon.ro` invoice endpoint used by the page itself.

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
2. Match `EOnDownloadLinkPattern` regex: `https://www\.eon\.ro/descarc(?:are|a)-factura\?[^""'\s<>]+`
3. HTML-decode the match, then extract the `token` query parameter (`EOnTokenPattern`)
4. If no match or token → log debug, return null
5. Read the `Ocp-Apim-Subscription-Key` from the message body (`EOnSubscriptionKeyPattern`), falling back to the documented public key
6. Create `HttpClient` via `IHttpClientFactory`
7. Set timeout from `AttachmentSettings.DownloadTimeoutSeconds` (default 30s)
8. `POST https://api2.eon.ro/invoices/v1/guest/pdf` with body `{"token":"..."}`, headers `Ocp-Apim-Subscription-Key` and `X-Client-Source: mylineWeb`
9. Validate response:
   - Success status code
   - Content-Type = `application/json`
   - `file` property contains non-empty base64 content
10. Decode the base64 `file` into PDF bytes
11. Create `MimePart` with:
   - MIME type: `application/pdf`
   - Filename: `E.ON_Factura_{messageId}.pdf` (colons replaced)
   - Content: Decoded bytes as `MemoryStream`
   - Content-Disposition: Attachment
   - Content-Transfer-Encoding: Base64
12. Return `MimePart` for downstream validation/processing

## Regex Patterns

| Pattern | Purpose |
|---------|---------|
| `EOnDownloadLinkPattern` | Invoice download link: `https://www.eon.ro/descarca-factura?...` (also tolerates the legacy `descarcare-factura` spelling) |
| `EOnTokenPattern` | Signed invoice token query parameter: `[?&]token=...` |
| `EOnSubscriptionKeyPattern` | API subscription key embedded in the body: `subscription-key=...` |

## Configuration

Controlled via `AttachmentSettings`:
- `DownloadTimeoutSeconds` — HTTP request timeout (default 30s)

## Error Handling

| Failure | Behavior |
|---------|----------|
| No download link found | Log debug, return null |
| Link without token | Log debug, return null |
| HTTP error status | Log error, return null |
| Non-JSON content type | Log error, return null |
| Missing or empty `file` | Log error, return null |
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
    │               ├─► Extract token from the invoice link in the HTML body
    │               ├─► POST invoice API with token (timeout-bounded)
    │               ├─► Decode base64 PDF from the JSON response
    │               └─► Return MimePart
    │
    ├─► AttachmentValidator.GetFailureReason (MIME type, size, PDF signature)
    │
    └─► BillDelivery.DeliverAsync (process + send)
```

## Testing

**Unit Tests:** `BillRelay.UnitTests.Services.Attachments.EOnBillDownloaderTests`
- Stubbed `HttpMessageHandler`; no network access
- Covers link/token extraction, HTML-encoded links, subscription-key propagation, API failures, and empty/missing `file` responses

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