# Security Policy

This document describes the security policy for Bill Relay, a self-hosted .NET 10 worker service that monitors an IMAP mailbox for utility bills and forwards validated PDF attachments through the NuciNotifications API. It defines how to report suspected vulnerabilities, which report categories are in scope, and which versions receive security maintenance.

## 📑 Table of Contents

- [Supported Versions](#supported-versions)
- [Reporting a Vulnerability](#reporting-a-vulnerability)
- [Scope](#scope)
- [Disclosure Policy](#disclosure-policy)
- [Safe Harbour](#safe-harbour)
- [Recognition](#recognition)

## 🛡️ Supported Versions

Use this table to indicate which project versions currently receive security maintenance.

| Version | Distribution Channel | Supported |
|---------|--------------------|-----------|
| Latest version | GitHub Releases | ✅ |
| Latest version | Source builds from the `master` branch | ✅ |
| Latest version | Unofficial third-party distribution channels | ❌ |
| Preceding versions | Any distribution channel | ❌ |

## 🚨 Reporting a Vulnerability

Please do not disclose suspected vulnerabilities publicly before maintainers have had an opportunity to validate and remediate them.

To report a vulnerability:
- [GitHub Security Advisories](https://github.com/hmlendea/bill-relay/security/advisories)
- Contact the maintainers directly

## 📌 Scope

The original maintainers are not responsible for changes, modifications, or security issues introduced by downstream forks.

The subsequent report categories are in scope for this repository:
- IMAP credential handling, mailbox polling, and message fetching in the BillRelay worker
- Validation and processing of bill PDF attachments, including PDFs downloaded from provider links
- Communication with the NuciNotifications API, including API key and HMAC shared secret handling
- File-based state persistence, checkpoint files, and the local download directory
- Configuration handling through `appsettings.json` and environment variables

The subsequent categories are out of scope unless explicitly stated to the contrary:
- Changes, modifications, or security issues introduced by downstream forks
- Vulnerabilities in the IMAP server, NuciNotifications API, or SMTP infrastructure operated by third parties
- Vulnerabilities in third-party dependencies, which should be reported to their upstream maintainers

## 🔍 Automated Security Scanning

This repository uses GitHub Actions for continuous security scanning:

- **CodeQL Static Analysis** — Runs on every push/PR to `master` and weekly (Sunday 00:00 UTC) to detect code vulnerabilities in C# code
- **Vulnerable Package Detection** — `dotnet list package --vulnerable --include-transitive` runs on every push/PR to `master` and weekly to identify vulnerable NuGet dependencies (including transitive dependencies)

The security scan workflow is defined in `.github/workflows/security.yml`. Results are published to GitHub Code Scanning and can be viewed in the **Security** tab of this repository.

## 📢 Disclosure Policy

This project follows coordinated disclosure:
1. Vulnerabilities are investigated privately.
2. A remediation plan is prepared and validated.
3. Public disclosure is published after a fix, mitigation, or agreed risk decision is available.
4. Credit is attributed in accordance with reporter preference and project policy.

## 🧾 Safe Harbour

If your research is conducted in good faith, confined to authorised scope, and disclosed responsibly, the maintainers will not pursue action for policy-compliant activity.

## 🙏 Recognition

We appreciate responsible disclosure. Reporters who desire public attribution may be acknowledged in release notes, advisories, or a dedicated acknowledgements section.
