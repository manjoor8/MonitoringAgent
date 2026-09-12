/**
 * Detailed Incident Root-Cause Investigation View per Sections 28-33, 42, 65, 66.
 */
const IncidentDetail = {
  cpuChart: null,

  async load(incidentId) {
    if (!this.cpuChart) {
      this.cpuChart = new TimelineChart('incident-cpu-chart');
    }

    const setElText = (id, val) => {
      const el = document.getElementById(id);
      if (el) el.innerText = (val !== null && val !== undefined && val !== '') ? val : '-';
    };

    const setElHtml = (id, html) => {
      const el = document.getElementById(id);
      if (el) el.innerHTML = html;
    };

    setElText('inc-detail-id', incidentId);

    // 1. Summary Header Card
    let topProc = '-';
    let topProcPid = '-';
    let topPool = '-';

    try {
      const incRes = await fetch(`/api/incidents/${incidentId}`);
      if (incRes.ok) {
        const inc = await incRes.json();
        const server = inc.ServerName ?? inc.serverName ?? '-';
        const startRaw = inc.StartTimeUtc ?? inc.startTimeUtc;
        const start = startRaw ? new Date(startRaw).toLocaleString() : '-';
        const endRaw = inc.EndTimeUtc ?? inc.endTimeUtc;
        const end = endRaw ? new Date(endRaw).toLocaleString() : 'Active';
        const durationSec = inc.DurationSeconds ?? inc.durationSeconds;
        const duration = App.formatDuration(durationSec);
        const peakCpu = (inc.PeakCpuPercent ?? inc.peakCpuPercent ?? 0).toFixed(1);
        topProc = inc.TopProcessName ?? inc.topProcessName ?? '-';
        topProcPid = inc.TopProcessId ?? inc.topProcessId ?? '-';
        topPool = inc.TopAppPoolName ?? inc.topAppPoolName ?? '-';
        const status = inc.Status ?? inc.status ?? 'Active';

        setElText('detail-server', server);
        setElText('detail-start', start);
        setElText('detail-end', end);
        setElText('detail-duration', duration);
        setElText('detail-peak-cpu', `${peakCpu}%`);
        setElText('detail-top-proc', topProc !== '-' ? `${topProc} (PID: ${topProcPid})` : '-');
        setElText('detail-top-pool', topPool);
        setElHtml('detail-status', status === 'Recovered' || status === 'AutoRecovered'
          ? '<span class="badge badge-normal">Recovered</span>'
          : '<span class="badge badge-critical">Active</span>');
      }
    } catch (e) {
      console.error('Failed to load incident summary', e);
    }

    // 2. CPU Timeline Chart
    try {
      const timelineRes = await fetch(`/api/incidents/${incidentId}/timeline`);
      if (timelineRes.ok) {
        const samples = await timelineRes.json();
        const dataPoints = samples.map(s => {
          const sm = s.ServerMetrics ?? s.serverMetrics ?? {};
          return {
            TimestampUtc: s.TimestampUtc ?? s.timestampUtc,
            TotalCpu: s.TotalCpu ?? s.totalCpu ?? sm.TotalCpuPercent ?? sm.totalCpuPercent ?? 0,
            UserCpu: s.UserCpu ?? s.userCpu ?? sm.UserCpuPercent ?? sm.userCpuPercent ?? 0,
            PrivilegedCpu: s.PrivilegedCpu ?? s.privilegedCpu ?? sm.PrivilegedCpuPercent ?? sm.privilegedCpuPercent ?? 0
          };
        });
        this.cpuChart.setData(dataPoints, [
          { key: 'TotalCpu', label: 'Total CPU %', color: '#38bdf8', width: 2 },
          { key: 'UserCpu', label: 'User CPU %', color: '#22c55e', width: 1.5 },
          { key: 'PrivilegedCpu', label: 'System CPU %', color: '#a855f7', width: 1.5 }
        ]);
      }
    } catch (e) {
      console.error('Failed to load timeline chart', e);
    }

    // 3. App Pool Ranking
    try {
      const poolsRes = await fetch(`/api/incidents/${incidentId}/apppools`);
      if (poolsRes.ok) {
        const pools = await poolsRes.json();
        const tbody = document.getElementById('inc-pools-tbody');
        if (tbody) {
          tbody.innerHTML = pools.map(p => {
            const name = p.AppPoolName ?? p.appPoolName ?? '-';
            const peak = (p.PeakCpu ?? p.peakCpu ?? 0).toFixed(1);
            const avg = (p.AvgCpu ?? p.avgCpu ?? 0).toFixed(1);
            const pid = p.ProcessId ?? p.processId ?? '-';
            const mem = p.MaxPrivateMemoryBytes ?? p.maxPrivateMemoryBytes ?? 0;
            const threads = p.MaxThreadCount ?? p.maxThreadCount ?? 0;
            const queue = p.MaxQueueLength ?? p.maxQueueLength ?? 0;
            return `
              <tr>
                <td><strong>${name}</strong></td>
                <td class="mono"><strong>${peak}%</strong></td>
                <td class="mono">${avg}%</td>
                <td class="mono">${pid}</td>
                <td>${App.formatBytes(mem)}</td>
                <td>${threads}</td>
                <td>${queue}</td>
              </tr>
            `;
          }).join('') || '<tr><td colspan="7" style="text-align:center;">No AppPool data available</td></tr>';
        }
      }
    } catch (e) {
      console.error('Failed to load app pool ranking', e);
    }

    // 4. Root Cause Evidence Report
    try {
      const rcRes = await fetch(`/api/incidents/${incidentId}/rootcause`);
      if (rcRes.ok) {
        const rc = await rcRes.json();
        const container = document.getElementById('inc-rootcause-container');
        const primProc = rc.PrimaryProcessName ?? rc.primaryProcessName ?? '-';
        const primProcCpu = (rc.PrimaryProcessPeakCpu ?? rc.primaryProcessPeakCpu ?? 0).toFixed(1);
        const primProcConf = App.getAssessmentBadge(rc.PrimaryProcessConfidence ?? rc.primaryProcessConfidence);
        const primProcPid = rc.PrimaryProcessId ?? rc.primaryProcessId ?? 'N/A';

        const primPool = rc.PrimaryAppPoolName ?? rc.primaryAppPoolName ?? 'None identified';
        const primPoolCpu = (rc.PrimaryAppPoolPeakCpu ?? rc.primaryAppPoolPeakCpu ?? 0).toFixed(1);
        const primPoolConf = App.getAssessmentBadge(rc.PrimaryAppPoolConfidence ?? rc.primaryAppPoolConfidence);
        const affectedWebsites = rc.AffectedWebsites ?? rc.affectedWebsites ?? [];

        // Fallback update to top summary header if process/apppool was not resolved
        if ((!topProc || topProc === '-' || topProc.startsWith('-')) && primProc !== '-') {
          setElText('detail-top-proc', `${primProc} (PID: ${primProcPid})`);
        }
        if ((!topPool || topPool === '-' || topPool.startsWith('-')) && primPool !== 'None identified') {
          setElText('detail-top-pool', primPool);
        }

        const trafficSummary = rc.TrafficSummary ?? rc.trafficSummary ?? '';
        const trafficAssess = App.getAssessmentBadge(rc.TrafficAssessment ?? rc.trafficAssessment);
        const secSummary = rc.SecuritySummary ?? rc.securitySummary ?? '';
        const secAssess = App.getAssessmentBadge(rc.SecurityAssessment ?? rc.securityAssessment);
        const schedTasks = rc.CorrelatedScheduledTasks ?? rc.correlatedScheduledTasks ?? [];
        const schedAssess = App.getAssessmentBadge(rc.ScheduledTaskAssessment ?? rc.scheduledTaskAssessment);

        if (container) {
          container.innerHTML = `
            <div class="evidence-card">
              <div class="evidence-title">Primary CPU Consumer: ${primProc} (${primProcCpu}%)</div>
              <div class="evidence-body">Assessment: ${primProcConf}. PID: ${primProcPid}</div>
            </div>

            <div class="evidence-card">
              <div class="evidence-title">Primary Application Pool: ${primPool} (${primPoolCpu}%)</div>
              <div class="evidence-body">Assessment: ${primPoolConf}. Associated Websites: ${affectedWebsites.join(', ') || 'None'}</div>
            </div>

            <div class="evidence-card">
              <div class="evidence-title">Traffic & Request Correlation</div>
              <div class="evidence-body">${trafficSummary} Assessment: ${trafficAssess}</div>
            </div>

            <div class="evidence-card">
              <div class="evidence-title">Antivirus / EDR (MsMpEng) Activity</div>
              <div class="evidence-body">${secSummary} Assessment: ${secAssess}</div>
            </div>

            <div class="evidence-card">
              <div class="evidence-title">Correlated Scheduled Tasks</div>
              <div class="evidence-body">
                ${schedTasks.length > 0 ? schedTasks.join(', ') : 'No scheduled tasks running during CPU spike.'}
                Assessment: ${schedAssess}
              </div>
            </div>
          `;
        }
      }
    } catch (e) {
      console.error('Failed to load root cause evidence', e);
    }

    // 5. Similar Incidents Fingerprinting per Section 42
    try {
      const simRes = await fetch(`/api/incidents/${incidentId}/similar`);
      if (simRes.ok) {
        const similar = await simRes.json();
        const simContainer = document.getElementById('inc-similar-container');
        if (simContainer) {
          if (similar.length === 0) {
            simContainer.innerHTML = '<p style="color:var(--text-muted);font-size:13px;">No similar historical incidents found.</p>';
          } else {
            simContainer.innerHTML = similar.map(s => {
              const sId = s.IncidentId ?? s.incidentId ?? '';
              const sPool = s.AppPoolName ?? s.appPoolName ?? '-';
              const sPeak = (s.PeakCpu ?? s.peakCpu ?? 0).toFixed(1);
              const score = ((s.SimilarityScore ?? s.similarityScore ?? 0) * 100).toFixed(0);
              return `
                <div style="padding:8px 12px;background:rgba(255,255,255,0.03);margin-bottom:6px;border-radius:4px;display:flex;justify-content:space-between;align-items:center;">
                  <div>
                    <strong><a href="#incident/${sId}" style="color:var(--accent-blue);text-decoration:none;">${sId}</a></strong>
                    <span style="color:var(--text-muted);margin-left:8px;">${sPool} (Peak: ${sPeak}%)</span>
                  </div>
                  <div>
                    <span class="badge badge-blue">${score}% Similar</span>
                  </div>
                </div>
              `;
            }).join('');
          }
        }
      }
    } catch (e) {
      console.error('Failed to load similar incidents', e);
    }

    // 6. Automatic Recovery Audit per Section 65-66
    try {
      const recRes = await fetch(`/api/incidents/${incidentId}/recovery`);
      if (recRes.ok) {
        const rec = await recRes.json();
        const recCard = document.getElementById('inc-recovery-card');
        if (recCard) {
          const performed = rec.Performed ?? rec.performed;
          const action = rec.Action ?? rec.action;
          if (performed === false || !action) {
            recCard.innerHTML = `
              <p style="color:var(--text-secondary);font-size:13px;">Automatic Recovery: <strong>Not Performed / Not Eligible</strong></p>
            `;
          } else {
            const recPool = rec.AppPoolName ?? rec.appPoolName ?? '-';
            const recResult = rec.Result ?? rec.result ?? '-';
            const recTime = (rec.TimestampUtc ?? rec.timestampUtc) ? new Date(rec.TimestampUtc ?? rec.timestampUtc).toLocaleTimeString() : '-';
            const cpuBefore = (rec.CpuBeforeRestart ?? rec.cpuBeforeRestart)?.toFixed(1);
            const cpu30 = rec.CpuAfter30s ?? rec.cpuAfter30s;
            const cpu60 = rec.CpuAfter60s ?? rec.cpuAfter60s;

            recCard.innerHTML = `
              <div style="border-left: 4px solid #22c55e; background: rgba(34,197,94,0.05); padding: 12px 16px; border-radius: 4px;">
                <h4 style="color:#22c55e;margin-bottom:6px;">Recovery Action Executed: ${action}</h4>
                <p style="font-size:13px;color:var(--text-primary);margin-bottom:6px;">
                  Application Pool: <strong>${recPool}</strong> | Result: <strong>${recResult}</strong> | Timestamp: ${recTime}
                </p>
                <p style="font-size:12px;color:var(--text-secondary);">
                  CPU before restart: <strong>${cpuBefore ? cpuBefore + '%' : 'N/A'}</strong> |
                  CPU after 30s: <strong>${cpu30 != null ? cpu30.toFixed(1) + '%' : 'Pending'}</strong> |
                  CPU after 60s: <strong>${cpu60 != null ? cpu60.toFixed(1) + '%' : 'Pending'}</strong>
                </p>
                <div style="margin-top:8px;font-size:12px;color:var(--text-muted);font-style:italic;">
                  Notice: Application Pool recycle temporarily eliminated the CPU condition and confirms strong association. Review diagnostic dump and runtime metrics for underlying application defect.
                </div>
              </div>
            `;
          }
        }
      }
    } catch (e) {
      console.error('Failed to load recovery audit', e);
    }
  }
};
