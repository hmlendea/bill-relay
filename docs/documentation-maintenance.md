# Documentation Maintenance

## Documentation Structure

```
docs/
├── INDEX.md                      # Navigation hub
├── architecture.md               # System architecture (complements root ARCHITECTURE.md)
├── repository-overview.md        # High-level repo description
├── repository-structure.md       # Directory/file layout
├── configuration.md              # All settings reference
├── data-model.md                 # Data structures, state schema
├── build-and-deployment.md       # Build, CI, deployment
├── dependencies.md               # External/internal dependencies
├── design-decisions.md           # Architectural decisions log
├── invariants.md                 # System invariants
├── concurrency-and-scheduling.md # Concurrency model
├── state-and-persistence.md      # Persistence design
├── integrations.md               # External system integrations
├── change-guide.md               # Change procedures
├── documentation-maintenance.md  # This file
├── troubleshooting.md            # Common issues
├── faq.md                        # Frequently asked questions
├── ambiguities-and-open-questions.md # Known unknowns
├── quick-start.md                # 5-minute setup
├── testing.md                    # Test strategy
├── security.md                   # Security model
├── error-handling.md             # Error handling patterns
├── logging.md                    # Logging reference
├── components/
│   ├── INDEX.md
│   ├── bill-relay-worker.md
│   ├── attachment-processor.md
│   ├── bill-delivery.md
│   ├── bill-provider-identification.md
│   ├── attachment-validator.md
│   ├── eon-bill-downloader.md
│   ├── email-age-policy.md
│   ├── file-state-store.md
│   ├── bill-sender.md
│   ├── host-and-composition.md
│   ├── application-services.md
│   ├── integration-models.md
│   └── presentation.md
├── flows/
│   ├── INDEX.md
│   ├── startup-and-rendering.md
│   ├── inbox-poll-and-classification.md
│   └── attachment-delivery.md
├── api-reference/
│   ├── INDEX.md
│   └── (controller files - N/A for worker)
├── api-usage-examples.md
└── behaviour/
    ├── INDEX.md
    ├── browse-and-search.md
    └── inspect-edit-delete.md
```

## Maintenance Principles

### 1. Code-Proximity Rule
Documentation lives in `docs/` but references source code by **relative path from repo root**:
```markdown
See `BillRelay/Services/BillRelayWorker.cs` line 45.
```

### 2. Single Source of Truth
- **Configuration:** `docs/configuration.md` (not README)
- **Architecture:** `docs/architecture.md` + root `ARCHITECTURE.md` (complementary)
- **Component APIs:** `docs/components/*.md`
- **Data Models:** `docs/data-model.md`

### 3. No Duplication
- Root `README.md` → High-level only, links to `docs/`
- Root `ARCHITECTURE.md` → Overview, links to `docs/architecture.md`
- Component docs → Reference source, don't copy code

### 4. Cross-Reference Standard
```markdown
[Component Name](../components/component-name.md)
[Architecture: Section](../architecture.md#section)
[Configuration: Setting](../configuration.md#setting-name)
```

---

## Update Triggers

| Trigger | Documents to Review |
|---------|---------------------|
| New provider added | `components/bill-provider-identification.md`, `configuration.md`, `data-model.md` |
| New setting | `configuration.md`, `components/host-and-composition.md` |
| State schema change | `data-model.md`, `state-and-persistence.md`, `invariants.md`, `components/file-state-store.md` |
| New dependency | `dependencies.md`, `architecture.md` |
| Pipeline logic change | `flows/*.md`, relevant `components/*.md`, `invariants.md` |
| Deployment change | `build-and-deployment.md`, `security.md` |
| Architecture decision | `design-decisions.md`, `architecture.md`, `invariants.md` |

---

## Writing Standards

### Headings
- **Sentence case:** "Attachment processing" not "Attachment Processing"
- **Descriptive:** "IMAP connection handling" not "Connection"

### Code References
```markdown
**File:** `BillRelay/Services/AttachmentProcessor.cs`
**Method:** `ProcessAsync(MimePart, string, CancellationToken)`
**Line:** 42 (approximate, update on major changes)
```

### Diagrams
- Use Mermaid for flow/sequence diagrams
- Keep simple; complex diagrams → separate `.mmd` file
- Render in GitHub: ```mermaid ... ```

### Tables
- Use for configuration, invariants, decision logs
- Header row required
- Align columns for readability in source

### Links
- **Internal:** Relative paths from `docs/` (e.g., `../components/file-state-store.md`)
- **External:** Full URLs with descriptive text
- **Source code:** `BillRelay/Services/Class.cs` (relative to repo root)

---

## Review Checklist

Before merging documentation changes:

- [ ] All internal links resolve (check in GitHub)
- [ ] Code references match current source (spot-check 3)
- [ ] No duplicate content with root docs
- [ ] Headings use sentence case
- [ ] Tables render correctly
- [ ] Mermaid diagrams render
- [ ] Configuration examples match `appsettings.json`
- [ ] Cross-references updated bidirectionally

---

## Automation

### Link Checking (Manual)
```bash
# Install markdown-link-check
npm install -g markdown-link-check

# Check all docs
markdown-link-check docs/**/*.md
```

### Spell Check (Manual)
```bash
# Install cspell
npm install -g cspell

# Check
cspell "docs/**/*.md"
```

### TODO: Add to CI
- [ ] Add link check to `.github/workflows/dotnet.yml`
- [ ] Add spell check to CI

---

## Versioning

- Documentation versioned with code (same git repo)
- No separate version numbers
- `CHANGELOG.md` (if created) tracks doc changes

---

## Contributing

See `CONTRIBUTING.md` for general guidelines.

**Documentation-specific:**
1. Edit in `docs/`
2. Follow writing standards above
3. Update cross-references
4. Run link check locally
5. Include in PR description: "Docs: updated X for Y"

---

## Related Documentation

- [Repository Structure](./repository-structure.md) — Doc file locations
- [Architecture: Documentation](../architecture.md#documentation)
- [Change Guide](./change-guide.md) — Doc update requirements
- [Root README.md](../../README.md) — Entry point
- [Root ARCHITECTURE.md](../../ARCHITECTURE.md) — Architecture overview