/**
 * Detailed Incident Root-Cause Investigation View per Sections 28-33, 42, 65, 66.
 */
const IncidentDetail = {
  cpuChart: null,

  async load(incidentId) {
    if (!this.cpuChart) {
      this.cpuChart = new TimelineChart('incident-cpu-chart');
    }

    document.getElementById('inc-detail-id').innerText = incidentId;

    try {
      // 1. Summary
      const incRes = await fetch(`/api/incidents/${incidentId}`);
      if (!incRes.ok) throw new Error('Incident not found');
      const inc = await incRes.json();

      document.getElementById('detail-server').innerText = inc.ServerName || '-';
      document.getElementById('detail-start').innerText = new Date(inc.StartTimeUtc).toLocaleString();
      document.getElementById('detail-end').innerText = inc.EndTimeUtc ? new Date(inc.EndTimeUtc).toLocaleString() : 'Active';
      document.getElementById('detail-duration').innerText = App.formatDuration(inc.DurationSeconds);
      document.getElementById('detail-peak-cpu').innerText = `${inc.PeakCpuPercent.toFixed(1)}%`;
      document.getElementById('detail-top-proc').innerText = `${inc.TopProcessName || '-'} (PID: ${inc.TopProcessId || '-'})`;
      document.getElementById('detail-top-pool').innerText = inc.TopAppPoolName || '-';
      document.getElementById('detail-status').innerHTML = inc.Status === 'Recovered' || inc.Status === 'AutoRecovered'
        ? '<span class="badge badge-normal">Recovered</span>'
        : '<span class="badge badge-critical">Active</span>';

      // 2. CPU Timeline Chart
      const timelineRes = await fetch(`/api/incidents/${incidentId}/timeline`);
      if (timelineRes.ok) {
        const samples = await timelineRes.json();
        this.cpuChart.setData(samples, [
          { key: 'TotalCpu', label: 'Total CPU %', color: '#38bdf8', width: 2 },
          { key: 'UserCpu', label: 'User CPU %', color: '#22c55e', width: 1.5 },
          { key: 'PrivilegedCpu', label: 'System CPU %', color: '#a855f7', width: 1.5 }
        ]);
      }

      // 3. App Pool Ranking
      const poolsRes = await fetch(`/api/incidents/${incidentId}/apppools`);
      if (poolsRes.ok) {
        const pools = await poolsRes.json();
        const tbody = document.getElementById('inc-pools-tbody');
        tbody.innerHTML = pools.map(p => `
          <tr>
            <td><strong>${p.AppPoolName}</strong></td>
            <td class="mono"><strong>${p.PeakCpu.toFixed(1)}%</strong></td>
            <td class="mono">${p.AvgCpu.toFixed(1)}%</td>
            <td class="mono">${p.ProcessId}</td>
            <td>${App.formatBytes(p.MaxPrivateMemoryBytes)}</td>
            <td>${p.MaxThreadCount}</td>
            <td>${p.MaxQueueLength}</td>
          </tr>
        `).join('') || '<tr><td colspan="7" style="text-align:center;">No AppPool data available</td></tr>';
      }

      // 4. Root Cause Evidence Report
      const rcRes = await fetch(`/api/incidents/${incidentId}/rootcause`);
      if (rcRes.ok) {
        const rc = await rcRes.json();
        const container = document.getElementById('inc-rootcause-container');
        container.innerHTML = `
          <div class="evidence-card">
            <div class="evidence-title">Primary CPU Consumer: ${rc.PrimaryProcessName} (${rc.PrimaryProcessPeakCpu.toFixed(1)}%)</div>
            <div class="evidence-body">Assessment: <strong>${rc.PrimaryProcessConfidence}</strong>. PID: ${rc.PrimaryProcessId || 'N/A'}</div>
          </div>

          <div class="evidence-card">
            <div class="evidence-title">Primary Application Pool: ${rc.PrimaryAppPoolName || 'None identified'} (${rc.PrimaryAppPoolPeakCpu.toFixed(1)}%)</div>
            <div class="evidence-body">Assessment: <strong>${rc.PrimaryAppPoolConfidence}</strong>. Associated Websites: ${rc.AffectedWebsites.join(', ') || 'None'}</div>
          </div>

          <div class="evidence-card">
            <div class="evidence-title">Traffic & Request Correlation</div>
            <div class="evidence-body">${rc.TrafficSummary} Assessment: <strong>${rc.TrafficAssessment}</strong></div>
          </div>

          <div class="evidence-card">
            <div class="evidence-title">Antivirus / EDR (MsMpEng) Activity</div>
            <div class="evidence-body">${rc.SecuritySummary} Assessment: <strong>${rc.SecurityAssessment}</strong></div>
          </div>

          <div class="evidence-card">
            <div class="evidence-title">Correlated Scheduled Tasks</div>
            <div class="evidence-body">
              ${rc.CorrelatedScheduledTasks.length > 0 ? rc.CorrelatedScheduledTasks.join(', ') : 'No scheduled tasks running during CPU spike.'}
              Assessment: <strong>${rc.ScheduledTaskAssessment}</strong>
            </div>
          </div>
        `;
      }

      // 5. Similar Incidents Fingerprinting per Section 42
      const simRes = await fetch(`/api/incidents/${incidentId}/similar`);
      if (simRes.ok) {
        const similar = await simRes.json();
        const simContainer = document.getElementById('inc-similar-container');
        if (similar.length === 0) {
          simContainer.innerHTML = '<p style="color:var(--text-muted);font-size:13px;">No similar historical incidents found.</p>';
        } else {
          simContainer.innerHTML = similar.map(s => `
            <div style="padding:8px 12px;background:rgba(255,255,255,0.03);margin-bottom:6px;border-radius:4px;display:flex;justify-content:space-between;align-items:center;">
              <div>
                <strong><a href="#incident/${s.IncidentId}" style="color:var(--accent-blue);text-decoration:none;">${s.IncidentId}</a></strong>
                <span style="color:var(--text-muted);margin-left:8px;">${s.AppPoolName} (Peak: ${s.PeakCpu.toFixed(1)}%)</span>
              </div>
              <div>
                <span class="badge badge-blue">${(s.SimilarityScore * 100).toFixed(0)}% Similar</span>
              </div>
            </div>
          `).join('');
        }
      }

      // 6. Automatic Recovery Audit per Section 65-66
      const recRes = await fetch(`/api/incidents/${incidentId}/recovery`);
      if (recRes.ok) {
        const rec = await recRes.json();
        const recCard = document.getElementById('inc-recovery-card');
        if (rec.Performed === false || !rec.Action) {
          recCard.innerHTML = `
            <p style="color:var(--text-secondary);font-size:13px;">Automatic Recovery: <strong>Not Performed / Not Eligible</strong></p>
          `;
        } else {
          recCard.innerHTML = `
            <div style="border-left: 4px solid #22c55e; background: rgba(34,197,94,0.05); padding: 12px 16px; border-radius: 4px;">
              <h4 style="color:#22c55e;margin-bottom:6px;">Recovery Action Executed: ${rec.Action}</h4>
              <p style="font-size:13px;color:var(--text-primary);margin-bottom:6px;">
                Application Pool: <strong>${rec.AppPoolName}</strong> | Result: <strong>${rec.Result}</strong> | Timestamp: ${new Date(rec.TimestampUtc).toLocaleTimeString()}
              </p>
              <p style="font-size:12px;color:var(--text-secondary);">
                CPU before restart: <strong>${rec.CpuBeforeRestart?.toFixed(1)}%</strong> |
                CPU after 30s: <strong>${rec.CpuAfter30s ? rec.CpuAfter30s.toFixed(1) + '%' : 'Pending'}</strong> |
                CPU after 60s: <strong>${rec.CpuAfter60s ? rec.CpuAfter60s.toFixed(1) + '%' : 'Pending'}</strong>
              </p>
              <div style="margin-top:8px;font-size:12px;color:var(--text-muted);font-style:italic;">
                Notice: Application Pool recycle temporarily eliminated the CPU condition and confirms strong association. Review diagnostic dump and runtime metrics for underlying application defect.
              </div>
            </div>
          `;
        }
      }
    } catch (e) {
      console.error('Failed to load incident detail', e);
    }
  }
};
