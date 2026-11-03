# AttachmentProcessor Component

## Overview

**Location:** `BillRelay/Services/AttachmentProcessor.cs`

**Interface:** `IAttachmentProcessor`

**Lifetime:** Singleton

**Purpose:** Handles atomic file storage of PDF attachments with deduplication, filename sanitisation, and provider name prefixing.

## Responsibilities

1. **Atomic File Write** — Temp file → hash compare → move (prevents partial writes)
2. **Content Deduplication** — SHA256 hash comparison, skip identical content
3. **Filename Sanitisation** — Remove invalid filesystem characters
4. **Provider Prefixing** — Prepend provider name to filename
4. **Directory Management** — Ensure download directory exists

## Key Methods

### ProcessAsync

```csharp
public async Task<string> ProcessAsync(Stream content, string originalFileName, Provider provider, CancellationToken cancellationToken)
{
    // 1. Compute SHA256 hash (AttachmentId)
    using var sha256 = SHA256.Create();
    var hashBytes = await sha256.ComputeHashAsync(content, cancellationToken);
    var attachmentId = Convert.ToHexString(hashBytes).ToLowerInvariant();
    content.Position = 0;

    // 2. Sanitise filename
    var safeName = SanitiseFileName(originalFileName);
    
    // 3. Prefix with provider name
    var prefixedName = $"{provider}_{safeName}";
    
    // 4. Target path
    var targetPath = Path.Combine(_storageSettings.DownloadDirectory, prefixedName);
    
    // 5. Deduplication: check if identical content already exists
    if (File.Exists(targetPath))
    {
        using var existingStream = File.OpenRead(targetPath);
        var existingHash = await sha256.ComputeHashAsync(existingStream, cancellationToken);
        var existingId = Convert.ToHexString(existingHash).ToLowerInvariant();
        
        if (existingId == attachmentId)
        {
            _logger.LogInformation("Attachment already exists, skipping: {FilePath}", targetPath);
            return attachmentId;
        }
        // Different content with same name - will overwrite after temp write
    }

    // 6. Atomic write: temp file → hash verify → move
    var tempPath = Path.GetTempFileName();
    try
    {
        await using (var tempStream = File.Create(tempPath))
        {
            await content.CopyToAsync(tempStream, cancellationToken);
        }

        // Verify hash matches
        await using (var verifyStream = File.OpenRead(tempPath))
        {
            var verifyHash = await sha256.ComputeHashAsync(verifyStream, cancellationToken);
            var verifyId = Convert.ToHexString(verifyHash).ToLowerInvariant();
            
            if (verifyId != attachmentId)
            {
                throw new InvalidOperationException("Hash mismatch after write");
            }
        }

        // Ensure directory exists
        Directory.CreateDirectory(_storageSettings.DownloadDirectory);

        // Atomic move (overwrites on Windows, replaces on Unix)
        File.Move(tempPath, targetPath, overwrite: true);
        
        _logger.LogInformation("Saved attachment: {FilePath}", targetPath);
        return attachmentId;
    }
    finally
    {
        if (File.Exists(tempPath))
            File.Delete(tempPath);
    }
}
```

### SanitiseFileName

```csharp
private static string SanitiseFileName(string fileName)
{
    var invalidChars = Path.GetInvalidFileNameChars();
    var sb = new StringBuilder(fileName.Length);
    
    foreach (var c in fileName)
    {
        if (Array.IndexOf(invalidChars, c) >= 0)
            sb.Append('_');
        else
            sb.Append(c);
    }
    
    var result = sb.ToString();
    // Trim trailing dots/spaces (Windows reserved)
    return result.TrimEnd('.', ' ');
}
```

## Dependencies (Injected)

| Dependency | Interface | Purpose |
|------------|-----------|---------|
| `ILogger<AttachmentProcessor>` | `ILogger` | Structured logging |
| `StorageSettings` | `IOptions<StorageSettings>` | Download directory path |

## File Naming Convention

```
{ProviderName}_{SanitisedOriginalFileName}
```

Examples:
- `Vodafone_VDF_2024_01_12345.pdf`
- `Electrica_Furnizare_DII_2024_P_001.pdf`
- `E-bloc_Nota_de_plat.pdf` (diacritics preserved, spaces → underscores)
- `Hidroelectrica_FX-2024-001.pdf`
- `EOn_factura_ianuarie_2024.pdf`

## Deduplication Strategy

| Level | Mechanism |
|-------|-----------|
| **Content** | SHA256 hash (`AttachmentId`) — primary deduplication key |
| **Filename** | If same name exists, compare hashes; skip if identical |
| **State Store** | `FileStateStore.IsProcessedAsync(AttachmentId)` — cross-run deduplication |

## Atomic Write Guarantees

1. **Temp file** created in system temp directory (`Path.GetTempFileName()`)
2. **Content copied** to temp file
3. **Hash verified** against original `AttachmentId`
4. **Directory created** if missing
5. **Atomic move** (`File.Move` with `overwrite: true`)
6. **Temp cleanup** in `finally` block

**Failure Modes:**
- Hash mismatch after write → `InvalidOperationException`, temp file cleaned up
- Disk full during write → IOException, temp file cleaned up
- Move fails → IOException, temp file cleaned up, original file untouched

## Concurrency

- **Single-writer assumption** — Worker is single-threaded
- **No file locking** — Relies on atomic move semantics
- **Cross-process safety** — Not designed for concurrent writers

## Logging

Uses `BillRelayOperation`:
- `ProcessAttachment` — Top-level processing
- `ValidateAttachment` — Validation (delegated to validator)
- `WriteAttachment` — Temp file write
- `SaveAttachment` — Atomic move to final location

Uses `BillRelayInfoKey`:
- `AttachmentId` — SHA256 hash
- `FilePath` — Final file path
- `FileName` — Original filename
- `ProviderName` — Provider enum value

## Extension Points

Implement `IAttachmentProcessor` for custom storage:
- Cloud storage (S3, Azure Blob)
- Database BLOB storage
- Encrypted filesystem
- Versioned storage

## Configuration

Controlled via `StorageSettings`:
- `DownloadDirectory` — Target directory (default `./bills`)

## Testing

**Unit Tests:** `BillRelay.UnitTests.Services.AttachmentProcessorTests`
- Happy path: file saved, hash returned
- Deduplication: identical content skipped
- Sanitisation: invalid chars replaced
- Atomic write: temp file cleaned on failure
- Provider prefix applied

**Integration Tests:** `BillRelay.IntegrationTests.Services.FileStateStoreIntegrationTests` (indirect)