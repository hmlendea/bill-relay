# Privacy and Personal Data

Bill Relay is a self-hosted .NET 10 worker service that monitors an IMAP inbox for supported utility bills, validates their PDF attachments, saves them locally, and forwards them through the NuciNotifications API. This document describes how the application handles personal data when deployed and operated by an instance operator.

## 📑 Table of Contents

- [What This Document Covers](#what-this-document-covers)
- [Self-Hosted Deployments](#self-hosted-deployments)
- [Data We Handle](#data-we-handle)
  - [Data Provided to the Application](#data-provided-to-the-application)
  - [Data Generated or Collected by the Application](#data-generated-or-collected-by-the-application)
  - [Data Received from Integrations](#data-received-from-integrations)
- [Processing and Use](#processing-and-use)
- [Storage, Retention, and Deletion](#storage-retention-and-deletion)
- [External Processing and Integrations](#external-processing-and-integrations)
- [International Transfers](#international-transfers)
- [Data Protection and Security](#data-protection-and-security)
- [Document Changes](#document-changes)
- [Contact](#contact)

## 🔎 What This Document Covers

This document describes how Bill Relay at https://github.com/hmlendea/bill-relay handles personal data. It covers the application behaviour and verified integrations described below. Where the software is self-hosted, the instance operator may have separate responsibilities described below.

## 🏠 Self-Hosted Deployments

Bill Relay is designed for self-hosted deployment. The instance operator controls the application's configuration, local storage, logs, backups, access controls, retention, and request handling. The project maintainers do not operate any instance and do not receive data from self-hosted deployments.

No data is sent from a self-hosted instance to project maintainers. The application has no built-in telemetry, update checks, crash reports, or authentication integrations that transmit data to the project. All external communication is configured by the operator and limited to the IMAP server and the NuciNotifications API endpoint specified in configuration.

## 📥 Data We Handle

### Data Provided to the Application

- IMAP credentials (server, port, username, password) configured by the operator in `appsettings.json` or environment variables.
- NuciNotifications API credentials (base URL, API key, HMAC shared secret) configured by the operator.
- Email delivery settings (recipient address, sender name, template path) configured by the operator.
- Filtering criteria (provider names, sender patterns, subject patterns) configured by the operator.

### Data Generated or Collected by the Application

- IMAP checkpoint state (last processed UID, timestamp) persisted to the configured state file.
- Delivery checkpoint state (last successful delivery timestamp) persisted to the configured state file.
- Application logs written to the configured log file path, which may contain email metadata (sender, subject, date, attachment filenames) and processing outcomes.
- Downloaded bill PDF files saved to the configured download directory.

### Data Received from Integrations

- Email messages fetched from the configured IMAP server, including headers, body, and attachments.
- PDF attachments from identified utility bill providers (Vodafone, Electrica Furnizare, E-bloc, Hidroelectrica, E.ON).
- HTTP responses from the NuciNotifications API (success or error status).

## 🧭 Processing and Use

The application processes the data described above for these verified functions:
- IMAP mailbox polling and message retrieval — IMAP credentials, email messages, attachments
- Provider identification and attachment validation — email metadata, attachment content, filename, MIME type, size, PDF signature
- Local attachment storage — validated PDF attachments, download directory path
- Checkpoint persistence — IMAP UID, timestamps, delivery status, state file path
- Bill forwarding via NuciNotifications — validated PDF attachments, recipient, sender name, template, API credentials
- Logging — processing events, errors, email metadata, attachment details, log file path

## 🗄️ Storage, Retention, and Deletion

All data is stored locally on the instance operator's infrastructure:
- **Downloaded bills**: Saved as PDF files in the configured download directory (`./bills` by default). Retention is controlled by the operator; the application does not automatically delete files.
- **Checkpoint state**: Stored in a JSON file at the configured state file path (`./state/billrelay.json` by default). Contains last processed IMAP UID and delivery timestamps. Retained until manually removed or overwritten.
- **Application logs**: Written to the configured log file path (`logfile.log` by default). Log rotation and retention are not managed by the application; the operator is responsible for log management.
- **Configuration**: Stored in `appsettings.json` and environment variables. Contains credentials and settings. The operator controls access and backup.

The project maintainers do not control storage, deletion, or backups for any self-hosted instance.

## 🔗 External Processing and Integrations

| Service or integration | Purpose | Data involved | Configuration or documentation |
|-----------------------|---------|---------------|--------------------------------|
| IMAP server | Fetch email messages and attachments | IMAP credentials, email headers, body, attachments | Configured in `appsettings.json` under `imap` section |
| NuciNotifications API | Forward validated bill PDFs via email | PDF attachments, recipient, sender name, template, API key, HMAC secret | Configured in `appsettings.json` under `nuciNotificationsSettings` section; requires HTTPS |

The application has no other built-in external data transfers.

## 🌍 International Transfers

Data transfers occur only to the IMAP server and NuciNotifications API endpoint configured by the instance operator. The geographic location of these endpoints is determined by the operator's configuration. The project maintainers do not control or influence deployment location or data routing.

## 🛡️ Data Protection and Security

The application implements the following verified safeguards:
- IMAP connections use SSL/TLS with certificate validation enabled by default (`useSsl: true`, `validateCertificates: true`).
- NuciNotifications API communication requires an absolute HTTPS base URL; HTTP is rejected at startup.
- API credentials (API key, HMAC shared secret) are validated for presence at startup.
- Credentials are loaded from configuration files and environment variables; the operator is responsible for securing these sources.
- No secrets are logged; the NuciLogger masks sensitive values where applicable.
- File-based state and downloaded bills are stored in operator-controlled directories; the operator is responsible for filesystem permissions, encryption at rest, and network exposure.

For self-hosted deployments, the instance operator is responsible for:
- Applying operating system and .NET runtime updates.
- Securing configuration files and environment variables containing credentials.
- Configuring filesystem permissions for download and state directories.
- Managing network exposure of the IMAP and NuciNotifications endpoints.
- Protecting log files from unauthorised access.
- Implementing backup and disaster recovery procedures.

The original maintainers are not responsible for changes, modifications, or privacy issues introduced by downstream forks.

## 🔄 Document Changes

Update this document when application data flows, storage, integrations, or deployment responsibilities change. The current version is published at https://github.com/hmlendea/bill-relay/blob/master/PRIVACY.md.

## 📬 Contact

For questions about application data handling, contact the project maintainers via the GitHub repository at https://github.com/hmlendea/bill-relay. For a self-hosted instance, contact the instance operator, unless the project explicitly handles the request. Include the instance identifier or deployment context if applicable; do not send passwords, access tokens, or other secrets.