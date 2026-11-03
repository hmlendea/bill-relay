# Repository Structure

## Source Tree Layout

```
BillRelay.slnx                          # SDK-style solution file
├── BillRelay/                          # Main worker project (executable)
│   ├── BillRelay.csproj                # Project file (net10.0, Exe output)
│   ├── Program.cs                      # Composition root, DI registration
│   ├── appsettings.json                # Configuration template (placeholders)
│   ├── Configuration/                  # Settings classes (POCOs)
│   │   ├── AttachmentSettings.cs
│   │   ├── BillRelaySettings.cs
│   │   ├── DeliverySettings.cs
│   │   ├── FilterSettings.cs
│   │   ├── ImapSettings.cs
│   │   ├── PollingSettings.cs
│   │   ├── RetentionSettings.cs
│   │   └── StorageSettings.cs
│   ├── Services/                       # Core business logic services
│   │   ├── Attachments/                # Attachment-specific implementations
│   │   │   ├── AttachmentValidator.cs
│   │   │   ├── EOnBillDownloader.cs
│   │   │   ├── IAttachmentValidator.cs
│   │   │   └── IEOnBillDownloader.cs
│   │   ├── AttachmentProcessor.cs
│   │   ├── BillDelivery.cs
│   │   ├── BillProviderIdentification.cs
│   │   ├── BillRelayWorker.cs
│   │   ├── BillSender.cs
│   │   ├── EmailAgePolicy.cs
│   │   ├── FileStateStore.cs
│   │   ├── IAttachmentProcessor.cs
│   │   ├── IBillDelivery.cs
│   │   ├── IBillSender.cs
│   │   ├── IStateStore.cs
│   │   └── ImapCheckpoint.cs
│   ├── Logging/                        # NuciLog structured logging keys
│   │   ├── BillRelayInfoKey.cs
│   │   └── BillRelayOperation.cs
│   └── DataAccess/
│       └── DataObjects/                # XML-serialised data objects
│           ├── EmailTemplateDataObject.cs
│           └── ProcessedAttachmentDataObject.cs
├── BillRelay.UnitTests/                # Unit tests (NUnit 4.x + Moq)
│   ├── BillRelay.UnitTests.csproj
│   └── Services/
│       ├── AttachmentProcessorTests.cs
│       ├── BillDeliveryTests.cs
│       ├── BillProviderIdentificationTests.cs
│       ├── BillSenderTests.cs
│       ├── EmailAgePolicyTests.cs
│       └── FileStateStoreIntegrationTests.cs
├── BillRelay.IntegrationTests/         # Integration tests (real FS, components)
│   ├── BillRelay.IntegrationTests.csproj
│   ├── Configuration/
│   │   └── BillRelaySettingsTests.cs
│   ├── Logging/
│   │   └── NuciLoggerTests.cs
│   └── Services/
│       ├── BillSenderTests.cs
│       ├── EmailAgePolicyTests.cs
│       ├── ExampleEmailTests.cs
│       └── FileStateStoreIntegrationTests.cs
├── BillRelay.ArchitectureTests/        # Architecture fitness tests (ArchUnit.NET)
│   ├── BillRelay.ArchitectureTests.csproj
│   └── DependencyTests.cs
├── email-template.xml                  # Email template (Subject/Body with {ProviderName})
├── release.sh                          # Release script (delegates to external script)
├── .github/
│   └── workflows/
│       └── dotnet.yml                  # CI pipeline (build, test)
├── ARCHITECTURE.md                     # Root architecture documentation
├── README.md                           # Project overview and usage
├── SECURITY.md                         # Security policy
├── PRIVACY.md                          # Privacy policy
├── LICENSE                             # MIT License
├── CONTRIBUTING.md                     # Contribution guidelines
├── CODE_OF_CONDUCT.md                  # Community standards
└── ROADMAP.md                          # Project roadmap
```

## Module Organisation

### BillRelay (Production)

| Namespace | Purpose | Key Types |
|-----------|---------|-----------|
| `BillRelay` | Composition root | `Program` |
| `BillRelay.Configuration` | Settings POCOs | `BillRelaySettings`, `ImapSettings`, `PollingSettings`, `FilterSettings`, `AttachmentSettings`, `StorageSettings`, `RetentionSettings`, `DeliverySettings` |
| `BillRelay.Services` | Core services | `BillRelayWorker`, `BillProviderIdentification`, `BillDelivery`, `BillSender`, `AttachmentProcessor`, `EmailAgePolicy`, `FileStateStore`, `ImapCheckpoint` |
| `BillRelay.Services.Attachments` | Attachment-specific | `AttachmentValidator`, `EOnBillDownloader`, `IAttachmentValidator`, `IEOnBillDownloader` |
| `BillRelay.Logging` | Structured logging keys | `BillRelayOperation`, `BillRelayInfoKey` |
| `BillRelay.DataAccess.DataObjects` | XML-serialised DTOs | `EmailTemplateDataObject`, `ProcessedAttachmentDataObject` |

### BillRelay.UnitTests

| Namespace | Purpose |
|-----------|---------|
| `BillRelay.UnitTests.Services` | Unit tests for each service (mocked dependencies) |

### BillRelay.IntegrationTests

| Namespace | Purpose |
|-----------|---------|
| `BillRelay.IntegrationTests.Configuration` | Configuration binding tests |
| `BillRelay.IntegrationTests.Logging` | NuciLog integration tests |
| `BillRelay.IntegrationTests.Services` | Integration tests with real filesystem/components |

### BillRelay.ArchitectureTests

| Namespace | Purpose |
|-----------|---------|
| `BillRelay.ArchitectureTests` | ArchUnit.NET dependency and visibility rules |

## Naming Conventions

- **Projects**: PascalCase, suffix indicates type (`.UnitTests`, `.IntegrationTests`, `.ArchitectureTests`)
- **Files**: PascalCase for types, kebab-case for non-code (`email-template.xml`, `release.sh`)
- **Namespaces**: Mirror folder structure exactly
- **Interfaces**: `I` prefix (`IStateStore`, `IBillSender`)
- **Settings classes**: `*Settings` suffix
- **Data objects**: `*DataObject` suffix (XML serialisation)
- **Operations/InfoKeys**: PascalCase static properties on dedicated classes

## Configuration Files

| File | Location | Purpose |
|------|----------|---------|
| `appsettings.json` | `BillRelay/` | Runtime configuration template with placeholders |
| `email-template.xml` | Repository root | Email subject/body template (copied to output) |

## Build Artifacts

- `BillRelay/bin/Debug/net10.0/` — Debug build output
- `BillRelay/bin/Release/net10.0/` — Release build output
- `BillRelay/obj/` — MSBuild intermediate files
- Test projects have equivalent `bin/` and `obj/` directories

## Output Files (Runtime)

| Path | Purpose |
|------|---------|
| `./bills/` | Downloaded PDF attachments |
| `./state/billrelay.json` | Processed attachment ledger (JSON array) |
| `./state/billrelay.json.checkpoint` | IMAP checkpoint (UID validity, last UID, last date) |
| `logfile.log` | NuciLog file output (if enabled) |