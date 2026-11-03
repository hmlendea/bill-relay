# Behaviour Index

## Overview

Bill Relay is a **headless worker service** with no user interface, no interactive commands, and no browsing/editing capabilities. This section documents the runtime behaviour for operators and developers.

## Applicable Behaviours

| Behaviour | Applicable | Documentation |
|-----------|------------|---------------|
| Browse and Search | ❌ No UI | [browse-and-search.md](./browse-and-search.md) |
| Inspect, Edit, Delete | ❌ No UI | [inspect-edit-delete.md](./inspect-edit-delete.md) |
| Log Inspection | ✅ Via journalctl | [logging.md](../logging.md) |
| State Inspection | ✅ Via file system | [state-and-persistence.md](../state-and-persistence.md) |
| Configuration | ✅ Via appsettings.json/env | [configuration.md](../configuration.md) |

## Runtime Behaviour Summary

### Startup
1. Load configuration
2. Validate required settings
3. Initialize state store
4. Connect to IMAP
5. Begin polling loop

### Steady State
- Poll IMAP every `PollingIntervalMinutes` (default 5)
- Process unseen messages sequentially
- Save attachments, send emails
- Update checkpoint
- Apply retention

### Shutdown
- Complete current message
- Save checkpoint
- Exit gracefully

## Operator Interactions

| Action | Method |
|--------|--------|
| View logs | `journalctl -u billrelay -f` |
| Check status | `systemctl status billrelay` |
| Restart | `systemctl restart billrelay` |
| View state | `cat /opt/billrelay/state/billrelay.json \| jq` |
| View PDFs | `ls -la /opt/billrelay/bills/` |
| Update config | Edit env file → `systemctl restart billrelay` |

## Related Documentation

- [Logging](../logging.md) — Log structure
- [State and Persistence](../state-and-persistence.md) — State file format
- [Configuration](../configuration.md) — Settings
- [Troubleshooting](../troubleshooting.md) — Common issues
- [Quick Start](../quick-start.md) — Deployment