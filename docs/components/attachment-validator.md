# AttachmentValidator Component

## Overview

**Location:** `BillRelay/Services/Attachments/AttachmentValidator.cs`

**Interface:** `IAttachmentValidator`

**Lifetime:** Singleton

**Purpose:** Validates MIME attachments against configured rules: permitted MIME types, maximum size, and PDF signature.

## Responsibilities

1. **MIME Type Validation** — Check against `AttachmentSettings.PermittedMimeTypes`
2. **Size Validation** — Check decoded content length against `AttachmentSettings.MaximumSizeBytes`
3. **PDF Signature Validation** — Verify file starts with `%PDF-` bytes
4. **Failure Reason Reporting** — Return descriptive failure reason for logging

## Key Methods

### IsPermitted

```csharp
public bool IsPermitted(MimeEntity attachment)
```

Returns true if `GetFailureReason` returns null.

### GetFailureReason

```csharp
public string? GetFailureReason(MimeEntity attachment)
```

**Validation Sequence:**
1. Must be `MimePart` — otherwise "The attachment is not a MIME part."
2. MIME type in permitted list — otherwise "The MIME type '{type}' is not permitted."
3. Size ≤ maximum — otherwise "The attachment size ({size} bytes) exceeds the maximum of {max} bytes."
4. PDF signature (`%PDF-`) — otherwise "The attachment does not start with the required PDF signature '%PDF-'."

**Returns:** First failure reason, or null if all pass.

## Configuration

Controlled via `AttachmentSettings`:

| Setting | Default | Description |
|---------|---------|-------------|
| `PermittedMimeTypes` | `["application/pdf"]` | Allowed MIME types |
| `MaximumSizeBytes` | `25_000_000` (25 MB) | Max decoded attachment size |
| `DownloadTimeoutSeconds` | `30` | HTTP timeout (used by E.ON downloader) |

## Validation Details

### MIME Type Check
```csharp
settings.Value.Attachments.PermittedMimeTypes.Contains(part.ContentType.MimeType)
```
Case-sensitive comparison against configured array.

### Size Check
```csharp
attachment.Content?.Stream?.Length ?? 0
```
Uses the MIME part's content stream length (decoded size).

### PDF Signature Check
```csharp
using MemoryStream content = new();
attachment.Content.DecodeTo(content);
byte[] bytes = content.ToArray();
return bytes.AsSpan().StartsWith("%PDF-"u8);
```
Decodes the full attachment to memory and checks first 5 bytes.

## Error Handling

- Returns descriptive string for first failure
- Does not throw; caller decides action
- All exceptions from MimeKit decoding propagate

## Testing

**Unit Tests:** Covered indirectly via `BillProviderIdentificationTests` (which uses `IsBillAttachment` that calls validator)

**Integration Tests:** `BillRelay.IntegrationTests.Services.ExampleEmailTests` validates real emails

## Extension Points

Implement `IAttachmentValidator` for custom validation:
- Additional MIME types
- Content-based validation (malware scanning, etc.)
- Provider-specific rules
- Async validation (external API calls)

## Logging

The validator itself does not log; callers (`BillRelayWorker`, `BillDelivery`) log validation results using:
- `BillRelayOperation.ValidateAttachment`
- `BillRelayInfoKey.Reason` — Failure reason
- `BillRelayInfoKey.AttachmentId` — For correlation