# Dependencies

## External Dependencies (NuGet)

### Runtime Dependencies

| Package | Version | Purpose | License | Source |
|---------|---------|---------|---------|--------|
| `MailKit` | 4.12.0 | IMAP client, MIME parsing | MIT | NuGet.org |
| `MimeKit` | 4.12.0 | MIME message/attachment handling | MIT | NuGet.org |
| `NuciDAL` | 3.2.1 | JSON repository for state persistence | MIT | NuGet.org (hmlendea) |
| `NuciLog.Core` | 3.1.0 | Structured logging abstractions | MIT | NuGet.org (hmlendea) |
| `NuciLog` | 1.2.1 | Structured logging implementation | MIT | NuGet.org (hmlendea) |
| `NuciNotifications.Client` | 1.2.0 | NuciNotifications API client | MIT | NuGet.org (hmlendea) |
| `Microsoft.Extensions.Hosting` | 10.0.0 | Generic host, BackgroundService | MIT | NuGet.org |
| `Microsoft.Extensions.Configuration` | 10.0.0 | Configuration binding | MIT | NuGet.org |
| `Microsoft.Extensions.Configuration.Json` | 10.0.0 | JSON config provider | MIT | NuGet.org |
| `Microsoft.Extensions.Configuration.EnvironmentVariables` | 10.0.0 | Env var config provider | MIT | NuGet.org |
| `Microsoft.Extensions.Options` | 10.0.0 | Options pattern | MIT | NuGet.org |
| `Microsoft.Extensions.Logging` | 10.0.0 | Logging abstractions | MIT | NuGet.org |
| `Microsoft.Extensions.Http` | 10.0.0 | IHttpClientFactory | MIT | NuGet.org |

### Development Dependencies

| Package | Version | Purpose | Scope |
|---------|---------|---------|-------|
| `NUnit` | 4.2.2 | Test framework | Test projects |
| `NUnit3TestAdapter` | 4.6.0 | VS Test integration | Test projects |
| `Moq` | 4.20.72 | Mocking framework | Unit tests |
| `ArchUnitNET` | 1.1.0 | Architecture tests | Architecture tests |
| `Microsoft.NET.Test.Sdk` | 17.11.1 | Test runner | Test projects |

## Internal Dependencies

### Project References

```
BillRelay (executable)
    └── (no project references)

BillRelay.UnitTests
    └── BillRelay

BillRelay.IntegrationTests
    └── BillRelay

BillRelay.ArchitectureTests
    └── BillRelay
```

**Rule:** Production code has NO references to test projects (enforced by architecture tests).

## Dependency Graph

```
BillRelay
├── Microsoft.Extensions.* (Hosting, Configuration, Logging, Options, Http)
├── MailKit / MimeKit
├── NuciDAL
├── NuciLog / NuciLog.Core
└── NuciNotifications.Client

BillRelay.UnitTests
├── BillRelay
├── NUnit / Moq
└── Microsoft.Extensions.* (for test settings)

BillRelay.IntegrationTests
├── BillRelay
├── NUnit / Moq
└── Microsoft.Extensions.*

BillRelay.ArchitectureTests
├── BillRelay
├── NUnit
└── ArchUnitNET
```

## Version Management

### .NET Version

- **Target Framework:** `net10.0` (.NET 10)
- **SDK Version:** 10.0.x (defined in `global.json` if present)

### Dependency Updates

**Strategy:** Conservative — update patch versions regularly, minor versions after testing, major versions with planning.

**Commands:**
```bash
# Check outdated packages
dotnet list BillRelay/BillRelay.csproj package --outdated

# Update specific package
dotnet add BillRelay/BillRelay.csproj package MailKit --version 4.13.0

# Update all (interactive)
dotnet outdated --upgrade
```

### Lock File

- `BillRelay/obj/project.assets.json` — Generated lock file
- Committed? **No** (in `.gitignore` via `obj/`)
- Reproducible builds: `dotnet restore --locked-mode` (requires lock file)

## Transitive Dependencies

### Key Transitive Dependencies

| Package | Via | Purpose |
|---------|-----|---------|
| `BouncyCastle.Cryptography` | MailKit | TLS/crypto |
| `System.Text.Json` | NuciDAL, NuciNotifications | JSON serialization |
| `System.Security.Cryptography` | NuciNotifications | HMAC signing |
| `Polly` | NuciNotifications.Client | Retry policies (if used) |

## Architecture Test Enforcement

**File:** `BillRelay.ArchitectureTests/DependencyTests.cs`

**Rules:**
1. `BillRelay.Configuration` must not depend on `BillRelay.Services`
2. `BillRelay.Services` must not depend on `BillRelay.Program`
3. Production code must not reference `Microsoft.EntityFrameworkCore`
4. Production code must not reference test projects

## License Compliance

All dependencies use permissive licenses (MIT, Apache-2.0, BSD).

**No GPL or copyleft dependencies** in runtime.

**Verification:**
```bash
dotnet list BillRelay/BillRelay.csproj package --include-transitive --format json | jq -r '.projects[0].dependencies[] | "\(.name) \(.version) \(.license)"'
```

## Supply Chain Security

### Internal Packages (hmlendea)

| Package | Repository | Verification |
|---------|------------|--------------|
| `NuciDAL` | github.com/hmlendea/nucidal | Source available |
| `NuciLog` | github.com/hmlendea/nucilog | Source available |
| `NuciNotifications.Client` | github.com/hmlendea/nucinotifications-client | Source available |

**Risk:** Single maintainer, no formal SLSA provenance.

**Mitigation:** Pin exact versions, verify checksums, consider vendoring if critical.

### Public Packages

- `MailKit`/`MimeKit`: Mature, widely used, active maintenance
- `Microsoft.Extensions.*`: Official Microsoft, supported

## Dependency Direction Rules

```
┌─────────────────────────────────────────────────────────────┐
│                      Composition Root                        │
│                        (Program)                               │
└──────────────────────────┬──────────────────────────────────┘
                           │ references
                           ▼
┌─────────────────────────────────────────────────────────────┐
│                    Configuration                             │
│              (BillRelay.Configuration)                       │
└──────────────────────────┬──────────────────────────────────┘
                           │ referenced by
                           ▼
┌─────────────────────────────────────────────────────────────┐
│                      Services                                │
│              (BillRelay.Services)                            │
└─────────────────────────────────────────────────────────────┘
```

**Forbidden:**
- Configuration → Services
- Services → Program
- Production → Test projects
- Production → Database providers (EF Core, etc.)

## Adding New Dependencies

### Checklist

- [ ] License compatible (MIT/Apache/BSD)
- [ ] Actively maintained (recent releases, open issues)
- [ ] Minimal transitive closure
- [ ] No duplicate functionality
- [ ] Architecture test updated if new namespace
- [ ] Documented in this file

### Process

1. Add to appropriate `.csproj`
2. Run `dotnet restore`
3. Run `dotnet build`
4. Run `dotnet test`
5. Update this document

## Related Documentation

- [Architecture: External Dependencies](../architecture.md#external-dependencies)
- [Architecture: Dependency Direction and Rules](../architecture.md#dependency-direction-and-rules)
- [Build and Deployment](./build-and-deployment.md)
- [Testing](./testing.md) — Test dependencies