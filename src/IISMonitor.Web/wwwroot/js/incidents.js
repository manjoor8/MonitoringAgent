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
        const start = new Date(inc.StartTimeUtc).toLocaleString();
        const duration = inc.DurationSeconds ? App.formatDuration(inc.DurationSeconds) : 'Active';
        const isRec = inc.Status === 'Recovered' || inc.Status === 'AutoRecovered';

        return `
          <tr>
            <td class="mono"><strong><a href="#incident/${inc.IncidentId}" style="color:var(--accent-blue);text-decoration:none;">${inc.IncidentId}</a></strong></td>
            <td>${inc.ServerName}</td>
            <td>${start}</td>
            <td class="mono">${duration}</td>
            <td class="mono"><strong>${inc.PeakCpuPercent.toFixed(1)}%</strong></td>
            <td>${inc.TopProcessName || '-'}</td>
            <td>${inc.TopAppPoolName || '-'}</td>
            <td>
              <span class="badge ${isRec ? 'badge-normal' : 'badge-critical'}">${inc.Status}</span>
            </td>
          </tr>
        `;
      }).join('');
    } catch (e) {
      tbody.innerHTML = `<tr><td colspan="8" style="text-align:center;color:var(--accent-red);">Error: ${e.message}</td></tr>`;
    }
  }
};
