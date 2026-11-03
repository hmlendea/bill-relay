# Browse and Search

## Not Applicable

Bill Relay is a **headless worker service** with no user interface, no web dashboard, and no interactive browsing capabilities.

## What You Cannot Do

| Action | Available |
|--------|-----------|
| Browse processed bills in UI | ❌ |
| Search emails by sender/subject | ❌ |
| Filter by date range in UI | ❌ |
| View attachment previews | ❌ |
| Paginate results | ❌ |

## How to "Browse" Data

### Via File System (PDFs)
```bash
# List all saved bills
ls -la /opt/billrelay/bills/

# Count
ls /opt/billrelay/bills/ | wc -l

# Recent
ls -lt /opt/billrelay/bills/ | head -20
```

### Via State File (Metadata)
```bash
# View all processed attachments
cat /opt/billrelay/state/billrelay.json | jq '.ProcessedAttachments[] | {AttachmentId, FileName, ProviderName, ProcessedDate, FileSizeBytes}'

# Filter by provider
cat /opt/billrelay/state/billrelay.json | jq '.ProcessedAttachments[] | select(.ProviderName=="Vodafone")'

# Recent deliveries
cat /opt/billrelay/state/billrelay.json | jq '.ProcessedAttachments[] | select(.AttachmentId | startswith("delivery:")) | {AttachmentId, ProcessedDate}'
```

### Via Logs (Activity)
```bash
# Recent processing activity
journalctl -u billrelay --since "1 hour ago" | grep ProcessMessage

# All Vodafone bills
journalctl -u billrelay --since "1 day ago" | grep "ProviderName=Vodafone"

# Failed deliveries
journalctl -u billrelay --since "1 day ago" | grep "SendEmail.*Failure"
```

## Programmatic Access

If you need to build a UI or reporting tool:

1. **Read state file** → `ProcessedAttachmentDataObject[]`
2. **Read PDF directory** → File listing
3. **Correlate** via `AttachmentId` (SHA256 hash)

## Related Documentation

- [State and Persistence](../state-and-persistence.md) — Data formats
- [Logging](../logging.md) — Log structure
- [Behaviour Index](./INDEX.md)