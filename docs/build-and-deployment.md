# Build and Deployment

## Build Pipeline

### Local Build

```bash
# Restore dependencies
dotnet restore BillRelay.slnx

# Build (Debug)
dotnet build BillRelay.slnx

# Build (Release)
dotnet build BillRelay.slnx -c Release

# Publish (self-contained, Linux x64)
dotnet publish BillRelay/BillRelay.csproj -c Release -r linux-x64 --self-contained true

# Publish (framework-dependent)
dotnet publish BillRelay/BillRelay.csproj -c Release
```

### Solution Structure

```
BillRelay.slnx
├── BillRelay/                    # Executable (net10.0)
├── BillRelay.UnitTests/          # NUnit test project
├── BillRelay.IntegrationTests/   # NUnit test project
├── BillRelay.ArchitectureTests/  # NUnit + ArchUnit test project
```

### Build Output

| Configuration | Output Directory |
|---------------|------------------|
| Debug | `BillRelay/bin/Debug/net10.0/` |
| Release | `BillRelay/bin/Release/net10.0/` |

**Key Files in Output:**
- `BillRelay` / `BillRelay.exe` — Executable
- `BillRelay.dll` — Main assembly
- `appsettings.json` — Copied from project (Content)
- `email-template.xml` — Copied from root (Content)
- `*.deps.json` — Dependency manifest
- `*.runtimeconfig.json` — Runtime config

## CI Pipeline (GitHub Actions)

**Workflow:** `.github/workflows/dotnet.yml`

```yaml
name: .NET
on: [push, pull_request]
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - name: Restore
        run: dotnet restore BillRelay.slnx
      - name: Build
        run: dotnet build BillRelay.slnx --no-restore
      - name: Test
        run: dotnet test BillRelay.slnx --no-restore --verbosity normal
```

**Triggers:** Push/PR to any branch
**Runs:** Restore → Build → Test
**Artifacts:** None published (add for releases)

## Deployment

### Deployment Model

**Self-hosted worker process** — No container orchestration, no Kubernetes, no cloud functions.

**Deployment Artifact:** Published output directory (framework-dependent or self-contained)

### Prerequisites

| Requirement | Details |
|-------------|---------|
| .NET Runtime | .NET 10 (if framework-dependent) |
| OS | Linux, Windows, macOS (tested on Linux) |
| Network | Outbound to IMAP server (993/TCP) and NuciNotifications API (443/TCP) |
| Filesystem | Write access to `DownloadDirectory` and `StateFilePath` directory |
| Permissions | Dedicated service user recommended |

### Configuration Deployment

**Files to Deploy:**
1. Published executable + dependencies
2. `appsettings.json` (with production values OR placeholders)
3. `email-template.xml`

**Secrets Management (Required):**
```bash
# Environment variables (recommended)
export BILLRELAY__IMAP__PASSWORD="app-password"
export BILLRELAY__NUCINOTIFICATIONSSETTINGS__APIKEY="api-key"
export BILLRELAY__NUCINOTIFICATIONSSETTINGS__HMACSHAREDSECRETKEY="hmac-secret"
export BILLRELAY__DELIVERY__RECIPIENT="user@example.com"
export BILLRELAY__STORAGE__DOWNLOADDIRECTORY="/var/lib/billrelay/bills"
export BILLRELAY__STORAGE__STATEFILEPATH="/var/lib/billrelay/state/billrelay.json"
```

**Alternative:** Edit deployed `appsettings.json` directly (not recommended for secrets)

### Systemd Service (Linux)

```ini
# /etc/systemd/system/billrelay.service
[Unit]
Description=Bill Relay Worker
After=network-online.target
Wants=network-online.target

[Service]
Type=notify
User=billrelay
Group=billrelay
WorkingDirectory=/opt/billrelay
ExecStart=/opt/billrelay/BillRelay
Restart=always
RestartSec=10
EnvironmentFile=/opt/billrelay/billrelay.env
StandardOutput=journal
StandardError=journal

# Security hardening
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ReadWritePaths=/opt/billrelay/bills /opt/billrelay/state /opt/billrelay/logfile.log

[Install]
WantedBy=multi-user.target
```

**Environment File (`/opt/billrelay/billrelay.env`):**
```bash
BILLRELAY__IMAP__SERVER=imap.example.com
BILLRELAY__IMAP__USERNAME=user@example.com
BILLRELAY__IMAP__PASSWORD=app-password
BILLRELAY__NUCINOTIFICATIONSSETTINGS__BASEURL=https://api.notifications.example.com
BILLRELAY__NUCINOTIFICATIONSSETTINGS__APIKEY=api-key
BILLRELAY__NUCINOTIFICATIONSSETTINGS__HMACSHAREDSECRETKEY=hmac-secret
BILLRELAY__DELIVERY__RECIPIENT=recipient@example.com
BILLRELAY__STORAGE__DOWNLOADDIRECTORY=/opt/billrelay/bills
BILLRELAY__STORAGE__STATEFILEPATH=/opt/billrelay/state/billrelay.json
```

