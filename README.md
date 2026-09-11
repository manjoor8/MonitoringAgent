# IIS Root-Cause Monitoring Agent

A production-grade, autonomous Windows monitoring service built in C# (.NET 8) designed to identify the root cause of intermittent CPU spikes on Windows IIS web servers hosting multi-tenant websites (~25 sites per server).

---

## Key Features

- **Evidence Collection First**: Continuous rolling baseline buffer (last 60 minutes) promoted immediately upon threshold breach ($\ge 70\%$).
- **5-State Anti-Flapping Engine**: Explicit state machine with hysteresis band ($60\% - 70\%$) and 3-consecutive-sample recovery validation.
- **Dynamic PID $\leftrightarrow$ AppPool $\leftrightarrow$ Website Mapping**: Tracks worker process recycles and preserves historical relationships across PID shifts.
- **Automated Root Cause Correlation**: Multi-dimensional evaluation of CPU consumers, traffic surges, memory growth, GC Gen 2 activity, Windows event logs, and scheduled tasks.
- **Multi-Server Incident Fingerprinting**: Hybrid similarity matcher (cosine trajectory similarity + scalar deltas) to detect recurring incident signatures across multiple servers.
- **Self-Contained Web Dashboard**: Co-hosted on Kestrel (`http://localhost:5050`) with Server-Sent Events (SSE) live streaming and an embedded canvas chart renderer with zero external CDN dependencies.
- **Automated Recovery Safeguards**: Optional, disabled-by-default auto-recycle that targets *only* the culprit Application Pool after sustained critical CPU ($\ge 90\%$ for $\ge 5$ min), with mandatory pre-restart dump capture and strict restart limits.

---

## Solution Structure

```
├── src/
│   ├── IISMonitor.Core/           # Domain models, enums, interfaces, state machine, circular buffer
│   ├── IISMonitor.Infrastructure/ # Perf counters, MWA IIS APIs, ProcDump, task scheduler, auto-recovery
│   ├── IISMonitor.Data/           # EF Core SQLite DbContext (WAL mode), batch writer channel, retention
│   ├── IISMonitor.Web/            # ASP.NET Core minimal APIs, SSE live feed, static SPA dashboard
│   └── IISMonitor.Service/        # Windows Service host, background workers, composition root
├── tests/
│   ├── IISMonitor.UnitTests/      # 30 unit tests covering core logic, state machine, auto-recovery
│   └── IISMonitor.IntegrationTests/ # End-to-end incident lifecycle test with SQLite in-memory engine
├── scripts/
│   ├── Install-IISMonitor.ps1     # Automated PowerShell installer
│   └── Uninstall-IISMonitor.ps1   # Clean uninstallation script
└── docs/
    ├── Architecture.md            # Comprehensive architectural design & data flow
    ├── Installation.md            # Prerequisites, installation, and configuration reference
    ├── Troubleshooting.md         # Operational runbook and troubleshooting steps
    └── RootCauseAnalysis.md       # Playbook for the 2–3 day intermittent 100% CPU spike pattern
```

---

## Quickstart

### Build & Run Tests
```powershell
dotnet restore IISMonitor.slnx
dotnet test IISMonitor.slnx
```

### Publish Release Package
```powershell
dotnet publish src/IISMonitor.Service/IISMonitor.Service.csproj -c Release -o publish/ --self-contained false
```

### Install as a Windows Service
In an elevated PowerShell prompt:
```powershell
.\scripts\Install-IISMonitor.ps1
```

Access the real-time management dashboard:
```
http://localhost:5050
```

---

## Documentation

- [Architecture & Data Flow](docs/Architecture.md)
- [Installation & Configuration Reference](docs/Installation.md)
- [Operational Runbook](docs/Troubleshooting.md)
- [Forensic Root Cause Playbook](docs/RootCauseAnalysis.md)
