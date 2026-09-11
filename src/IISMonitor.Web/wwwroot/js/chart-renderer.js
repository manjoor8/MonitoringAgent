/**
 * Self-contained canvas chart renderer for IISMonitor.
 * Zero external CDN or internet dependencies.
 */
class TimelineChart {
  constructor(canvasId) {
    this.canvas = document.getElementById(canvasId);
    if (!this.canvas) return;
    this.ctx = this.canvas.getContext('2d');
    this.data = [];
    this.series = [];
    this.thresholds = [
      { value: 70, color: '#eab308', label: 'Spike Threshold (70%)' },
      { value: 90, color: '#ef4444', label: 'Critical Threshold (90%)' }
    ];
    this.initResize();
  }

  initResize() {
    const resize = () => {
      if (!this.canvas) return;
      const rect = this.canvas.parentElement.getBoundingClientRect();
      this.canvas.width = rect.width * window.devicePixelRatio;
      this.canvas.height = rect.height * window.devicePixelRatio;
      this.ctx.scale(window.devicePixelRatio, window.devicePixelRatio);
      this.render();
    };
    window.addEventListener('resize', resize);
    setTimeout(resize, 50);
  }

  setData(dataPoints, seriesConfig) {
    this.data = dataPoints || [];
    this.series = seriesConfig || [
      { key: 'TotalCpu', label: 'Total CPU %', color: '#38bdf8', width: 2 }
    ];
    this.render();
  }

  render() {
    if (!this.canvas || !this.ctx) return;
    const ctx = this.ctx;
    const width = this.canvas.width / window.devicePixelRatio;
    const height = this.canvas.height / window.devicePixelRatio;

    ctx.clearRect(0, 0, width, height);

    const padding = { top: 20, right: 30, bottom: 30, left: 45 };
    const chartW = width - padding.left - padding.right;
    const chartH = height - padding.top - padding.bottom;

    if (this.data.length === 0) {
      ctx.fillStyle = '#64748b';
      ctx.font = '13px sans-serif';
      ctx.textAlign = 'center';
      ctx.fillText('Awaiting monitoring samples...', width / 2, height / 2);
      return;
    }

    // Grid lines & Y-axis (0 - 100%)
    ctx.strokeStyle = '#334155';
    ctx.lineWidth = 1;
    ctx.fillStyle = '#94a3b8';
    ctx.font = '11px monospace';
    ctx.textAlign = 'right';

    for (let pct = 0; pct <= 100; pct += 25) {
      const y = padding.top + chartH - (pct / 100) * chartH;
      ctx.beginPath();
      ctx.moveTo(padding.left, y);
      ctx.lineTo(padding.left + chartW, y);
      ctx.stroke();
      ctx.fillText(`${pct}%`, padding.left - 8, y + 4);
    }

    // Threshold indicator lines
    for (const t of this.thresholds) {
      const y = padding.top + chartH - (t.value / 100) * chartH;
      ctx.strokeStyle = t.color;
      ctx.setLineDash([4, 4]);
      ctx.beginPath();
      ctx.moveTo(padding.left, y);
      ctx.lineTo(padding.left + chartW, y);
      ctx.stroke();
      ctx.setLineDash([]);
    }

    // Draw series
    const stepX = chartW / Math.max(1, this.data.length - 1);

    for (const s of this.series) {
      ctx.strokeStyle = s.color;
      ctx.lineWidth = s.width || 2;
      ctx.beginPath();

      for (let i = 0; i < this.data.length; i++) {
        const x = padding.left + i * stepX;
        const val = Math.min(100, Math.max(0, this.data[i][s.key] ?? 0));
        const y = padding.top + chartH - (val / 100) * chartH;

        if (i === 0) ctx.moveTo(x, y);
        else ctx.lineTo(x, y);
      }

      ctx.stroke();
    }

    // X-axis timestamps
    ctx.fillStyle = '#64748b';
    ctx.textAlign = 'center';
    const sampleStep = Math.max(1, Math.floor(this.data.length / 5));

    for (let i = 0; i < this.data.length; i += sampleStep) {
      const item = this.data[i];
      const x = padding.left + i * stepX;
      let label = '';
      if (item.TimestampUtc) {
        const d = new Date(item.TimestampUtc);
        label = d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' });
      }
      ctx.fillText(label, x, height - 10);
    }
  }
}