**Enable and Start:**
```bash
systemctl daemon-reload
systemctl enable --now billrelay
systemctl status billrelay
journalctl -u billrelay -f
```

### Windows Service

```powershell
# Create service (run as Administrator)
New-Service -Name "BillRelay" -BinaryPathName "C:\BillRelay\BillRelay.exe" -Description "Bill Relay Worker" -StartupType Automatic

# Set environment variables (registry or .env file)
# Use NSSM (Non-Sucking Service Manager) for better management
```

### Docker (Optional)

```dockerfile
# Dockerfile
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
WORKDIR /app
RUN useradd -r -s /bin/false billrelay

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["BillRelay.slnx", "."]
COPY ["BillRelay/", "BillRelay/"]
RUN dotnet restore "BillRelay.slnx"
RUN dotnet publish "BillRelay/BillRelay.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
USER billrelay
ENTRYPOINT ["dotnet", "BillRelay.dll"]
```

```bash
# Build
docker build -t billrelay:latest .

# Run
docker run -d \
  --name billrelay \
  --restart unless-stopped \
  -v /host/bills:/app/bills \
  -v /host/state:/app/state \
  -v /host/logs:/app/logs \
  --env-file .env \
  billrelay:latest
```

### File System Layout (Production)

```
/opt/billrelay/
├── BillRelay                    # Executable
├── BillRelay.dll                # Assembly
├── *.dll                        # Dependencies
├── appsettings.json             # Config (placeholders)
├── email-template.xml           # Template
├── billrelay.env                # Secrets (600, owned by billrelay)
├── bills/                       # DownloadDirectory (700)
│   ├── Vodafone_VDF123.pdf
│   └── ...
├── state/                       # StateFilePath directory (700)
│   ├── billrelay.json           # Processed attachments
│   └── billrelay.json.checkpoint # IMAP checkpoint
└── logfile.log                  # Logs (600)
```

### Backup Strategy

**Critical Data:**
- `state/billrelay.json` — Delivery records (deduplication)
- `state/billrelay.json.checkpoint` — IMAP position
- `bills/` — PDF attachments (can be re-downloaded for E.ON)

**Backup Frequency:** Daily (cron/systemd timer)
**Retention:** 30 days minimum

```bash
# Backup script
#!/bin/bash
BACKUP_DIR="/backup/billrelay/$(date +%Y%m%d)"
mkdir -p "$BACKUP_DIR"
cp -r /opt/billrelay/state "$BACKUP_DIR/"
cp -r /opt/billrelay/bills "$BACKUP_DIR/"
tar -czf "/backup/billrelay-$(date +%Y%m%d).tar.gz" -C /opt/billrelay state bills
```

### Health Checks

**No built-in health endpoint.** Monitor via:
- Systemd service status: `systemctl is-active billrelay`
- Log file: `tail -f /opt/billrelay/logfile.log`
- Process: `pgrep -f BillRelay`
- File system: Disk space on `/opt/billrelay`

**Recommended:** Add `/health` endpoint or use `systemd` watchdog.

### Updates

**Zero-downtime not supported** — Single process.

**Update Procedure:**
```bash
# 1. Stop service
systemctl stop billrelay

# 2. Backup state
cp -r /opt/billrelay/state /backup/billrelay-state-$(date +%s)

# 3. Deploy new version
rsync -av --exclude='state' --exclude='bills' --exclude='logfile.log' --exclude='billrelay.env' ./publish/ /opt/billrelay/

# 4. Start service
systemctl start billrelay

# 5. Verify logs
journalctl -u billrelay -f
```

### Rollback

```bash
# 1. Stop service
systemctl stop billrelay

# 2. Restore previous binary (keep state!)
rsync -av --exclude='state' --exclude='bills' --exclude='logfile.log' /backup/billrelay-v1.2.3/ /opt/billrelay/

# 3. Start service
systemctl start billrelay
```

## Environments

| Environment | Purpose | Config Source |
|-------------|---------|---------------|
| Development | Local testing | `appsettings.json` + user secrets |
| Staging | Pre-production validation | Environment variables |
| Production | Live bill processing | Environment variables + secret manager |

## Related Documentation

- [Configuration](./configuration.md) — Settings and environment variables
- [Security](./security.md) — Deployment security checklist
- [Architecture: Deployment and Operations](../architecture.md#deployment-and-operations)
- [Repository Structure](./repository-structure.md) — Build artifacts