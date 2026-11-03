# Presentation

## Overview

The presentation layer is the **entry point** and **host** of the application. Bill Relay is a console/daemon application with no HTTP API, no UI, and no user-facing endpoints.

## Entry Point

**File:** `BillRelay/Program.cs`

```csharp
public static async Task Main(string[] args)
{
    var builder = Host.CreateApplicationBuilder(args);

    // Configuration, validation, DI, logging setup
    // ... (see host-and-composition.md)

    var host = builder.Build();
    await host.RunAsync();  // Blocks until shutdown
}
```

## Host Model

- **Type:** .NET Generic Host (`Microsoft.Extensions.Hosting`)
- **Lifetime:** `IHostApplicationLifetime` (Ctrl+C, SIGTERM, systemd stop)
- **Services:** All registered as singletons
- **Background Service:** `BillRelayWorker` (single `ExecuteAsync` loop)

## No HTTP Endpoints

| Feature | Status |
|---------|--------|
| REST API | ❌ Not implemented |
| Health endpoint | ❌ Not implemented |
| Metrics endpoint | ❌ Not implemented |
| Web UI | ❌ Not implemented |
| gRPC | ❌ Not implemented |

**Rationale:** Worker service pattern — polling IMAP, no inbound requests needed.

## Command Line Arguments

| Argument | Purpose |
|----------|---------|
| `--config <path>` | Alternative appsettings.json (not implemented) |
| `--help` | Show help (not implemented) |

**Current:** Only standard .NET host args (environment, content root).

## Output

### Console (Standard Out/Error)
- Structured logs via NuciLog → Console formatter
- Captured by systemd journal / Docker logs
- No separate log file by default

### Log Format Example
```
info: BillRelay.Worker[0] PollInbox Success MessageCount=3 DurationMs=1250
info: BillRelay.Worker[0] ProcessMessage Success MessageUid=42 ProviderName=Vodafone AttachmentCount=1
```

## Shutdown

### Graceful Shutdown Sequence
1. SIGTERM / Ctrl+C received
2. `IHostApplicationLifetime.StopApplication()` called
3. `BillRelayWorker.StopAsync` triggered
4. Current `ProcessInboxAsync` completes (checks cancellation at cycle boundary)
5. `ExecuteAsync` returns
6. Host disposes services
7. Process exits

### No Mid-Cycle Cancellation
- Current message processing completes
- Checkpoint saved after current message
- Next cycle not started

## Deployment Modes

| Mode | Command | Use Case |
|------|---------|----------|
| Development | `dotnet run --project BillRelay` | Local debugging |
| Published | `./BillRelay` | Production (framework-dependent) |
| Self-contained | `./BillRelay` | Production (no .NET runtime needed) |
| Docker | `docker run billrelay` | Containerized |

## Process Management

### systemd (Linux)
```ini
Type=notify
ExecStart=/opt/billrelay/BillRelay
Restart=always
```

### Windows Service
```powershell
New-Service -Name BillRelay -BinaryPathName "C:\BillRelay\BillRelay.exe"
```

### Docker
```dockerfile
ENTRYPOINT ["dotnet", "BillRelay.dll"]
```

## Monitoring Interface

**No programmatic interface.** Monitor via:
- Process existence: `systemctl is-active billrelay`
- Logs: `journalctl -u billrelay -f`
- File system: State file timestamps, PDF counts
- Disk space: `df -h /opt/billrelay`

## Future: Health Endpoint (Optional)

If needed, add minimal HTTP server:

```csharp
// In Program.cs
if (settings.EnableHealthEndpoint)
{
    services.AddHostedService<HealthEndpointHost>();
    // Maps GET /health → 200 OK
}
```

**Not in current scope.**

## Related Documentation

- [Host and Composition](../components/host-and-composition.md) — DI and startup
- [BillRelayWorker](../components/bill-relay-worker.md) — Background service
- [Build and Deployment](../build-and-deployment.md) — Deployment modes
- [Architecture: Presentation Layer](../architecture.md#presentation-layer)