/**
 * Incidents List view logic per Section 27.
 */
const Incidents = {
  async load() {
    const tbody = document.getElementById('incidents-tbody');
    tbody.innerHTML = '<tr><td colspan="8" style="text-align:center;">Loading incidents...</td></tr>';

    try {
      const res = await fetch('/api/incidents');
      if (!res.ok) throw new Error('Failed to load incidents');
      const incidents = await res.json();

      if (incidents.length === 0) {
        tbody.innerHTML = '<tr><td colspan="8" style="text-align:center;color:var(--text-muted);">No CPU incidents recorded.</td></tr>';
        return;
      }

      tbody.innerHTML = incidents.map(inc => {
        const id = inc.IncidentId ?? inc.incidentId ?? '';
        const server = inc.ServerName ?? inc.serverName ?? '-';
        const startRaw = inc.StartTimeUtc ?? inc.startTimeUtc;
        const start = startRaw ? new Date(startRaw).toLocaleString() : '-';
        const durationSec = inc.DurationSeconds ?? inc.durationSeconds;
        const duration = durationSec ? App.formatDuration(durationSec) : 'Active';
        const status = inc.Status ?? inc.status ?? 'Active';
        const isRec = status === 'Recovered' || status === 'AutoRecovered';
        const peakCpu = inc.PeakCpuPercent ?? inc.peakCpuPercent ?? 0;
        const topProc = inc.TopProcessName ?? inc.topProcessName ?? '-';
        const topPool = inc.TopAppPoolName ?? inc.topAppPoolName ?? '-';

        return `
          <tr>
            <td class="mono"><strong><a href="#incident/${id}" style="color:var(--accent-blue);text-decoration:none;">${id}</a></strong></td>
            <td>${server}</td>
            <td>${start}</td>
            <td class="mono">${duration}</td>
            <td class="mono"><strong>${peakCpu.toFixed(1)}%</strong></td>
            <td>${topProc}</td>
            <td>${topPool}</td>
            <td>
              <span class="badge ${isRec ? 'badge-normal' : 'badge-critical'}">${status}</span>
            </td>
          </tr>
        `;
      }).join('');
    } catch (e) {
      tbody.innerHTML = `<tr><td colspan="8" style="text-align:center;color:var(--accent-red);">Error: ${e.message}</td></tr>`;
    }
  }
};
