# Forensic Root-Cause Analysis Guide: 2-3 Day Intermittent CPU Spikes

This document provides a playbook for investigating the recurring production issue described in the environment specification:
- 5 Windows IIS web servers
- ~25 websites per server
- CPU suddenly jumps from 40% to 100% every 2–3 days, lasting 10–15 minutes (rarely up to 40 minutes)
- Recovers on its own without manual intervention.

---

## 1. The Forensic Decision Matrix

```
                             Server CPU = 100%
                                    │
                                    ▼
                         Is top process w3wp.exe?
                               /         \
                             NO           YES
                             │             │
                             ▼             ▼
                     Investigate OS       Find Culprit AppPool
                     (e.g. Defender,      & Website(s)
                      Backup, Tasks)       │
                                           ▼
                                    Did Request Volume
                                    increase significantly?
                                      /          \
                                    YES           NO
                                    │              │
                                    ▼              ▼
                              Traffic Surge   Internal Workload /
                              or DDoS Attack  Application Anomaly
                                                   │
                                                   ▼
                                          Check Gen 2 GC & Memory
                                             /            \
                                           HIGH          NORMAL
                                           │              │
                                           ▼              ▼
                                     GC Thrashing   CPU Loop, Contention,
                                     or LOH Leak    or Thread Starvation
```

---

## 2. Forensic Indicators & Evidence Interpretation

### Scenario A: IIS w3wp.exe CPU = 95%, Request Rate = Normal
* **Symptom**: CPU hits 100%, but IIS request count per minute shows no change.
* **Interpretation**: The CPU spike is NOT driven by incoming user web traffic.
* **Likely Causes**:
  1. Internal background `Task.Run` or timers inside the web application.
  2. Database query returning an unusually large dataset leading to intensive in-memory sorting/serialization.
  3. Infinite recursion or tight loops in business logic.
  4. Regular Expression denial of service (ReDoS) on user input parsing.
* **Evidence to Examine**:
  - Captured `.dmp` file from `C:\ProgramData\IISMonitor\Dumps`.
  - Open in Visual Studio / WinDbg: run `~*e !clrstack` to locate threads actively consuming CPU.

### Scenario B: w3wp.exe CPU = 95%, High Gen 2 GC Pauses
* **Symptom**: Root cause card reports elevated `% Time in GC` and Gen 2 collections.
* **Interpretation**: The application is experiencing Large Object Heap (LOH) allocation fragmentation or memory thrashing.
* **Evidence to Examine**:
  - Inspect `.NET CLR Memory` metrics in the incident details.
  - Inspect dump with `!dumpheap -stat` to identify which objects dominate the managed heap.

### Scenario C: Non-IIS Process (e.g. `MsMpEng.exe` = 85%)
* **Symptom**: Root cause card reports `Primary Process: MsMpEng.exe (85%)` and `w3wp.exe: 8%`.
* **Interpretation**: Microsoft Defender or EDR software initiated a scheduled scan or caught a file-locking event.
* **Evidence to Examine**:
  - Correlate scheduled tasks running at that minute.
  - Review Windows Defender operational logs in the Windows Event Log view.
  - Add appropriate folder exclusions for IIS temporary compilation files (`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\Temporary ASP.NET Files`).

### Scenario D: Scheduled Task Correlation
* **Symptom**: Root cause card reports `Task "NightlyBatchSync" started at 14:31:00`.
* **Interpretation**: A Windows Scheduled Task fired on this server and triggered heavy local disk or network I/O, impacting IIS worker threads.

---

## 3. How to Analyze a Captured Memory Dump

When ProcDump captures a dump in `C:\ProgramData\IISMonitor\Dumps`:

1. Copy the `.dmp` file to your analysis workstation.
2. Open the file in **Visual Studio** (File -> Open -> File) or **WinDbg**:
   - In Visual Studio: Click **"Debug Managed Memory"** or **"Debug with Managed Only"**.
   - Review the **Parallel Stacks** window to see where all threads are parked.
3. In **WinDbg**:
   ```text
   .loadby sos coreclr    (or .loadby sos clr for .NET Framework)
   ~*e !clrstack          (Dumps stack traces of all managed threads)
   !runaway               (Shows which thread consumed the most CPU time)
   ~<ThreadNumber>s       (Switch to runaway thread)
   !clrstack              (Inspect what code that specific thread is executing)
   ```
4. This pinpointed call stack will reveal the exact method, loop, or query causing the 100% CPU spike.
