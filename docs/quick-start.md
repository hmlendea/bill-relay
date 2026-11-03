# Quick Start

## Prerequisites

- .NET 10 SDK
- IMAP email account (with App Password)
- NuciNotifications API endpoint (HTTPS, API key, HMAC secret)
- Linux/Windows/macOS host

---

## 1. Clone and Build

```bash
git clone <repository-url>
cd bill-relay
dotnet build BillRelay.slnx -c Release
```

---

## 2. Configure

Copy example config and edit:

```bash
cp BillRelay/appsettings.json BillRelay/appsettings.Production.json
# Edit BillRelay/appsettings.Production.json with your values
```

**Minimum required settings:**

```json
{
  "Imap": {
    "Server": "imap.example.com",
    "Port": 993,
    "Username": "bills@example.com",
    "Password": "REPLACE_WITH_APP_PASSWORD",
    "UseSsl": true
  },
  "NuciNotificationsSettings": {
    "BaseUrl": "https://api.notifications.example.com",
    "ApiKey": "REPLACE_WITH_API_KEY",
    "HmacSharedSecretKey": "REPLACE_WITH_HMAC_SECRET"
  },
  "Delivery": {
    "Recipient": "recipient@example.com"
  },
  "Storage": {
    "DownloadDirectory": "./bills",
    "StateFilePath": "./state/billrelay.json"
  }
}
```

**Use environment variables for secrets (recommended):**

```bash
export BILLRELAY__IMAP__PASSWORD="your-app-password"
export BILLRELAY__NUCINOTIFICATIONSSETTINGS__APIKEY="your-api-key"
export BILLRELAY__NUCINOTIFICATIONSSETTINGS__HMACSHAREDSECRETKEY="your-hmac-secret"
export BILLRELAY__DELIVERY__RECIPIENT="recipient@example.com"
export BILLRELAY__STORAGE__DOWNLOADDIRECTORY="/var/lib/billrelay/bills"
export BILLRELAY__STORAGE__STATEFILEPATH="/var/lib/billrelay/state/billrelay.json"
```

---

## 3. Create Directories

```bash
mkdir -p /var/lib/billrelay/bills
mkdir -p /var/lib/billrelay/state
chown -R billrelay:billrelay /var/lib/billrelay
```

---

## 4. Run

**Development:**
```bash
dotnet run --project BillRelay/BillRelay.csproj
```

**Production (published):**
```bash
dotnet publish BillRelay/BillRelay.csproj -c Release -o ./publish
cd ./publish
./BillRelay
```

---

## 5. Verify

Check logs for successful cycle:
```
info: BillRelay.Worker[0] PollInbox Success MessageCount=3 DurationMs=1250
info: BillRelay.Worker[0] ProcessMessage Success MessageUid=42 ProviderName=Vodafone AttachmentCount=1
info: BillRelay.Worker[0] SaveAttachment Success FilePath=/var/lib/billrelay/bills/a1b2c3.pdf AttachmentId=sha256:a1b2c3...
info: BillRelay.Worker[0] SendEmail Success Recipient=recipient@example.com AttachmentId=sha256:a1b2c3...
info: BillRelay.Worker[0] SaveCheckpoint Success LastProcessedUid=42 DurationMs=15
```

---

## 6. Deploy as Service (Linux systemd)

```bash
# Create service file
sudo tee /etc/systemd/system/billrelay.service > /dev/null <<'EOF'
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
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ReadWritePaths=/opt/billrelay/bills /opt/billrelay/state /opt/billrelay/logfile.log

[Install]
WantedBy=multi-user.target
EOF

# Create environment file
sudo tee /opt/billrelay/billrelay.env > /dev/null <<'EOF'
BILLRELAY__IMAP__SERVER=imap.example.com
BILLRELAY__IMAP__USERNAME=bills@example.com
BILLRELAY__IMAP__PASSWORD=your-app-password
BILLRELAY__NUCINOTIFICATIONSSETTINGS__BASEURL=https://api.notifications.example.com
BILLRELAY__NUCINOTIFICATIONSSETTINGS__APIKEY=your-api-key
BILLRELAY__NUCINOTIFICATIONSSETTINGS__HMACSHAREDSECRETKEY=your-hmac-secret
BILLRELAY__DELIVERY__RECIPIENT=recipient@example.com
BILLRELAY__STORAGE__DOWNLOADDIRECTORY=/opt/billrelay/bills
BILLRELAY__STORAGE__STATEFILEPATH=/opt/billrelay/state/billrelay.json
EOF

# Secure and start
sudo chmod 600 /opt/billrelay/billrelay.env
sudo chown billrelay:billrelay /opt/billrelay/billrelay.env
sudo systemctl daemon-reload
sudo systemctl enable --now billrelay
sudo journalctl -u billrelay -f
```

---

## 7. Test with Example Emails (Optional)

```bash
# Save .eml files to a directory
mkdir -p /tmp/test-emails
# ... copy .eml files ...

# Run integration tests
BILLRELAY_EXAMPLE_DIRECTORY=/tmp/test-emails dotnet test BillRelay.IntegrationTests/
```

---

## Next Steps

- [Configuration Reference](./configuration.md) — All settings
- [Troubleshooting](./troubleshooting.md) — Common issues
- [Build and Deployment](./build-and-deployment.md) — Production deployment
- [Monitoring](./troubleshooting.md#log-analysis) — Log analysis