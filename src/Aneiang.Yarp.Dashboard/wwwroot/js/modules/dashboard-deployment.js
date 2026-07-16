/**
 * Dashboard Deployment Page
 * Displays current startup mode, listening endpoints, and health check status.
 * Fetches data from /api/deployment/summary.
 *
 * Uses native DOM APIs (no jQuery) to match project convention.
 */
(function () {
    'use strict';

    const DashboardDeployment = {
        state: {
            summary: null,
            uptimeTimer: null
        },

        init: function () {
            this.refresh();
            this.state.uptimeTimer = setInterval(() => this.updateUptime(), 1000);
        },

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
                    // Unwrap ApiResponse<T> format: { code, success, data }
                    if (data && typeof data === 'object' && 'code' in data && data.data) {
                        data = data.data;
                    }
                }
                this.state.summary = data;
                this.render(data);
            } catch (err) {
                console.error('Deployment refresh failed', err);
            }
        },

        render: function (data) {
            this.setText('deployment-mode-name', data.mode);
            this.setText('deployment-mode-desc', this._modeDescription(data.mode));
            this._setModeColor(data.mode);

            this.setText('deployment-process-start', new Date(data.processStart).toLocaleString());
            this.setText('deployment-uptime', this._formatUptime(data.uptimeSeconds));
            this.setText('deployment-version', data.version);
            this.setText('deployment-env', data.environment);

            this.setHtml('deployment-healthcheck', data.healthCheck.enabled
                ? '<span class="badge bg-success">' + this._escape(__('deployment.enabled')) + '</span>'
                : '<span class="badge bg-secondary">' + this._escape(__('deployment.disabled')) + '</span>');

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
            this._showRestartRequired(data);
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

        _showRestartRequired: function (data) {
            if (!data || !data.restartRequired) return;
            const card = this.getEl('deployment-security-card');
            const container = this.getEl('deployment-security-warnings');
            if (!card || !container) return;

            const reasons = data.restartReasons || [];
            const messages = reasons.length
                ? reasons.map(r => (r.title || 'Restart required') + ': ' + (r.message || r.configPath || r.key))
                : [__('deployment.restartRequired')];

            messages.forEach(message => {
                const div = document.createElement('div');
                div.className = 'd-flex align-items-start gap-2 py-2';
                div.innerHTML = '<i class="bi bi-arrow-clockwise" style="color:#f59e0b;font-size:18px;"></i>' +
                    '<div>' + this._escape(message) + '</div>';
                container.appendChild(div);
            });
            card.style.display = '';
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

        updateUptime: function () {
            const el = this.getEl('deployment-uptime');
            if (!el || !this.state.summary) return;
            const seconds = Math.floor((Date.now() - new Date(this.state.summary.processStart).getTime()) / 1000);
            el.textContent = this._formatUptime(seconds);
        },

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

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { DashboardDeployment.init(); });
    } else {
        DashboardDeployment.init();
    }
})();
