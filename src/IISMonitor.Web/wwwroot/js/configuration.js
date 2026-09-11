/**
 * Configuration editor view logic per Section 64.
 */
const Configuration = {
  async load() {
    try {
      const res = await fetch('/api/config');
      if (!res.ok) return;
      const data = await res.json();

      const m = data.Monitoring;
      document.getElementById('cfg-normal-interval').value = m.NormalIntervalSeconds;
      document.getElementById('cfg-incident-interval').value = m.IncidentIntervalSeconds;
      document.getElementById('cfg-cpu-threshold').value = m.CpuThresholdPercent;
      document.getElementById('cfg-critical-threshold').value = m.CriticalCpuThresholdPercent;
      document.getElementById('cfg-recovery-threshold').value = m.RecoveryThresholdPercent;
      document.getElementById('cfg-recovery-samples').value = m.RecoveryConsecutiveSamples;

      const r = data.AutomaticRecovery;
      document.getElementById('cfg-recovery-enabled').checked = r.Enabled;
      document.getElementById('cfg-rec-critical-cpu').value = r.CriticalCpuThresholdPercent;
      document.getElementById('cfg-rec-duration').value = r.MinimumCriticalDurationMinutes;
      document.getElementById('cfg-rec-culprit-cpu').value = r.MinimumCulpritCpuPercent;
      document.getElementById('cfg-rec-max-hour').value = r.MaxRestartsPerHour;
      document.getElementById('cfg-rec-max-day').value = r.MaxRestartsPerDay;
      document.getElementById('cfg-rec-cooldown').value = r.CooldownMinutes;
    } catch (e) {
      console.error('Failed to load configuration', e);
    }
  },

  async saveMonitoring() {
    const payload = {
      NormalIntervalSeconds: parseInt(document.getElementById('cfg-normal-interval').value, 10),
      IncidentIntervalSeconds: parseInt(document.getElementById('cfg-incident-interval').value, 10),
      CpuThresholdPercent: parseFloat(document.getElementById('cfg-cpu-threshold').value),
      CriticalCpuThresholdPercent: parseFloat(document.getElementById('cfg-critical-threshold').value),
      RecoveryThresholdPercent: parseFloat(document.getElementById('cfg-recovery-threshold').value),
      RecoveryConsecutiveSamples: parseInt(document.getElementById('cfg-recovery-samples').value, 10)
    };

    try {
      const res = await fetch('/api/config/monitoring', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
      });
      if (res.ok) {
        alert('Monitoring settings saved successfully.');
      } else {
        alert('Failed to save monitoring settings.');
      }
    } catch (e) {
      alert(`Error saving settings: ${e.message}`);
    }
  },

  async saveRecovery() {
    const enabled = document.getElementById('cfg-recovery-enabled').checked;
    const confirmed = document.getElementById('cfg-rec-confirm').checked;

    if (enabled && !confirmed) {
      alert('You must check the confirmation checkbox before enabling Automatic Recovery.');
      return;
    }

    const payload = {
      Enabled: enabled,
      CriticalCpuThresholdPercent: parseFloat(document.getElementById('cfg-rec-critical-cpu').value),
      MinimumCriticalDurationMinutes: parseInt(document.getElementById('cfg-rec-duration').value, 10),
      MinimumCulpritCpuPercent: parseFloat(document.getElementById('cfg-rec-culprit-cpu').value),
      MaxRestartsPerHour: parseInt(document.getElementById('cfg-rec-max-hour').value, 10),
      MaxRestartsPerDay: parseInt(document.getElementById('cfg-rec-max-day').value, 10),
      CooldownMinutes: parseInt(document.getElementById('cfg-rec-cooldown').value, 10),
      Confirmed: confirmed
    };

    try {
      const res = await fetch('/api/config/recovery', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
      });
      if (res.ok) {
        alert('Automatic recovery settings saved successfully.');
      } else {
        const err = await res.json();
        alert(`Error: ${err.Message || 'Failed to save settings'}`);
      }
    } catch (e) {
      alert(`Error saving recovery settings: ${e.message}`);
    }
  }
};
