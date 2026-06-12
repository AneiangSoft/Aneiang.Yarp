/**
 * Dashboard Health Check Module - Cluster health status viewer
 */
(function() {
    'use strict';

    var HealthCheckModule = {
        name: 'healthcheck',
        initialized: false,

        init: async function() {
            if (this.initialized) return;
            this.setupEvents();
            this.initialized = true;
            setTimeout(function() { HealthCheckModule.loadHealthStatus(); }, 0);
        },

        loadHealthStatus: async function() {
            try {
                var container = window.DashboardDOM.safe('#health-content');
                if (!container) return;

                window.DashboardDOM.showLoading(container, __('health.loading'));

                var [statusData, configsData] = await Promise.all([
                    window.DashboardApi.endpoints.getHealthCheckStatus(),
                    window.DashboardApi.endpoints.getClusterHealthConfigs()
                ]);

                // Use HealthCheckModule explicitly instead of this to avoid context issues
                HealthCheckModule.renderHealthStatus(statusData || [], configsData || [], container);
            } catch (error) {
                console.error('[HealthCheck] Load failed:', error);
                var container = window.DashboardDOM.safe('#health-content');
                if (container) window.DashboardDOM.showError(container, __('health.loadFailed'));
            }
        },

        renderHealthStatus: function(statusList, configList, container) {
            window.DashboardDOM.clear(container);

            if (!statusList || statusList.length === 0) {
                container.innerHTML =
                    '<div class="text-center py-5">' +
                        '<i class="bi bi-heart-pulse text-muted" style="font-size:48px;"></i>' +
                        '<p class="text-muted mt-3">' + __('health.noData') + '</p>' +
                        '<p class="text-muted small">' + __('health.noDataHelp') + '</p>' +
                    '</div>';
                return;
            }

            var configMap = {};
            if (configList && configList.forEach) {
                configList.forEach(function(c) { configMap[c.clusterId] = c; });
            }

            // Summary cards
            var healthyCount = 0, unhealthyCount = 0, unknownCount = 0;
            statusList.forEach(function(s) {
                var hc = s.healthCheck;
                if (!hc) { unknownCount++; return; }
                var hasActive = hc.active && hc.active.enabled;
                var hasPassive = hc.passive && hc.passive.enabled;
                if (hasActive || hasPassive) { healthyCount++; }
                else { unknownCount++; }
            });

            var html = '';
            html += '<div class="row g-3 mb-4">';
            html += '<div class="col-md-4"><div class="stat-mini-card"><div class="stat-mini-value" style="color:#22c55e">' + healthyCount + '</div><div class="stat-mini-label">' + __('health.monitoring') + '</div></div></div>';
            html += '<div class="col-md-4"><div class="stat-mini-card"><div class="stat-mini-value" style="color:#ef4444">' + unhealthyCount + '</div><div class="stat-mini-label">' + __('health.unhealthy') + '</div></div></div>';
            html += '<div class="col-md-4"><div class="stat-mini-card"><div class="stat-mini-value" style="color:#94a3b8">' + unknownCount + '</div><div class="stat-mini-label">' + __('health.unknown') + '</div></div></div>';
            html += '</div>';

            // Cluster list
            html += '<div class="table-responsive"><table class="table table-hover mb-0">';
            html += '<thead><tr>';
            html += '<th>' + __('health.cluster') + '</th>';
            html += '<th>' + __('health.destinations') + '</th>';
            html += '<th>' + __('health.activeCheck') + '</th>';
            html += '<th>' + __('health.passiveCheck') + '</th>';
            html += '<th>' + __('health.status') + '</th>';
            html += '</tr></thead><tbody>';

            statusList.forEach(function(s) {
                var hc = s.healthCheck;
                var activeEnabled = hc && hc.active && hc.active.enabled;
                var passiveEnabled = hc && hc.passive && hc.passive.enabled;
                var isMonitored = activeEnabled || passiveEnabled;

                var statusBadge = isMonitored
                    ? '<span class="badge bg-success"><i class="bi bi-check-circle-fill me-1"></i>' + __('health.enabled') + '</span>'
                    : '<span class="badge bg-secondary"><i class="bi bi-dash-circle me-1"></i>' + __('health.disabled') + '</span>';

                var activeDetail = '-';
                if (hc && hc.active) {
                    activeDetail = (hc.active.enabled ? '<span class="badge bg-success me-1">' + __('health.on') + '</span>' : '<span class="badge bg-secondary me-1">' + __('health.off') + '</span>');
                    if (hc.active.enabled) {
                        var parts = [];
                        if (hc.active.path) parts.push(hc.active.path);
                        if (hc.active.interval) parts.push(hc.active.interval);
                        if (hc.active.timeout) parts.push(__('health.timeout') + ': ' + hc.active.timeout);
                        if (hc.active.policy) parts.push(hc.active.policy);
                        activeDetail += '<small class="text-muted d-block mt-1">' + parts.join(' · ') + '</small>';
                    }
                }

                var passiveDetail = '-';
                if (hc && hc.passive) {
                    passiveDetail = (hc.passive.enabled ? '<span class="badge bg-success me-1">' + __('health.on') + '</span>' : '<span class="badge bg-secondary me-1">' + __('health.off') + '</span>');
                    if (hc.passive.enabled) {
                        var pParts = [];
                        if (hc.passive.policy) pParts.push(hc.passive.policy);
                        if (hc.passive.reactivationPeriod) pParts.push(__('health.reactivation') + ': ' + hc.passive.reactivationPeriod);
                        passiveDetail += '<small class="text-muted d-block mt-1">' + pParts.join(' · ') + '</small>';
                    }
                }

                html += '<tr class="cluster-row health-' + (isMonitored ? 'healthy' : 'unknown') + '">';
                html += '<td><strong>' + window.DashboardUtils.escapeHtml(s.clusterId) + '</strong></td>';
                html += '<td>' + (s.destinationCount || 0) + '</td>';
                html += '<td>' + activeDetail + '</td>';
                html += '<td>' + passiveDetail + '</td>';
                html += '<td>' + statusBadge + '</td>';
                html += '</tr>';
            });

            html += '</tbody></table></div>';

            container.innerHTML = html;
        },

        setupEvents: function() {
            document.addEventListener('dashboard:shortcut:refresh', function() { HealthCheckModule.loadHealthStatus(); });
            document.addEventListener('dashboard:localeChange', function() { HealthCheckModule.loadHealthStatus(); });
        }
    };

    if (window.DashboardApp) {
        window.DashboardApp.registerModule('healthcheck', HealthCheckModule);
    }
    window.HealthCheckModule = HealthCheckModule;
})();
