# Troubleshooting & Operations Guide: IIS Root-Cause Monitoring Agent

## 1. Operating the Monitoring Agent

The agent is designed for unattended operation with minimal host overhead (<1% CPU, <200MB RAM).

### Service Management
```powershell
# Check status
Get-Service IISMonitorAgent

# Restart agent
Restart-Service IISMonitorAgent

# Inspect Windows Event Log messages from agent
Get-WinEvent -ProviderName "IISMonitorAgent" -MaxEvents 20
```

---

## 2. Common Operational Issues

### Issue 1: "UnauthorizedAccessException" when accessing ServerManager
- **Cause**: The service or interactive session lacks Local Administrator elevation. Reading `C:\Windows\System32\inetsrv\config\applicationHost.config` requires administrative privileges.
- **Resolution**: Ensure the Windows Service runs under `NT AUTHORITY\SYSTEM` or a dedicated service account added to the Local Administrators group.

### Issue 2: ProcDump does not capture memory dumps
- **Check 1**: Is `ProcDumpEnabled` set to `true` in `appsettings.json`?
- **Check 2**: Does `procdump.exe` exist at the configured path (e.g. `C:\Tools\procdump.exe`)?
- **Check 3**: Is there at least 5 GB of free disk space on the drive hosting the dump folder?
- **Check 4**: Has the incident exceeded `MaxDumpsPerIncident` (default: 3)?

### Issue 3: SQLite `busy_timeout` or Database Locking
- The agent configures `PRAGMA journal_mode = WAL;` and `busy_timeout = 5000;`.
- Avoid opening the database directly in locking third-party GUI tools on the server while the service is actively capturing an incident. Use read-only queries or copy the `.db` file before inspecting.

---

## 3. Investigating an Incident Step-by-Step

When an alert triggers or the dashboard shows an incident:

1. Open `http://localhost:5050` and click the **Incidents** tab.
2. Select the incident ID (e.g., `INC-20260911-001`).
3. Check the **Primary CPU Consumer**:
   - If `w3wp.exe`: Review the mapped **Primary Application Pool** and **Associated Websites**.
   - If `MsMpEng.exe`: CPU was consumed by Microsoft Defender scanning rather than your IIS websites.
   - If a third-party process (e.g., backup agent or database): The issue is external to IIS.
4. Check the **CPU Profile Graph**:
   - Review the 30–60 minutes prior to the spike. Did CPU gradually creep up or suddenly jump from 40% to 100%?
5. Review the **Root Cause Evidence Summary**:
   - **Traffic volume**: Did requests/minute surge proportionally with CPU, or did CPU spike while request volume remained flat? (A flat request volume during 100% CPU strongly indicates runaway worker threads, threadpool starvation, or infinite loops).
   - **Memory & GC**: Did Gen 2 collections or private bytes escalate sharply?
   - **Scheduled Tasks**: Did a batch job start immediately before the CPU spike?
6. Check **Similar Incidents**:
   - The agent computes a multi-dimensional fingerprint. If the similarity is >80% to a past incident, the root cause is recurring with the same signature.
