# EmailAgePolicy Component

## Overview

**Location:** `BillRelay/Services/EmailAgePolicy.cs`

**Type:** Static class with pure functions

**Purpose:** Calculates the effective cutoff date for email processing based on configuration and checkpoint state.

## Responsibilities

1. **Cutoff Calculation** — Determine earliest eligible email date
2. **Eligibility Check** — Test if a message date falls within the processing window

## Key Methods

### GetCutoff (Primary)

```csharp
public static DateTimeOffset GetCutoff(
    PollingSettings settings,
    DateTimeOffset currentTime,
    DateTimeOffset? lastProcessedDate)
```

**Algorithm:**
1. Base cutoff = `currentTime - MaximumEmailAgeDays`
2. If `InitialProcessingDate` configured and > base cutoff → use it
3. If `lastProcessedDate` (from checkpoint) > current cutoff → use it
4. Return effective cutoff

**Priority Order (highest wins):**
1. `lastProcessedDate` (checkpoint) — Ensures no gaps after restart
2. `InitialProcessingDate` (config) — Explicit lower bound for first run
3. `currentTime - MaximumEmailAgeDays` — Rolling window

### GetCutoff (Overload)

```csharp
public static DateTimeOffset GetCutoff(PollingSettings settings, DateTimeOffset currentTime)
```
Calls primary with `lastProcessedDate = null`.

### IsEligible

```csharp
public static bool IsEligible(DateTimeOffset? arrivalTime, DateTimeOffset cutoff)
```
Returns true if `arrivalTime >= cutoff`.

## Configuration

Controlled via `PollingSettings`:

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `MaximumEmailAgeDays` | `int` | `30` | Rolling window (must be > 0) |
| `InitialProcessingDate` | `DateTimeOffset?` | `null` | Explicit lower bound |

## Usage in Worker

```csharp
// In BillRelayWorker.ProcessMailboxAsync
var checkpoint = await _stateStore.GetCheckpointAsync();
var cutoff = EmailAgePolicy.GetCutoff(
    _pollingSettings,
    DateTimeOffset.UtcNow,
    checkpoint.LastProcessedDate);

var query = SearchQuery.DeliveredAfter(cutoff);
var uids = await mailbox.SearchAsync(query, cancellationToken);

// Then filter by exact InternalDate:
foreach (var uid in uids.OrderBy(u => u.Id))
{
    var message = await mailbox.GetMessageAsync(uid, cancellationToken);
    if (!EmailAgePolicy.IsEligible(message.Date, cutoff)) continue;
    // ... process
}
```

## Two-Stage Filtering

1. **Server-side (IMAP SEARCH)** — `DeliveredAfter(cutoff)` coarse filter
2. **Client-side (exact)** — `IsEligible(message.Date, cutoff)` precise filter

This handles IMAP server timezone/precision differences.

## Validation

- `MaximumEmailAgeDays` must be > 0 (throws `ArgumentOutOfRangeException`)
- `InitialProcessingDate` can be null (ignored)
- `lastProcessedDate` can be null (ignored)

## Testing

**Unit Tests:** `BillRelay.UnitTests.Services.EmailAgePolicyTests`
- Base cutoff calculation
- InitialProcessingDate precedence
- Checkpoint date precedence
- Eligibility boundary conditions

## Extension Points

- Add timezone-aware cutoff (currently UTC only)
- Add upper-bound cutoff (future-dated messages)
- Add per-provider age policies