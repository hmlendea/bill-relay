# Change Guide

## Purpose

Guide for making changes to Bill Relay safely and consistently.

---

## Change Categories

| Category | Examples | Review Required |
|----------|----------|-----------------|
| **Configuration** | New settings, default changes | Low |
| **Provider Support** | New provider regex, E.ON link pattern | Medium |
| **Pipeline Logic** | Processing order, validation rules | High |
| **Storage Format** | State schema, file naming | High |
| **Dependencies** | Package updates, new packages | Medium |
| **Architecture** | New services, DI changes | High |

---

## Pre-Change Checklist

- [ ] Read relevant component documentation
- [ ] Check architecture tests (`BillRelay.ArchitectureTests`)
- [ ] Identify affected invariants (`invariants.md`)
- [ ] Plan test updates (unit + integration)
- [ ] Consider backward compatibility
- [ ] Plan rollback procedure

---

## Common Change Patterns

### Adding a New Provider

**Files to Modify:**
1. `BillProviderIdentification.cs` — Add provider enum, regex, keywords
2. `BillProviderIdentificationTests.cs` — Add test cases
3. `ExampleEmailTests` — Add `.eml` sample (private)
4. `configuration.md` — Document provider patterns

**Steps:**
```csharp
// 1. Add to Provider enum
public enum BillProvider
{
    // ...existing...
    NewProvider
}

// 2. Add identification keywords
private static readonly string[] NewProviderKeywords = ["newprovider", "new provider"];

// 3. Add filename regex
private static readonly Regex NewProviderRegex = new(@"^NewProvider_\d+\.pdf$", RegexOptions.Compiled);

// 4. Update IdentifyProvider and IsBillAttachment
```

**Testing:**
- Unit tests for identification logic
- Integration test with real `.eml` sample
- Verify no false positives on existing providers

---

### Changing Attachment Validation

**Files to Modify:**
1. `AttachmentValidator.cs` — MIME types, size, signature
2. `AttachmentSettings.cs` — Configuration
3. `AttachmentValidatorTests` — Test new rules
4. `AttachmentProcessorTests` — Test processing with new rules

**Invariants to Preserve:**
- I-12: Only PDF stored
- I-13: Size limit enforced
- I-6: Hash matches content

---

### Modifying State Schema

**Files to Modify:**
1. `ProcessedAttachmentDataObject.cs` — Add fields
2. `FileStateStore.cs` — Handle migration
3. `FileStateStoreIntegrationTests` — Test migration
4. `invariants.md` — Update if new invariants

**Migration Strategy:**
```csharp
// In FileStateStore constructor or LoadAll
var loaded = _repository.LoadAll();
foreach (var item in loaded)
{
    if (item.NewField == null)
        item.NewField = DefaultValue;  // Backfill
}
_repository.SaveAll(loaded);  // Rewrite with new schema
```

**Never:** Remove fields without migration.

---

### Updating Dependencies

**Process:**
```bash
# 1. Check outdated
dotnet list BillRelay/BillRelay.csproj package --outdated

# 2. Update one at a time
dotnet add BillRelay/BillRelay.csproj package MailKit --version 4.13.0

# 3. Build and test
dotnet build
dotnet test

# 4. Check for breaking changes in release notes
```

**Architecture Test:** May need updates if new namespaces introduced.

---

### Changing Polling Behavior

**Files:**
- `PollingSettings.cs` — Configuration
- `BillRelayWorker.cs` — Loop logic
- `EmailAgePolicy.cs` — Age calculation

**Invariants:**
- I-10: Checkpoint advances
- I-11: Age filter consistency
- I-23: Sequential processing

**Testing:**
- Integration test with mocked time
- Verify checkpoint behavior on edge cases

---

## Testing Requirements

### Minimum Test Coverage for Changes

| Change Type | Unit Tests | Integration Tests | Architecture Tests |
|-------------|------------|-------------------|-------------------|
| New provider | Required | Required (sample) | No |
| Validation rule | Required | Required | No |
| State schema | Required | Required | No |
| New dependency | No | No | **Required** |
| Pipeline logic | Required | Required | Maybe |
| Config only | No | Required | No |

### Running Tests Before Commit

```bash
# Full suite
dotnet test BillRelay.slnx

# Quick feedback (unit only)
dotnet test BillRelay.UnitTests/BillRelay.UnitTests.csproj

# Architecture rules
dotnet test BillRelay.ArchitectureTests/BillRelay.ArchitectureTests.csproj
```

---

## Deployment Considerations

### Configuration Changes

- **Breaking:** Removing required settings
- **Non-breaking:** Adding optional settings with defaults
- **Action:** Document in `configuration.md`, update `appsettings.json` example

### State Schema Changes

- **Forward compatible:** New optional fields (backfill on load)
- **Breaking:** Removing fields, changing types
- **Action:** Migration code in `FileStateStore`, test with old state file

### Binary Compatibility

- Single executable — no version negotiation
- **Action:** Blue-green or stop-start deployment

---

## Rollback Procedures

### Configuration Rollback
```bash
# Restore previous appsettings.json
cp /backup/appsettings.json.v1 /opt/billrelay/appsettings.json
systemctl restart billrelay
```

### State Rollback
```bash
# Restore state files (see state-and-persistence.md)
cp /backup/billrelay.json /opt/billrelay/state/
cp /backup/billrelay.json.checkpoint /opt/billrelay/state/
systemctl restart billrelay
```

### Binary Rollback
```bash
# Restore previous publish output
rsync -av --exclude='state' --exclude='bills' /backup/billrelay-v1.2.3/ /opt/billrelay/
systemctl restart billrelay
```

---

## Documentation Updates

**Required for Every Change:**
- [ ] Update component documentation in `docs/components/`
- [ ] Update `docs/configuration.md` if settings changed
- [ ] Update `docs/dependencies.md` if packages changed
- [ ] Update `docs/invariants.md` if invariants changed
- [ ] Update `docs/design-decisions.md` if architectural decision
- [ ] Update `CHANGELOG.md` (if exists)

---

## Code Review Checklist

- [ ] All tests pass
- [ ] Architecture tests pass
- [ ] No new compiler warnings
- [ ] Configuration documented
- [ ] Invariants preserved
- [ ] Logging uses `BillRelayOperation` and `BillRelayInfoKey`
- [ ] Cancellation tokens plumbed (if async)
- [ ] No secrets in code/logs
- [ ] Error handling follows patterns (log, don't swallow)

---

## Release Process

1. **Version Bump:** Update `BillRelay.csproj` `<Version>`
2. **Changelog:** Document changes
3. **Tag:** `git tag v1.x.x`
4. **Build:** `dotnet publish -c Release`
5. **Deploy:** Follow deployment guide
6. **Verify:** Check logs for successful cycles

---

## Related Documentation

- [Testing](./testing.md) — Test requirements
- [Invariants](./invariants.md) — System invariants
- [Architecture Tests](../components/architecture-tests.md) — Dependency rules
- [Build and Deployment](./build-and-deployment.md) — Deployment procedures
- [Configuration](./configuration.md) — Settings reference