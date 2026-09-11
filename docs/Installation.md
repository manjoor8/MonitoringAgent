# Installation & Deployment Guide: IIS Root-Cause Monitoring Agent

## 1. Prerequisites

Before installing the agent on a target Windows IIS server, ensure:

1. **Operating System**: Windows Server 2016, 2019, 2022, or Windows 10/11.
2. **Runtime**: .NET 8.0 Runtime (or ASP.NET Core Runtime 8.0 Hosting Bundle).
3. **IIS Role**:
   - IIS Management Tools (`Web-Mgmt-Tools`)
   - IIS Management Scripts and Tools (`Web-Scripting-Tools` - recommended for appcmd)
4. **Administrative Rights**: The installer must run in an elevated PowerShell session (`Administrator`).
5. **Disk Space**: At least 10 GB free space on the drive hosting `C:\ProgramData\IISMonitor` (for SQLite database and ProcDump crash dumps).
6. **ProcDump (Optional)**: If crash dump capture is desired, download Sysinternals `procdump.exe` to `C:\Tools\procdump.exe`.

---

## 2. Building the Binaries

From your build workstation:

```powershell
# Restore dependencies
dotnet restore IISMonitor.slnx

# Build Release
dotnet build IISMonitor.slnx -c Release

# Publish self-contained or framework-dependent
dotnet publish src/IISMonitor.Service/IISMonitor.Service.csproj -c Release -o publish/ --self-contained false
```

---

## 3. Automated Installation via PowerShell

Copy the `publish/` folder and `scripts/` folder to the target server, then run in an elevated PowerShell prompt:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass

# Standard installation
.\scripts\Install-IISMonitor.ps1
```

### Custom Installation Parameters

```powershell
.\scripts\Install-IISMonitor.ps1 `
    -InstallDir "D:\Agents\IISMonitor" `
    -DataDir "D:\IISMonitorData" `
    -ServiceName "IISMonitorAgent"
```

The script will automatically:
1. Create target binary and data directories.
2. Copy all published application binaries and static dashboard assets.
3. Register the Windows Service using `sc.exe create`.
4. Configure service auto-restart on unexpected termination.
5. Start the service and display dashboard access URLs.

---

## 4. Configuration Reference (`appsettings.json`)

Configuration is stored in `appsettings.json` in the installation directory:

```json
{
  "ServerIdentity": {
    "MachineName": "WEB03",
    "EnvironmentName": "Production",
    "ServerId": "srv-web03"
  },

  "Monitoring": {
    "NormalIntervalSeconds": 30,
    "IncidentIntervalSeconds": 5,
    "CpuThresholdPercent": 70.0,
    "CriticalCpuThresholdPercent": 90.0,
    "RecoveryThresholdPercent": 60.0,
    "RecoveryConsecutiveSamples": 3,
    "BaselineRetentionMinutes": 60
  },

  "Database": {
    "ConnectionString": "Data Source=C:\\ProgramData\\IISMonitor\\monitor.db",
    "BusyTimeoutMs": 5000
  },

  "Diagnostics": {
    "ProcDumpEnabled": true,
    "ProcDumpPath": "C:\\Tools\\procdump.exe",
    "DumpDirectory": "C:\\ProgramData\\IISMonitor\\Dumps",
    "TriggerCpuPercent": 80.0,
    "TriggerDurationSeconds": 30,
    "MaxDumpsPerIncident": 3,
    "DumpType": "Full"
  },

  "AutomaticRecovery": {
    "Enabled": false,
    "CriticalCpuThresholdPercent": 90.0,
    "MinimumCriticalDurationMinutes": 5,
    "MinimumCulpritCpuPercent": 70.0,
    "MaxRestartsPerHour": 1,
    "MaxRestartsPerDay": 3,
    "CooldownMinutes": 30
  }
}
```

---

## 5. Verifying Installation & Accessing Dashboard

1. Check Windows Service status:
   ```powershell
   Get-Service IISMonitorAgent
   ```

2. Open the Management Dashboard:
   - Navigate to `http://localhost:5050` in a web browser on the server.
   - Verify that the **Server CPU**, **Top Processes**, and **Top Application Pools** are updating in real time.

---

## 6. Uninstallation

To cleanly remove the agent:

```powershell
# Uninstalls service and binaries, preserves SQLite database and dumps
.\scripts\Uninstall-IISMonitor.ps1

# To purge database and dump files as well:
.\scripts\Uninstall-IISMonitor.ps1 -PurgeData
```
