/**
 * Live Dashboard view logic.
 */
const Dashboard = {
  chart: null,
  eventSource: null,

  init() {
    if (!this.chart) {
      this.chart = new TimelineChart('dashboard-chart');
    }
    this.refresh();
    this.startLiveStream();
  },

  async refresh() {
    try {
      const res = await fetch('/api/dashboard');
      if (!res.ok) return;
      const data = await res.json();

      // Update Top Metrics
      document.getElementById('dash-server-name').innerText = data.Server || '-';
      document.getElementById('dash-server-badge').innerText = data.Environment || 'Production';

      const cpuVal = data.CurrentCpuPercent || 0;
      document.getElementById('dash-cpu-val').innerText = `${cpuVal.toFixed(1)}%`;
      const fill = document.getElementById('dash-cpu-fill');
      fill.style.width = `${Math.min(100, Math.max(0, cpuVal))}%`;

      fill.className = 'progress-bar-fill ' +
        (cpuVal >= 90 ? 'cpu-critical' : cpuVal >= 70 ? 'cpu-warning' : 'cpu-normal');

      document.getElementById('dash-mem-val').innerText = `${(data.CommittedMemoryPercent || 0).toFixed(0)}%`;
      document.getElementById('dash-state-badge').innerHTML = App.getStateBadge(data.State);

      // Top Processes Table
      const procBody = document.getElementById('dash-processes-tbody');
      procBody.innerHTML = (data.TopProcesses || []).map(p => `
        <tr>
          <td class="mono">${p.ProcessId}</td>
          <td><strong>${p.ProcessName}</strong></td>
          <td class="mono">${p.CpuPercent.toFixed(1)}%</td>
          <td>${App.formatBytes(p.PrivateBytes)}</td>
          <td>${p.ThreadCount}</td>
        </tr>
      `).join('') || '<tr><td colspan="5" style="text-align:center;color:var(--text-muted);">No active processes</td></tr>';

      // Top App Pools Table
      const poolBody = document.getElementById('dash-pools-tbody');
      poolBody.innerHTML = (data.TopAppPools || []).map(a => `
        <tr>
          <td><strong>${a.AppPoolName}</strong></td>
          <td class="mono">${a.ProcessId}</td>
          <td class="mono">${a.CpuPercent.toFixed(1)}%</td>
          <td>${App.formatBytes(a.PrivateMemoryBytes)}</td>
          <td>${a.ThreadCount}</td>
        </tr>
      `).join('') || '<tr><td colspan="5" style="text-align:center;color:var(--text-muted);">No active worker processes</td></tr>';

      // Load Recent Chart Samples
      const recentRes = await fetch('/api/metrics/recent');
      if (recentRes.ok) {
        const recentSamples = await recentRes.json();
        const dataPoints = recentSamples.map(s => ({
          TimestampUtc: s.TimestampUtc,
          TotalCpu: s.ServerMetrics.TotalCpuPercent
        }));
        this.chart.setData(dataPoints, [
          { key: 'TotalCpu', label: 'Server CPU %', color: '#38bdf8', width: 2 }
        ]);
      }
    } catch (e) {
      console.error('Dashboard refresh failed', e);
    }
  },

  startLiveStream() {
    if (this.eventSource) {
      this.eventSource.close();
    }

    try {
      this.eventSource = new EventSource('/api/metrics/live');
      this.eventSource.onmessage = (event) => {
        try {
          const sample = JSON.parse(event.data);
          const cpu = sample.Cpu || 0;

          document.getElementById('dash-cpu-val').innerText = `${cpu.toFixed(1)}%`;
          const fill = document.getElementById('dash-cpu-fill');
          if (fill) {
            fill.style.width = `${Math.min(100, Math.max(0, cpu))}%`;
            fill.className = 'progress-bar-fill ' +
              (cpu >= 90 ? 'cpu-critical' : cpu >= 70 ? 'cpu-warning' : 'cpu-normal');
          }

          if (this.chart && this.chart.data) {
            this.chart.data.push({
              TimestampUtc: sample.Timestamp,
              TotalCpu: cpu
            });
            if (this.chart.data.length > 120) {
              this.chart.data.shift();
            }
            this.chart.render();
          }
        } catch (e) {
          // Parse error
        }
      };
    } catch (e) {
      console.warn('SSE connection failed', e);
    }
  }
};
