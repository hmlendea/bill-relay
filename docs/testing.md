# Testing

## Test Strategy

Bill Relay uses a three-layer test pyramid:

```
                    ┌─────────────────────┐
                    │  Architecture Tests │  ← Dependency rules, forbidden patterns
                    │  (BillRelay.ArchitectureTests)  │
                    └──────────┬──────────┘
                               │
              ┌────────────────┼────────────────┐
              ▼                ▼                ▼
       ┌─────────────┐  ┌─────────────┐  ┌─────────────┐
       │ Unit Tests  │  │Integration  │  │  Example    │
       │ (UnitTests) │  │  Tests      │  │  Email Tests│
       │             │  │(Integration)│  │ (Integration)│
       └─────────────┘  └─────────────┘  └─────────────┘
```

## Test Projects

### BillRelay.UnitTests

**Framework:** NUnit 4.x + Moq

**Purpose:** Fast, isolated unit tests for business logic with mocked dependencies.

**Coverage Areas:**

| Test Class | Target | Key Scenarios |
|------------|--------|---------------|
| `AttachmentProcessorTests` | `AttachmentProcessor` | Happy path, deduplication, sanitization, provider prefix, cancellation, stale temp files, non-part rejection |
| `BillDeliveryTests` | `BillDelivery` | Deduplication skip, successful delivery marks processed, send failure doesn't mark processed, state store failure propagation |
| `BillProviderIdentificationTests` | `BillProviderIdentification` | Filename patterns per provider, provider identification from subject+links, negative cases (wrong MIME, wrong pattern, ambiguous domains), diacritic normalization |
| `BillSenderTests` | `BillSender` | Template loading, placeholder replacement, attachment Base64 encoding, recipient validation |
| `EmailAgePolicyTests` | `EmailAgePolicy` | Cutoff calculation, InitialProcessingDate precedence, checkpoint date precedence, eligibility boundaries |
| `FileStateStoreIntegrationTests` | `FileStateStore` | Initialize, checkpoint load/save, IsProcessed, MarkProcessed, retention (age + count) |

**Run Command:**
```bash
dotnet test BillRelay.UnitTests/BillRelay.UnitTests.csproj
```

### BillRelay.IntegrationTests

**Framework:** NUnit 4.x (real filesystem, real components)

**Purpose:** Test component interactions with real implementations (no mocks for filesystem, NuciDAL, etc.).

**Coverage Areas:**

| Test Class | Target | Key Scenarios |
|------------|--------|---------------|
| `BillRelaySettingsTests` | Configuration binding | JSON binding, environment variable overrides, validation |
| `NuciLoggerTests` | NuciLog integration | Log levels, structured fields, file output |
| `BillSenderTests` | `BillSender` + NuciNotifications client | Real template loading, email construction (mocked HTTP) |
| `EmailAgePolicyTests` | `EmailAgePolicy` | Integration with real DateTimeOffset |
| `FileStateStoreIntegrationTests` | `FileStateStore` | Real JSON repository, file I/O, retention |
| `ExampleEmailTests` | End-to-end with `.eml` samples | Provider identification, attachment validation, file saving |

**Run Command:**
```bash
dotnet test BillRelay.IntegrationTests/BillRelay.IntegrationTests.csproj
```

**Example Email Tests (Optional):**
```bash
BILLRELAY_EXAMPLE_DIRECTORY="/path/to/email-samples" dotnet test BillRelay.IntegrationTests/BillRelay.IntegrationTests.csproj
```

### BillRelay.ArchitectureTests

**Framework:** NUnit 4.x + ArchUnit.NET

**Purpose:** Enforce architectural constraints and dependency rules.

**Rules Enforced (in `DependencyTests.cs`):**

| Rule | Description |
|------|-------------|
| Configuration independence | `BillRelay.Configuration` namespace has no dependency on `BillRelay.Services` |
| Services don't reference Program | `BillRelay.Services` has no dependency on `BillRelay.Program` |
| No database in production | Production code has no reference to `Microsoft.EntityFrameworkCore` or similar |
| Test isolation | Production code has no dependency on test projects |

**Run Command:**
```bash
dotnet test BillRelay.ArchitectureTests/BillRelay.ArchitectureTests.csproj
```

## Test Organization

### Naming Conventions

- **Test Classes:** `{TargetClass}Tests` (e.g., `AttachmentProcessorTests`)
- **Test Methods:** `Given{Context}_When{Action}_Then{Outcome}` (BDD-style)
- **Test Files:** `{TargetClass}Tests.cs` in matching namespace folder

### Test Structure (AAA Pattern)

```csharp
[Test]
public async Task GivenAValidPdfAttachment_WhenProcessingIt_ThenTheFileIsSaved()
{
    // Arrange
    string pdfContent = "%PDF-1.4\n%Test PDF content\n%%EOF";
    MimePart attachment = CreateMimePart("test.pdf", "application/pdf", pdfContent);
    string attachmentIdentifier = "test123";

    // Act
    await processor.ProcessAsync(attachment, attachmentIdentifier, CancellationToken.None);

    // Assert
    string[] files = Directory.GetFiles(temporaryDirectory);
    Assert.That(files, Has.Length.EqualTo(1));
    Assert.That(Path.GetFileName(files[0]), Is.EqualTo("test.pdf"));
    string savedContent = await File.ReadAllTextAsync(files[0]);
    Assert.That(savedContent, Is.EqualTo(pdfContent));
}
```

