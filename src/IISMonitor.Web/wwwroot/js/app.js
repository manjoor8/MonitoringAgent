/**
 * SPA Router and application coordinator for IISMonitor.
 */
const App = {
  currentTab: 'dashboard',

  init() {
    this.bindNavigation();
    this.handleRoute();
    window.addEventListener('hashchange', () => this.handleRoute());
  },

  bindNavigation() {
    document.querySelectorAll('.nav-link').forEach(link => {
      link.addEventListener('click', e => {
        e.preventDefault();
        const target = link.getAttribute('data-tab');
        window.location.hash = target;
      });
    });
  },

  handleRoute() {
    const hash = window.location.hash.replace('#', '') || 'dashboard';
    const parts = hash.split('/');
    const tab = parts[0];

    document.querySelectorAll('.nav-link').forEach(l => l.classList.remove('active'));
    const activeLink = document.querySelector(`.nav-link[data-tab="${tab}"]`);
    if (activeLink) activeLink.classList.add('active');

    document.querySelectorAll('.tab-view').forEach(view => view.style.display = 'none');

    if (tab === 'incident' && parts[1]) {
      // Incident detail sub-route
      const detailView = document.getElementById('view-incident-detail');
      if (detailView) detailView.style.display = 'block';
      IncidentDetail.load(parts[1]);
    } else {
      const view = document.getElementById(`view-${tab}`);
      if (view) view.style.display = 'block';

      if (tab === 'dashboard') Dashboard.init();
      if (tab === 'incidents') Incidents.load();
      if (tab === 'config') Configuration.load();
    }
  },

  formatBytes(bytes) {
    if (!bytes || bytes === 0) return '0 B';
    const k = 1024;
    const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(1)) + ' ' + sizes[i];
  },

  formatDuration(seconds) {
    if (!seconds || seconds <= 0) return '0s';
    const m = Math.floor(seconds / 60);
    const s = Math.floor(seconds % 60);
    return m > 0 ? `${m}m ${s}s` : `${s}s`;
  },

  getStateBadge(state) {
    const s = (state || '').toLowerCase();
    if (s === 'normal') return '<span class="badge badge-normal">Normal</span>';
    if (s === 'incidentstarting') return '<span class="badge badge-warning">Starting</span>';
    if (s === 'highdetail') return '<span class="badge badge-warning">High Detail</span>';
    if (s === 'critical') return '<span class="badge badge-critical">Critical</span>';
    if (s === 'recovery') return '<span class="badge badge-recovery">Recovery</span>';
    return `<span class="badge badge-blue">${state}</span>`;
  }
};

document.addEventListener('DOMContentLoaded', () => App.init());
