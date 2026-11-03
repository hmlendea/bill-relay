# Bill Relay Documentation

Master index and navigation for the Bill Relay documentation.

## Repository Summary

Bill Relay is a .NET 10 worker service that monitors an IMAP inbox for supported utility bills, validates their PDF attachments, saves them locally, and forwards them through the NuciNotifications API for email delivery.

## Root Document Map

- [ARCHITECTURE.md](../ARCHITECTURE.md) — High-level architecture and system context
- [SECURITY.md](../SECURITY.md) — Security model and vulnerability reporting
- [ROADMAP.md](../ROADMAP.md) — Project roadmap and planned features
- [PRIVACY.md](../PRIVACY.md) — Data handling and privacy policy
- [LICENSE](../LICENSE) — MIT License
- [README.md](../README.md) — Project overview, usage, and configuration
- [CONTRIBUTING.md](../CONTRIBUTING.md) — Contribution guidelines
- [CODE_OF_CONDUCT.md](../CODE_OF_CONDUCT.md) — Community standards

## Documentation Catalogue

### Getting Started
- [Quick Start](./quick-start.md) — Getting started guide for newcomers
- [Repository Overview](./repository-overview.md) — Repository purpose, scope, and entry points
- [Repository Structure](./repository-structure.md) — Source tree layout and module organisation

### Architecture & Design
- [Architecture](./architecture.md) — High-level architecture (complements root ARCHITECTURE.md)
- [Components](./components/) — Per-component deep dives
  - [Presentation](./components/presentation.md) — Entry point and hosting
  - [Host and Composition](./components/host-and-composition.md) — DI container and service registration
  - [Application Services](./components/application-services.md) — Core business logic services
  - [Integration Models](./components/integration-models.md) — External integration types
  - [BillRelayWorker](./components/bill-relay-worker.md) — Background service, polling loop
  - [AttachmentProcessor](./components/attachment-processor.md) — Attachment saving, deduplication, hashing
  - [BillDelivery](./components/bill-delivery.md) — Delivery orchestration, deduplication
  - [BillProviderIdentification](./components/bill-provider-identification.md) — Provider detection from email
  - [AttachmentValidator](./components/attachment-validator.md) — MIME, size, PDF signature validation
  - [EOnBillDownloader](./components/eon-bill-downloader.md) — E.ON link extraction and download
  - [EmailAgePolicy](./components/email-age-policy.md) — Processing eligibility cutoff
  - [FileStateStore](./components/file-state-store.md) — JSON state persistence, checkpoint, retention
  - [BillSender](./components/bill-sender.md) — NuciNotifications email sending
- [Flows](./flows/) — End-to-end execution flows
  - [Startup and Rendering](./flows/startup-and-rendering.md) — Application startup sequence
  - [Inbox Poll and Classification](./flows/inbox-poll-and-classification.md) — IMAP polling and message classification
  - [Attachment Delivery](./flows/attachment-delivery.md) — Attachment processing and delivery
- [Data Model](./data-model.md) — Domain entities, relationships, and persistence
- [Dependencies](./dependencies.md) — External and internal dependencies
- [Design Decisions](./design-decisions.md) — Key architectural choices and rationale
- [Invariants](./invariants.md) — System-wide invariants and contracts

### Configuration & Operations
- [Configuration](./configuration.md) — Configuration schema, sources, and precedence
- [Build and Deployment](./build-and-deployment.md) — Build pipeline, deployment, and environments
- [Logging](./logging.md) — Logging framework, levels, structured fields, correlation, sinks
- [Error Handling](./error-handling.md) — Error taxonomy, handling patterns, recovery
- [Security](./security.md) — Security model, threats, mitigations (complements root SECURITY.md)
- [State and Persistence](./state-and-persistence.md) — State management, stores, caches, migrations
- [Concurrency and Scheduling](./concurrency-and-scheduling.md) — Threading, async, schedulers, locks
- [Integrations](./integrations.md) — External system integrations

### Testing & Quality
- [Testing](./testing.md) — Test strategy, organisation, coverage
- [Change Guide](./change-guide.md) — How to modify common areas safely
- [Documentation Maintenance](./documentation-maintenance.md) — How to keep docs current, ownership

### Reference
- [API Reference](./api-reference/) — API reference (one file per controller)
  - [Index](./api-reference/INDEX.md) — Endpoint summary table
- [API Usage Examples](./api-usage-examples.md) — Example requests/responses for API usage
- [Troubleshooting](./troubleshooting.md) — Common issues and solutions
- [FAQ](./faq.md) — Frequently asked questions
- [Ambiguities and Open Questions](./ambiguities-and-open-questions.md) — Unresolved items, TODOs, known gaps

### Behaviour
- [Browse and Search](./behaviour/browse-and-search.md) — User-facing browse and search behaviours
- [Inspect, Edit, Delete](./behaviour/inspect-edit-delete.md) — User-facing inspect, edit, delete behaviours

## Navigation Aids

- **Start here** (newcomers): [Quick Start](./quick-start.md) → [Repository Overview](./repository-overview.md) → [Architecture](./architecture.md)
- **Deep dive** (component owners): [Components](./components/) → [Flows](./flows/) → [Data Model](./data-model.md)
- **Flows** (debuggers): [Flows](./flows/) → [Error Handling](./error-handling.md) → [Logging](./logging.md)

## Maintenance Metadata

- Last reviewed: 2026-11-02
- Owner: Repository maintainers
- Coverage status: Initial generation from repository evidence