## Test Data

### Unit Test Helpers

```csharp
// Common in UnitTests
private static MimePart CreateMimePart(string fileName, string mimeType, string content)
    => new(mimeType)
    {
        FileName = fileName,
        Content = new MimeContent(new MemoryStream(Encoding.UTF8.GetBytes(content)))
    };
```

### Integration Test Fixtures

- Temporary directories created per test (`Path.GetTempPath()` + GUID)
- Cleanup in `TearDown` or `finally` blocks
- Real `BillRelaySettings` with test-specific paths

### Example Email Samples

**Location:** External (not in repo) — set via `BILLRELAY_EXAMPLE_DIRECTORY`

**Format:** `.eml` files (RFC 822)

**Purpose:** Validate real-world provider identification and attachment processing

**Providers Covered:** Vodafone, Electrica Furnizare, E-bloc, Hidroelectrica, E.ON

## Coverage Goals

| Layer | Target | Rationale |
|-------|--------|-----------|
| Unit Tests | High (>80%) | Business logic, edge cases, error paths |
| Integration Tests | Medium | Component wiring, filesystem, config binding |
| Architecture Tests | 100% of rules | Prevent architectural drift |
| Example Email Tests | All providers | Real-world validation |

**No formal coverage tool configured** — coverage assessed qualitatively.

## Running Tests

### All Tests
```bash
dotnet test BillRelay.slnx
```

### Specific Project
```bash
dotnet test BillRelay.UnitTests/BillRelay.UnitTests.csproj
dotnet test BillRelay.IntegrationTests/BillRelay.IntegrationTests.csproj
dotnet test BillRelay.ArchitectureTests/BillRelay.ArchitectureTests.csproj
```

### With Filter
```bash
dotnet test --filter "FullyQualifiedName~AttachmentProcessorTests"
dotnet test --filter "Category=Unit"
```

### Verbose Output
```bash
dotnet test --logger "console;verbosity=detailed"
```

## CI Integration

**GitHub Actions:** `.github/workflows/dotnet.yml`

```yaml
- name: Test
  run: dotnet test --no-restore --verbosity normal
```

Runs on every push/PR to main branches.

## Test Patterns

### Mocking (Unit Tests)

```csharp
// Logger mock
var loggerMock = new Mock<ILogger>();

// Verify logging
loggerMock.Verify(
    logger => logger.Info(
        It.Is<Operation>(o => o.Name == "SaveAttachment"),
        It.Is<OperationStatus>(s => s.Name == "Success"),
        It.Is<LogInfo[]>(details => details.Any(d => d.Key.Name == "FilePath"))),
    Times.Once);
```

### Settings Injection

```csharp
// Unit tests: Options.Create with test settings
var settings = Options.Create(new BillRelaySettings
{
    Storage = new() { DownloadDirectory = tempDir },
    Attachments = new() { PermittedMimeTypes = ["application/pdf"], MaximumSizeBytes = 25_000_000 }
});
```

### Async Testing

```csharp
[Test]
public async Task GivenCancellation_WhenWriting_ThenOperationCanceled()
{
    using var cts = new CancellationTokenSource();
    cts.Cancel();

    await Assert.ThatAsync(
        () => processor.ProcessAsync(attachment, "id", cts.Token),
        Throws.InstanceOf<OperationCanceledException>());
}
```

### File System Testing

```csharp
// Temporary directory per test
string tempDir = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}");
Directory.CreateDirectory(tempDir);

try
{
    // Test with real filesystem
}
finally
{
    if (Directory.Exists(tempDir))
        Directory.Delete(tempDir, true);
}
```

## Known Test Gaps

1. **No IMAP integration tests** — Requires live IMAP server
2. **No NuciNotifications API integration tests** — Requires live API endpoint
3. **No end-to-end smoke test** — Full pipeline with real services
4. **No performance/load tests** — Sequential processing limits throughput
5. **No chaos testing** — Network failures, disk full, clock skew
6. **No contract tests** — NuciNotifications API schema validation
7. **Retention not tested in worker context** — Only `FileStateStore` unit tested

## Adding New Tests

### For New Provider

1. Add test cases to `BillProviderIdentificationTests`
2. Add `.eml` sample to example directory (private)
3. Run `ExampleEmailTests` with `BILLRELAY_EXAMPLE_DIRECTORY`

### For New Service

1. Create `{ServiceName}Tests.cs` in appropriate test project
2. Follow BDD naming convention
3. Mock external dependencies (IMAP, HTTP, filesystem for unit tests)
4. Add integration test with real dependencies where valuable

### For New Architecture Rule

1. Add test method to `DependencyTests.cs`
2. Use ArchUnit.NET fluent API
3. Document rule in `ARCHITECTURE.md`

## Related Documentation

- [Components](./components/) — Individual component test coverage
- [Architecture: Testing and Verification](../architecture.md#testing-and-verification)
- [Change Guide](./change-guide.md) — Testing requirements for changes
- [Repository Structure](./repository-structure.md) — Test project layout