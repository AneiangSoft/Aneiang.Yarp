/**
 * Dashboard Deployment Page
 * Displays current startup mode, listening endpoints, hot-reload status, and config snapshots.
 * Fetches data from /api/deployment/summary.
 *
 * Uses native DOM APIs (no jQuery) to match project convention.
 */
(function () {
    'use strict';

    const DashboardDeployment = {
        state: {
            summary: null,
            uptimeTimer: null,
            snapshotModal: null
        },

        // ─── Initialization ─────────────────────────────────────────
        init: function () {
            this.refresh();
            this.state.uptimeTimer = setInterval(() => this.updateUptime(), 1000);
        },

        // ─── Data Loading ──────────────────────────────────────────
        refresh: async function () {
            try {
                // Use DashboardApi (respects basePath + auth) when available
                let data;
                if (window.DashboardApi && typeof window.DashboardApi.get === 'function') {
                    data = await window.DashboardApi.get('/api/deployment/summary');
                } else {
                    const res = await fetch((window.__dashboard && window.__dashboard.basePath || '') + '/api/deployment/summary',
                        { cache: 'no-store' });
                    if (!res.ok) {
                        console.warn('Deployment summary API returned', res.status);
                        return;
                    }
                    data = await res.json();
                }
                this.state.summary = data;
                this.render(data);
            } catch (err) {
                console.error('Deployment refresh failed', err);
            }
        },

        // ─── Rendering ─────────────────────────────────────────────
        render: function (data) {
            this.setText('deployment-mode-name', data.mode);
            this.setText('deployment-mode-desc', this._modeDescription(data.mode));
            this._setModeColor(data.mode);

            this.setText('deployment-process-start', new Date(data.processStart).toLocaleString());
            this.setText('deployment-uptime', this._formatUptime(data.uptimeSeconds));
            this.setText('deployment-version', data.version);
            this.setText('deployment-env', data.environment);

            this.setHtml('deployment-hotreload', data.hotReload.enabled
                ? '<span class="badge bg-success">' + this._escape(__('deployment.enabled')) + '</span>'
                : '<span class="badge bg-secondary">' + this._escape(__('deployment.disabled')) + '</span>');
            this.setHtml('deployment-healthcheck', data.healthCheck.enabled
                ? '<span class="badge bg-success">' + this._escape(__('deployment.enabled')) + '</span>'
                : '<span class="badge bg-secondary">' + this._escape(__('deployment.disabled')) + '</span>');

            this.setHtml('deployment-hotreload-enabled', data.hotReload.enabled
                ? '<span class="text-success">' + this._escape(__('deployment.yes')) + '</span>'
                : '<span class="text-muted">' + this._escape(__('deployment.no')) + '</span>');
            this.setText('deployment-watched-files', (data.hotReload.watchedFiles || []).join(', ') || '-');
            this.setText('deployment-debounce', data.hotReload.debounceMs + ' ms');
            this.setText('deployment-fallback', data.hotReload.fallbackPollSeconds + ' s');
            this.setHtml('deployment-rollback', data.hotReload.rollbackOnFailure
                ? '<span class="text-success">' + this._escape(__('deployment.yes')) + '</span>'
                : '<span class="text-muted">' + this._escape(__('deployment.no')) + '</span>');

            this.setHtml('deployment-health-enabled', data.healthCheck.enabled
                ? '<span class="text-success">' + this._escape(__('deployment.yes')) + '</span>'
                : '<span class="text-muted">' + this._escape(__('deployment.no')) + '</span>');
            this.setText('deployment-health-paths',
                data.healthCheck.livePath + ', ' +
                data.healthCheck.readyPath + ', ' +
                data.healthCheck.path);
            const auths = (data.healthCheck.authentication || []);
            this.setText('deployment-health-auth', auths.length ? auths.map(this._translateAuth).join(' + ') : __('deployment.noRestrictions'));
            const checks = (data.healthCheck.checks || []);
            this.setText('deployment-health-checks', checks.length ? checks.map(this._translateCheck).join(' + ') : __('deployment.checks.none'));

            this.renderEndpoints(data.endpoints || []);
            this.renderSnapshots(data.snapshots || []);
        },

        renderEndpoints: function (endpoints) {
            const tbody = this.getEl('deployment-endpoints-body');
            if (!tbody) return;
            tbody.innerHTML = '';
            this.setText('deployment-endpoint-count', String(endpoints.length));

            if (endpoints.length === 0) {
                const tr = document.createElement('tr');
                tr.innerHTML = '<td colspan="6" class="text-center text-muted py-4">' + this._escape(__('deployment.unknown')) + '</td>';
                tbody.appendChild(tr);
                this._showSecurityWarnings([]);
                return;
            }

            const publicDashboard = [], publicAdmin = [], publicHealth = [];

            endpoints.forEach(ep => {
                const tr = document.createElement('tr');
                tr.innerHTML =
                    '<td><code>' + this._escape(ep.name) + '</code></td>' +
                    '<td><code>' + this._escape(ep.address) + '</code></td>' +
                    '<td>' + ep.port + '</td>' +
                    '<td>' + this._roleBadge(ep.role) + '</td>' +
                    '<td>' + (ep.isPublic
                        ? '<span class="badge bg-warning text-dark">' + this._escape(__('deployment.publicYes')) + '</span>'
                        : '<span class="badge bg-light text-dark">' + this._escape(__('deployment.publicNo')) + '</span>') + '</td>' +
                    '<td><span class="badge bg-success">' + this._escape(__('deployment.listening')) + '</span></td>';
                tbody.appendChild(tr);

                if (ep.isPublic) {
                    if (ep.role === 'Dashboard') publicDashboard.push(ep);
                    else if (ep.role === 'Admin') publicAdmin.push(ep);
                    else if (ep.role === 'Health') publicHealth.push(ep);
                }
            });

            const warnings = [];
            if (publicDashboard.length > 0) {
                warnings.push({ level: 'danger', message: __('deployment.security.dashboardPublic') + ' (' + publicDashboard.map(e => e.address + ':' + e.port).join(', ') + ')' });
            }
            if (publicAdmin.length > 0) {
                warnings.push({ level: 'danger', message: __('deployment.security.adminPublic') + ' (' + publicAdmin.map(e => e.address + ':' + e.port).join(', ') + ')' });
            }
            if (publicHealth.length > 0) {
                warnings.push({ level: 'warning', message: __('deployment.security.healthPublic') + ' (' + publicHealth.map(e => e.address + ':' + e.port).join(', ') + ')' });
            }
            this._showSecurityWarnings(warnings);
        },

        renderSnapshots: function (snapshots) {
            const tbody = this.getEl('deployment-snapshots-body');
            if (!tbody) return;
            tbody.innerHTML = '';
            this.setText('deployment-snapshot-count', String(snapshots.length));

            if (snapshots.length === 0) {
                const tr = document.createElement('tr');
                tr.innerHTML = '<td colspan="4" class="text-center text-muted py-4">' + this._escape(__('deployment.noSnapshots')) + '</td>';
                tbody.appendChild(tr);
                return;
            }

            snapshots.forEach(s => {
                const date = new Date(s.timestamp).toLocaleString();
                const fileName = (s.filePath || '').split(/[/\\]/).pop();
                const safeTimestamp = this._escape(s.timestamp);

                const tr = document.createElement('tr');
                tr.innerHTML =
                    '<td>' + date + '</td>' +
                    '<td><span class="badge bg-light text-dark">' + this._escape(s.trigger || '-') + '</span></td>' +
                    '<td><code>' + this._escape(fileName || '-') + '</code></td>' +
                    '<td><button class="btn btn-sm btn-outline-primary" data-action="view" data-ts="' + safeTimestamp + '">' +
                        '<i class="bi bi-eye"></i> ' + this._escape(__('deployment.viewDetail')) +
                    '</button></td>';
                tbody.appendChild(tr);
            });

            // Wire up handlers
            tbody.querySelectorAll('button[data-action="view"]').forEach(btn => {
                btn.addEventListener('click', (e) => {
                    const ts = e.currentTarget.getAttribute('data-ts');
                    this.viewSnapshot(ts);
                });
            });
        },

        _showSecurityWarnings: function (warnings) {
            const card = this.getEl('deployment-security-card');
            const container = this.getEl('deployment-security-warnings');
            if (!card || !container) return;
            container.innerHTML = '';
            if (!warnings || warnings.length === 0) {
                card.style.display = 'none';
                return;
            }
            warnings.forEach(w => {
                const icon = w.level === 'danger' ? 'bi-exclamation-triangle-fill' : 'bi-exclamation-circle-fill';
                const color = w.level === 'danger' ? '#ef4444' : '#f59e0b';
                const div = document.createElement('div');
                div.className = 'd-flex align-items-start gap-2 py-2';
                div.innerHTML =
                    '<i class="bi ' + icon + '" style="color:' + color + ';font-size:18px;"></i>' +
                    '<div>' + this._escape(w.message) + '</div>';
                container.appendChild(div);
            });
            card.style.display = '';
        },

        // ─── Actions ───────────────────────────────────────────────
        reload: function () {
            if (window.DashboardUtils && typeof window.DashboardUtils.toast === 'function') {
                window.DashboardUtils.toast(__('deployment.reloadSuccess') + __('deployment.pageRefresh'), 'success');
            }
            setTimeout(() => location.reload(), 500);
        },

        takeSnapshot: function () {
            if (window.DashboardUtils && typeof window.DashboardUtils.toast === 'function') {
                window.DashboardUtils.toast(__('deployment.snapshotSuccess') + __('deployment.placeholderSuffix'), 'info');
            }
        },

        checkHealth: async function () {
            try {
                const res = await fetch((window.__dashboard && window.__dashboard.basePath || '') + '/health',
                    { cache: 'no-store' });
                if (res.ok) {
                    if (window.DashboardUtils && typeof window.DashboardUtils.toast === 'function') {
                        window.DashboardUtils.toast(__('deployment.healthOk'), 'success');
                    }
                } else {
                    if (window.DashboardUtils && typeof window.DashboardUtils.toast === 'function') {
                        window.DashboardUtils.toast(__('deployment.healthFailed') + ' (' + res.status + ')', 'error');
                    }
                }
            } catch (err) {
                if (window.DashboardUtils && typeof window.DashboardUtils.toast === 'function') {
                    window.DashboardUtils.toast(__('deployment.healthFailed'), 'error');
                }
            }
        },

        viewSnapshot: async function (timestamp) {
            let data;
            try {
                if (window.DashboardApi && typeof window.DashboardApi.get === 'function') {
                    data = await window.DashboardApi.get('/api/deployment/snapshots/' + encodeURIComponent(timestamp));
                } else {
                    const res = await fetch((window.__dashboard && window.__dashboard.basePath || '') +
                        '/api/deployment/snapshots/' + encodeURIComponent(timestamp));
                    if (res.status === 404) {
                        this._showSnapshotNotFound();
                        return;
                    }
                    if (!res.ok) {
                        if (window.DashboardUtils && typeof window.DashboardUtils.toast === 'function') {
                            window.DashboardUtils.toast(__('deployment.snapshotFailed'), 'error');
                        }
                        return;
                    }
                    data = await res.json();
                }
            } catch (err) {
                // 404 / "not found" is expected when snapshot no longer exists
                if (err && /not\s*found/i.test(String(err.message || ''))) {
                    this._showSnapshotNotFound();
                    return;
                }
                if (window.DashboardUtils && typeof window.DashboardUtils.toast === 'function') {
                    window.DashboardUtils.toast(__('deployment.snapshotFailed') + ': ' + (err && err.message || ''), 'error');
                }
                return;
            }
            this.setText('snapshot-detail-content', JSON.stringify(data, null, 2));
            if (!this.state.snapshotModal) {
                const modalEl = document.getElementById('snapshot-detail-modal');
                if (modalEl && window.bootstrap) {
                    this.state.snapshotModal = new window.bootstrap.Modal(modalEl);
                }
            }
            if (this.state.snapshotModal) this.state.snapshotModal.show();
        },

        _showSnapshotNotFound: function () {
            this.setText('snapshot-detail-content', __('deployment.snapshotNotFound'));
            if (!this.state.snapshotModal) {
                const modalEl = document.getElementById('snapshot-detail-modal');
                if (modalEl && window.bootstrap) {
                    this.state.snapshotModal = new window.bootstrap.Modal(modalEl);
                }
            }
            if (this.state.snapshotModal) this.state.snapshotModal.show();
        },

        updateUptime: function () {
            const el = this.getEl('deployment-uptime');
            if (!el || !this.state.summary) return;
            const seconds = Math.floor((Date.now() - new Date(this.state.summary.processStart).getTime()) / 1000);
            el.textContent = this._formatUptime(seconds);
        },

        // ─── DOM helpers ───────────────────────────────────────────
        getEl: function (id) {
            return document.getElementById(id);
        },

        setText: function (id, text) {
            const el = this.getEl(id);
            if (el) el.textContent = text == null ? '' : text;
        },

        setHtml: function (id, html) {
            const el = this.getEl(id);
            if (el) el.innerHTML = html;
        },

        // ─── String helpers ────────────────────────────────────────
        _modeDescription: function (mode) {
            const key = 'deployment.mode.' + (mode || '').toLowerCase();
            const translated = __(key);
            return translated !== key ? translated : (mode || '');
        },

        _setModeColor: function (mode) {
            const colors = {
                'AllInOne': '#6366f1',
                'Split': '#22c55e',
                'ProxyOnly': '#0ea5e9',
                'DashboardOnly': '#f59e0b',
                'Auto': '#94a3b8'
            };
            const card = this.getEl('deployment-mode-card');
            if (card) card.style.borderLeftColor = colors[mode] || '#94a3b8';
        },

        // Map server-returned authentication label to i18n key
        _translateAuth: function (auth) {
            const key = (auth || '').toLowerCase();
            if (key === 'token') return __('deployment.tokenAuth');
            if (key === 'ip whitelist' || key === 'ipwhitelist') return __('deployment.ipWhitelist');
            return auth || '';
        },

        // Map server-returned check label to i18n key
        _translateCheck: function (check) {
            const key = (check || '').toLowerCase();
            if (key === 'database') return __('deployment.checks.db');
            if (key === 'config') return __('deployment.checks.config');
            if (key === 'database + config' || key === 'database+config' || key === 'dbconfig') return __('deployment.checks.dbConfig');
            return check || '';
        },

        _roleBadge: function (role) {
            const colors = {
                'Proxy': 'bg-primary',
                'Dashboard': 'bg-info',
                'Admin': 'bg-warning text-dark',
                'Health': 'bg-success',
                'All': 'bg-secondary'
            };
            const key = 'deployment.role.' + (role || '').toLowerCase();
            const label = __(key);
            return '<span class="badge ' + (colors[role] || 'bg-secondary') + '">' + this._escape(label) + '</span>';
        },

        _formatUptime: function (seconds) {
            seconds = Math.floor(seconds || 0);
            const days = Math.floor(seconds / 86400);
            const hours = Math.floor((seconds % 86400) / 3600);
            const mins = Math.floor((seconds % 3600) / 60);
            const secs = seconds % 60;
            const parts = [];
            if (days > 0) parts.push(days + 'd');
            if (hours > 0) parts.push(hours + 'h');
            if (mins > 0) parts.push(mins + 'm');
            parts.push(secs + 's');
            return parts.join(' ');
        },

        _escape: function (s) {
            if (s == null) return '';
            return String(s)
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;')
                .replace(/'/g, '&#39;');
        }
    };

    function __(key) {
        // Prefer the global I18N dictionary declared by _DashboardLayout
        if (typeof window.I18N === 'object' && window.I18N && window.I18N[key]) {
            return window.I18N[key];
        }
        // Fallback to dashboard config
        if (window.__dashboard && window.__dashboard.I18N && window.__dashboard.I18N[key]) {
            return window.__dashboard.I18N[key];
        }
        // Fallback to DashboardI18n module (if it provides translations)
        if (window.DashboardI18n && window.DashboardI18n.translations && window.DashboardI18n.translations[key]) {
            return window.DashboardI18n.translations[key];
        }
        return key;
    }

    window.DashboardDeployment = DashboardDeployment;

    // Init on DOM ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { DashboardDeployment.init(); });
    } else {
        DashboardDeployment.init();
    }
})();
