[![Build Status](https://github.com/hmlendea/bill-relay/actions/workflows/dotnet.yml/badge.svg)](https://github.com/hmlendea/bill-relay/actions/workflows/dotnet.yml)
[![License](https://img.shields.io/github/license/hmlendea/bill-relay)](https://github.com/hmlendea/bill-relay/blob/master/LICENSE)

# Bill Relay

Bill Relay is a .NET 10 worker service that monitors an IMAP inbox for supported utility bills, validates their PDF attachments, saves them locally, and forwards them through the NuciNotifications API.

## 📑 Table of Contents

- [Capabilities](#capabilities)
- [Use Cases](#use-cases)
- [Usage](#usage)
- [Known Limitations](#known-limitations)
- [Installation](#installation)
  - [Installation from Source](#installation-from-source)
  - [Verification](#verification)
- [Configuration](#configuration)
  - [Configuration Files](#configuration-files)
  - [Settings](#settings)
  - [Environment Variables](#environment-variables)
- [Integrations](#integrations)
- [Authentication and Authorisation](#authentication-and-authorisation)
- [Privacy and Data](#privacy-and-data)
- [Development](#development)
  - [Requirements](#requirements)
  - [Setup](#setup)
  - [Build](#build)
  - [Run](#run)
  - [Test](#test)
  - [Dependencies](#dependencies)
- [GitHub Actions](#github-actions)
- [Project Structure](#project-structure)
  - [Projects and Packages](#projects-and-packages)
- [Deployment](#deployment)
- [Contributing](#contributing)
- [Project Engagement](#project-engagement)
- [License](#license)

## ✨ Capabilities

- Monitors an IMAP mailbox for recent messages.
- Identifies Vodafone, Electrica Furnizare, E-bloc, and Hidroelectrica bills.
- Validates provider-specific PDF filenames, MIME type, size, and PDF signature.
- Saves validated attachments to the configured download directory.
- Forwards validated bills through NuciNotifications.
- Persists IMAP and delivery checkpoints to avoid repeated processing after restart.

## 🎯 Use Cases

- **Personal bill forwarding:** Collect utility invoices in one inbox and forward validated PDFs to a configured recipient.
- **Self-hosted automation:** Run the worker as a locally controlled background service with file-based state.

## 🚀 Usage

Configure `BillRelay/appsettings.json`, then run the worker from the repository root:

```bash
dotnet run --project BillRelay
```

The worker polls the configured mailbox, processes messages newer than the effective checkpoint and age cutoff, and forwards matching attachments.

## ⚠️ Known Limitations

- Polling is used instead of IMAP `IDLE`; new messages are detected on the configured interval.
- Only the providers and attachment filename patterns implemented in `BillProviderIdentification` are supported.
- The service relies on the external NuciNotifications API and its configured SMTP delivery path.

## 📦 Installation

### Installation from Source

The repository currently provides source-based installation only:

```bash
dotnet restore BillRelay.slnx
dotnet build BillRelay.slnx
```

### Verification

Run the test suite after installation:

```bash
dotnet test
```

## ⚙️ Configuration

Runtime configuration is loaded from `BillRelay/appsettings.json` in the application output directory. Environment variables are also loaded and can override configuration values.

### Configuration Files

| File | Scope | Purpose |
|------|-------|---------|
| `BillRelay/appsettings.json` | Runtime | IMAP, polling, filters, attachment, storage, delivery, and notification settings. |
| `email-template.xml` | Runtime | Subject and body template for forwarded bill emails. |

### Settings

The subsequent settings are recognised:

| Section | Key | Type | Default | Required | Description |
|---------|-----|------|---------|----------|-------------|
| `delivery` | `recipient` | string | — | Yes | Destination email address. |
| `delivery` | `senderName` | string | `BillRelay` | No | Display name used for forwarded email. |
| `delivery` | `templatePath` | string | `email-template.xml` | Yes | Forwarded-email XML template path. |
| `imap` | `server`, `port`, `mailbox`, `username`, `password` | string/integer | — | Yes | IMAP connection settings. |
| `imap` | `useSsl`, `validateCertificates` | boolean | `true` | No | TLS and certificate-validation settings. |
| `polling` | `intervalSeconds` | integer | `300` | Yes | Mailbox polling interval. |
| `polling` | `maximumEmailAgeDays` | integer | `30` | Yes | Maximum message age to inspect. |
| `polling` | `initialProcessingDate` | date/time or null | `null` | No | Optional lower bound for initial processing. |
| `filters` | `providers` | string array | — | Yes | Enabled bill providers. |
| `attachments` | `permittedMimeTypes` | string array | `application/pdf` | Yes | Allowed attachment MIME types. |
| `attachments` | `maxSizeBytes` | integer | `25000000` | Yes | Maximum decoded attachment size. |
| `storage` | `downloadDirectory`, `stateFilePath` | string | — | Yes | Download and checkpoint locations. |
| `nuciNotificationsSettings` | `baseUrl`, `apiKey`, `hmacSharedSecretKey` | string | — | Yes | NuciNotifications API connection and authentication settings. |

### Environment Variables

Environment variables are loaded by the host configuration system. Use them for deployment secrets instead of committing credentials to `appsettings.json`.

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| Configuration-path environment variables | As applicable | — | Override matching configuration keys using the .NET configuration environment-variable convention. |

## 🔌 Integrations

| Integration | Compatibility | Purpose | Required |
|-------------|---------------|---------|----------|
| IMAP server | IMAP over TLS | Reads messages from the configured mailbox. | Yes |
| NuciNotifications API | `NuciNotifications.Client` 1.2.0 | Sends forwarded emails and attachments. | Yes |
| SMTP server behind NuciNotifications | Configured by the API | Delivers forwarded messages to the recipient. | Yes |

## 🔐 Authentication and Authorisation

Bill Relay authenticates to the IMAP server with the configured mailbox credentials and authenticates to NuciNotifications with an API key and HMAC shared secret. Do not commit these values to the repository.

## 🛡️ Privacy and Data

Bill Relay processes email metadata and invoice attachments from the configured mailbox, stores validated files locally, and transmits forwarded attachments to NuciNotifications for delivery. The service persists checkpoint and delivery state so messages and attachments are not repeatedly processed.

| Data | Purpose | Storage | Retention | Optional |
|------|---------|---------|-----------|----------|
| Email metadata | Provider and bill identification | Process memory and logs | Runtime-dependent | No |
| Invoice PDF attachments | Local saving and forwarding | `storage.downloadDirectory` and NuciNotifications | Controlled by retention settings and external mail systems | No |
| Processing state | Deduplication and restart recovery | `storage.stateFilePath` and its checkpoint file | Controlled by retention settings | No |

## 🛠️ Development

### Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### Setup

Restore the solution dependencies:

```bash
dotnet restore BillRelay.slnx
```

Configure local IMAP and NuciNotifications values without committing secrets.

### Build

```bash
dotnet build BillRelay.slnx
```

### Run

```bash
dotnet run --project BillRelay
```

### Test

```bash
dotnet test
```

The integration test that validates local `.eml` samples can be enabled with:

```bash
BILLRELAY_EXAMPLE_DIRECTORY="/path/to/email-samples" dotnet test BillRelay.IntegrationTests/BillRelay.IntegrationTests.csproj
```

### Dependencies

| Package | Version | Scope | Purpose |
|---------|---------|-------|---------|
| MailKit | 4.12.0 | Runtime | IMAP access. |
| MimeKit | 4.12.0 | Runtime | MIME message and attachment parsing. |
| NuciDAL | 3.2.1 | Runtime | File-backed state persistence. |
| NuciLog and NuciLog.Core | 1.2.1 / 3.1.0 | Runtime | Structured logging. |
| NuciNotifications.Client | 1.2.0 | Runtime | Forwarded email delivery. |

## ⚙️ GitHub Actions

| Workflow | Purpose | What it does |
|----------|---------|--------------|
| [dotnet.yml](https://github.com/hmlendea/bill-relay/blob/master/.github/workflows/dotnet.yml) | Validate the .NET solution | Restores dependencies, builds the solution, and runs tests. |

## 🗂️ Project Structure

The solution contains the executable service and separate unit, integration, and architecture test projects.

### Projects and Packages

| Project | Type | Purpose |
|---------|------|---------|
| `BillRelay` | .NET executable | IMAP polling, bill identification, validation, persistence, and delivery. |
| `BillRelay.UnitTests` | NUnit test project | Unit coverage for processing and configuration behavior. |
| `BillRelay.IntegrationTests` | NUnit test project | Integration coverage for delivery, persistence, and email samples. |
| `BillRelay.ArchitectureTests` | NUnit test project | Dependency and architecture constraints. |

## 🚢 Deployment

Bill Relay is a self-hosted worker. Publish the solution, provide runtime configuration and secrets through the deployment environment, then run the `BillRelay` executable under the host's service manager.

Persistent files include the configured download directory, state file, and IMAP checkpoint file. Back up or protect them according to the operational requirements of the mailbox and invoice data.

## 🤝 Contributing

You are welcome to submit any suggestion, feedback, or modification to this project.

When doing so, please:
- Maintain cross-platform compatibility
- Submit focused pull requests that conform to the existing code style
- Maintain your branch synchronised with `master`
- Revise the documentation when functionality changes
- Properly test all modifications, including edge cases and error conditions
- Include tests for additional or modified functionality
- Raise a new [issue](https://github.com/hmlendea/bill-relay/issues) for problems or suggestions

## 💝 Project Engagement

Discovered a problem or have a suggestion? [Open an issue](https://github.com/hmlendea/bill-relay/issues)!

If you find this project useful, consider starring ⭐️ it on GitHub!

## 📄 License

This project is being distributed under the `GNU General Public License, Version 3 or later`.
See [LICENSE](./LICENSE) for additional information.