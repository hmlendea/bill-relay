# Inspect, Edit, Delete

## Not Applicable

Bill Relay is a **headless worker service** with no user interface and no interactive editing capabilities.

## What You Cannot Do

| Action | Available |
|--------|-----------|
| Edit bill metadata in UI | ❌ |
| Delete bills via UI | ❌ |
| Modify email template in UI | ❌ |
| Change configuration in UI | ❌ |
| Reprocess specific email in UI | ❌ |

## How to Inspect Data

### State File (Read-Only)
```bash
# Pretty print
cat /opt/billrelay/state/billrelay.json | jq

# Specific attachment
cat /opt/billrelay/state/billrelay.json | jq '.ProcessedAttachments[] | select(.AttachmentId=="sha256:abc123...")'

# Checkpoint
cat /opt/billrelay/state/billrelay.json.checkpoint | jq
```

### PDF Files (Read-Only)
```bash
# View PDF info
file /opt/billrelay/bills/*.pdf

# Check hash matches filename
sha256sum /opt/billrelay/bills/abc123.pdf
# Should match: abc123...  /opt/billrelay/bills/abc123.pdf
```

### Logs (Read-Only)
```bash
# Follow live
journalctl -u billrelay -f

# Search
journalctl -u billrelay -g "Vodafone" --since "1 day ago"
```

## How to "Edit" (Manual Operations)

### Reprocess an Email
```bash
# 1. Stop service
systemctl stop billrelay

# 2. Remove from state (both content and delivery records)
jq 'del(.ProcessedAttachments[] | select(.AttachmentId=="sha256:abc123..."))' \
   /opt/billrelay/state/billrelay.json > /tmp/state.json
mv /tmp/state.json /opt/billrelay/state/billrelay.json

# 3. Optionally delete PDF
rm /opt/billrelay/bills/abc123.pdf

# 4. Start service
systemctl start billrelay
```

### Change Configuration
```bash
# Edit environment file
sudo vim /opt/billrelay/billrelay.env

# Restart to apply
sudo systemctl restart billrelay
```

### Update Email Template
```bash
# Edit template
sudo vim /opt/billrelay/email-template.xml

# Restart (template loaded at startup)
sudo systemctl restart billrelay
```

## How to "Delete" (Manual Operations)

### Delete Single Bill
```bash
# 1. Stop service
systemctl stop billrelay

# 2. Remove state entries
ATTACHMENT_ID="sha256:abc123..."
jq --arg id "$ATTACHMENT_ID" 'del(.ProcessedAttachments[] | select(.AttachmentId==$id or .AttachmentId=="delivery:"+$id))' \
   /opt/billrelay/state/billrelay.json > /tmp/state.json
mv /tmp/state.json /opt/billrelay/state/billrelay.json

# 3. Delete PDF
rm -f /opt/billrelay/bills/abc123.pdf

# 4. Start service
systemctl start billrelay
```

### Delete All State (Full Reset)
```bash
# 1. Stop service
systemctl stop billrelay

# 2. Backup (optional)
cp /opt/billrelay/state/billrelay.json /backup/billrelay.json.$(date +%s)
cp /opt/billrelay/state/billrelay.json.checkpoint /backup/billrelay.json.checkpoint.$(date +%s)

# 3. Remove state files
rm /opt/billrelay/state/billrelay.json
rm /opt/billrelay/state/billrelay.json.checkpoint

# 4. Remove all PDFs
rm /opt/billrelay/bills/*.pdf

# 5. Start service (fresh state, will reprocess from InitialProcessingDate)
systemctl start billrelay
```

### Delete Old Bills (Manual Retention)
```bash
# Delete PDFs older than 90 days
find /opt/billrelay/bills -name "*.pdf" -mtime +90 -delete

# Note: State file entries not cleaned automatically by this command
# Run retention via service or manually prune state file
```

## Safety Warnings

| Operation | Risk |
|-----------|------|
| Editing state file manually | JSON corruption → startup failure |
| Deleting checkpoint | Full mailbox rescan (bounded by age filter) |
| Deleting PDFs without state cleanup | Orphan state entries, wasted space |
| Editing template while running | Not loaded until restart |

**Always:** Stop service before manual state/file modifications.

## Related Documentation

- [State and Persistence](../state-and-persistence.md) — File formats
- [Troubleshooting](../troubleshooting.md#state-file-corruption) — Recovery
- [Change Guide](../change-guide.md#rollback-procedures) — Rollback
- [Behaviour Index](./INDEX.md)