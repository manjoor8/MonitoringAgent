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
      document.getElementById('dash-server-name').innerText = data.Server ?? data.server ?? '-';
      document.getElementById('dash-server-badge').innerText = data.Environment ?? data.environment ?? 'Production';

      const cpuVal = data.CurrentCpuPercent ?? data.currentCpuPercent ?? 0;
      document.getElementById('dash-cpu-val').innerText = `${cpuVal.toFixed(1)}%`;
      const fill = document.getElementById('dash-cpu-fill');
      fill.style.width = `${Math.min(100, Math.max(0, cpuVal))}%`;

      fill.className = 'progress-bar-fill ' +
        (cpuVal >= 90 ? 'cpu-critical' : cpuVal >= 70 ? 'cpu-warning' : 'cpu-normal');

      const memVal = data.CommittedMemoryPercent ?? data.committedMemoryPercent ?? 0;
      document.getElementById('dash-mem-val').innerText = `${memVal.toFixed(0)}%`;
      document.getElementById('dash-state-badge').innerHTML = App.getStateBadge(data.State ?? data.state);

      // Top Processes Table
      const topProcs = data.TopProcesses || data.topProcesses || [];
      const procBody = document.getElementById('dash-processes-tbody');
      procBody.innerHTML = topProcs.map(p => {
        const pid = p.ProcessId ?? p.processId ?? 0;
        const name = p.ProcessName ?? p.processName ?? '-';
        const cpu = p.CpuPercent ?? p.cpuPercent ?? 0;
        const mem = p.PrivateBytes ?? p.privateBytes ?? 0;
        const threads = p.ThreadCount ?? p.threadCount ?? 0;
        return `
          <tr>
            <td class="mono">${pid}</td>
            <td><strong>${name}</strong></td>
            <td class="mono">${cpu.toFixed(1)}%</td>
            <td>${App.formatBytes(mem)}</td>
            <td>${threads}</td>
          </tr>
        `;
      }).join('') || '<tr><td colspan="5" style="text-align:center;color:var(--text-muted);">No active processes</td></tr>';

      // Top App Pools Table
      const topPools = data.TopAppPools || data.topAppPools || [];
      const poolBody = document.getElementById('dash-pools-tbody');
      poolBody.innerHTML = topPools.map(a => {
        const name = a.AppPoolName ?? a.appPoolName ?? '-';
        const pid = a.ProcessId ?? a.processId ?? 0;
        const cpu = a.CpuPercent ?? a.cpuPercent ?? 0;
        const mem = a.PrivateMemoryBytes ?? a.privateMemoryBytes ?? 0;
        const threads = a.ThreadCount ?? a.threadCount ?? 0;
        const state = a.State ?? a.state ?? (pid > 0 ? 'Running' : 'Idle');

        const pidHtml = pid > 0
          ? `<span class="mono">${pid}</span>`
          : `<span class="badge" style="background:var(--bg-secondary);color:var(--text-muted);">${state}</span>`;

        return `
          <tr>
            <td><strong>${name}</strong></td>
            <td>${pidHtml}</td>
            <td class="mono">${cpu.toFixed(1)}%</td>
            <td>${mem > 0 ? App.formatBytes(mem) : '-'}</td>
            <td>${threads > 0 ? threads : '-'}</td>
          </tr>
        `;
      }).join('') || '<tr><td colspan="5" style="text-align:center;color:var(--text-muted);">No application pools discovered</td></tr>';

      // Load Recent Chart Samples
      const recentRes = await fetch('/api/metrics/recent');
      if (recentRes.ok) {
        const recentSamples = await recentRes.json();
        const dataPoints = recentSamples.map(s => {
          const sm = s.ServerMetrics ?? s.serverMetrics ?? {};
          return {
            TimestampUtc: s.TimestampUtc ?? s.timestampUtc,
            TotalCpu: sm.TotalCpuPercent ?? sm.totalCpuPercent ?? 0
          };
        });
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
          const cpu = sample.Cpu ?? sample.cpu ?? 0;

          document.getElementById('dash-cpu-val').innerText = `${cpu.toFixed(1)}%`;
          const fill = document.getElementById('dash-cpu-fill');
          if (fill) {
            fill.style.width = `${Math.min(100, Math.max(0, cpu))}%`;
            fill.className = 'progress-bar-fill ' +
              (cpu >= 90 ? 'cpu-critical' : cpu >= 70 ? 'cpu-warning' : 'cpu-normal');
          }

          if (this.chart && this.chart.data) {
            this.chart.data.push({
              TimestampUtc: sample.Timestamp ?? sample.timestamp,
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
