# BillSender

## Overview

`BillSender` implements `IBillSender` to send bill emails via NuciNotifications API with PDF attachments encoded as Base64.

**File:** `BillRelay/Services/BillSender.cs`

## Responsibilities

- Load email template from `email-template.xml`
- Replace `{ProviderName}` placeholder
- Read PDF file and encode as Base64
- Construct NuciNotifications request
- Send via `NuciNotificationsClient`

## Key Methods

### SendAsync
```csharp
public async Task SendAsync(
    string recipient,
    string providerName,
    string filePath,
    string fileName,
    CancellationToken cancellationToken)
```

**Flow:**
1. Load template XML
2. Replace `{ProviderName}` in subject and body
3. Read PDF bytes from `filePath`
4. Convert to Base64
5. Build `NotificationRequest` with attachment
6. Call `NuciNotificationsClient.SendAsync`
7. Return result

## Email Template

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

**Template Processing:**
```csharp
var subject = template.Subject.Replace("{ProviderName}", providerName);
var body = template.Body.Replace("{ProviderName}", providerName);
```

## NuciNotifications Request

```csharp
var request = new NotificationRequest
{
    To = recipient,
    Subject = subject,
    Body = body,
    IsHtml = true,
    Attachments =
    [
        new AttachmentDto
        {
            FileName = fileName,
            ContentBase64 = Convert.ToBase64String(pdfBytes),
            MimeType = "application/pdf"
        }
    ]
};
```

## Configuration

```json
"NuciNotificationsSettings": {
  "BaseUrl": "https://api.notifications.example.com",
  "ApiKey": "api-key",
  "HmacSharedSecretKey": "hmac-secret"
},
"Delivery": {
  "Recipient": "recipient@example.com"
}
```

**Validation (Program.cs):**
- `BaseUrl` must start with `https://`
- `ApiKey` required
- `HmacSharedSecretKey` required

## Dependencies

| Dependency | Purpose |
|------------|---------|
| `NuciNotifications.Client` | API client |
| `IOptions<NuciNotificationsSettings>` | API configuration |
| `ILogger` | Structured logging |

## Logging

| Operation | Info Keys |
|-----------|-----------|
| `SendEmail` | `Recipient`, `ProviderName`, `AttachmentId`, `FileSizeBytes`, `DurationMs`, `Status` |

## Invariants

- **I-16:** Delivery only after storage
- **I-17:** Template placeholder replaced
- **I-18:** Attachment Base64 encoded

## Testing

**Unit Tests:** `BillSenderTests` (UnitTests)
- Template loading
- Placeholder replacement
- Base64 encoding
- Recipient validation

**Integration Tests:** `BillSenderTests` (IntegrationTests)
- Real template loading
- Mocked HTTP client

**Test Pattern:**
```csharp
[Test]
public async Task GivenValidPdf_WhenSending_ThenRequestBuiltCorrectly()
{
    var sender = CreateSender();
    var pdfBytes = "test pdf content"u8.ToArray();
    var tempFile = Path.GetTempFileName();
    await File.WriteAllBytesAsync(tempFile, pdfBytes);

    await sender.SendAsync("test@example.com", "Vodafone", tempFile, "bill.pdf", CancellationToken.None);

    // Verify client called with correct request
    _clientMock.Verify(c => c.SendAsync(
        It.Is<NotificationRequest>(r =>
            r.To == "test@example.com" &&
            r.Subject == "Bill from Vodafone" &&
            r.Attachments[0].ContentBase64 == Convert.ToBase64String(pdfBytes)),
        It.IsAny<CancellationToken>()), Times.Once);
}
```

## Error Handling

| Error | Behavior |
|-------|----------|
| Template file missing | Throw `FileNotFoundException` |
| PDF file missing | Throw `FileNotFoundException` |
| HTTP 401/403 | Log, throw (caller handles) |
| HTTP 429 | Log, throw (caller handles) |
| Network timeout | Log, throw (caller handles) |

**No Retry:** Caller (`BillDelivery`) handles retry via next cycle.

## Related Documentation

- [BillDelivery](../components/bill-delivery.md) — Caller
- [Integrations: NuciNotifications](../integrations.md#2-nucinotifications-integration)
- [Configuration](../configuration.md#nucinotifications-settings)
- [Security](../security.md#email-delivery)
- [Architecture: Email Delivery](../architecture.md#email-delivery)