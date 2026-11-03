# BillProviderIdentification Component

## Overview

**Location:** `BillRelay/Services/BillProviderIdentification.cs`

**Type:** Static class with domain logic

**Purpose:** Identifies utility bill providers from email metadata (subject, sender, body links) and validates provider-specific attachment filename patterns.

## Responsibilities

1. **Provider Identification** — Classify emails by provider using subject keywords, sender domains, and body link domains
2. **Attachment Validation** — Validate attachment filenames against provider-specific regex patterns
3. **Billing Subject Detection** — Determine if an email subject indicates a bill/invoice

## Key Methods

### IdentifyProvider

```csharp
public static string? IdentifyProvider(MimeMessage message)
```

**Algorithm:**
1. Extract subject, normalize (remove diacritics)
2. Check for billing keywords: "factur", "intretinere", "invoice", "bill"
3. If subject contains "hidroelectrica" → return "Hidroelectrica"
4. Extract domains from:
   - Links in HTML/Text body (via `LinkPattern` regex)
   - Sender email addresses
5. Map domains to providers via `IdentifyDomain`
6. Return provider if exactly one unique provider identified; otherwise null

### IsBillAttachment

```csharp
public static bool IsBillAttachment(MimePart attachment, string providerName)
```

**Validation:**
1. MIME type must be `application/pdf` (case-insensitive)
2. Filename must match provider-specific regex pattern

### HasBillingSubject

```csharp
private static bool HasBillingSubject(string subject)
```

Normalizes subject (NFD + remove non-spacing marks) and checks for billing keywords.

## Provider Patterns

| Provider | Subject Keywords | Sender/Link Domains | Filename Regex |
|----------|------------------|---------------------|----------------|
| **Vodafone** | factur, intretinere, invoice, bill | vodafone.ro | `^VDF[0-9]+\.pdf$` |
| **Electrica Furnizare** | factur, intretinere, invoice, bill | electricafurnizare.ro, myelectrica.ro | `^DII[0-9]P[0-9]+\.pdf$` |
| **E-bloc** | factur, intretinere, invoice, bill | e-bloc.ro | `^Nota de plat[\u0103a]\.pdf$` |
| **Hidroelectrica** | factur, intretinere, invoice, bill | hidroelectrica.ro | `^FX-[0-9]+\.pdf$` |
| **E.ON** | factur, intretinere, invoice, bill | eon.ro, myline-eon.ro, noutati.myline-eon.ro | `^.*\.pdf$` |

## Domain Matching Logic

```csharp
private static bool IsDomain(string domain, string expectedDomain) =>
    string.Equals(domain, expectedDomain, StringComparison.OrdinalIgnoreCase) ||
    domain.EndsWith("." + expectedDomain, StringComparison.OrdinalIgnoreCase);
```

Matches exact domain or subdomain (e.g., `info.electricafurnizare.ro` matches `electricafurnizare.ro`).

## Link Extraction

```csharp
[GeneratedRegex("https?://[^\\s<>\"']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
private static partial Regex LinkPattern();
```

Extracts HTTP/HTTPS URLs from message body for domain analysis.

## Provider Identification Rules

1. **Subject must contain billing keyword** — Otherwise returns null immediately
2. **Hidroelectrica special case** — Subject containing "hidroelectrica" returns provider regardless of domains
3. **Single provider requirement** — Exactly one unique provider must be identified from domains
4. **Ambiguity rejection** — Multiple providers or no providers → null
5. **Subdomain matching** — Sender/link domains match via suffix

## Configuration Integration

- `FilterSettings.Providers` array lists enabled providers
- Worker checks `providers.Contains(providerName)` before processing
- Provider must be both identified AND enabled in config

## Testing

**Unit Tests:** `BillRelay.UnitTests.Services.BillProviderIdentificationTests`
- Filename pattern validation per provider
- Provider identification from subject + links
- Negative cases: wrong MIME type, wrong filename pattern, ambiguous domains
- Diacritic normalization in subject matching

## Extension Points

To add a new provider:
1. Add provider name to `FilterSettings.Providers` in configuration
2. Add domain mapping in `IdentifyDomain`
3. Add filename regex pattern as `GeneratedRegex` method
4. Add case in `IsBillAttachment` switch
5. Add unit tests for identification and filename validation
6. Add representative `.eml` sample for integration testing

## Logging

Uses `BillRelayOperation`:
- `ProcessAttachment` — Provider identification and validation

Uses `BillRelayInfoKey`:
- `ProviderName` — Identified provider
- `Subject` — Email subject
- `Sender` — Sender address
- `Reason` — Rejection reason