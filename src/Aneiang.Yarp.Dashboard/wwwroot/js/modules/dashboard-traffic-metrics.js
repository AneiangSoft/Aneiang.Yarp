/**
 * Traffic Metrics Module - Per-plugin traffic statistics
 */
(function() {
    'use strict';

    var TrafficMetricsModule = {
        initialized: false,
        autoRefreshInterval: null,
        init: function() { if (this.initialized) return; this.initialized = true; },
        destroy: function() { if (this.autoRefreshInterval) { clearInterval(this.autoRefreshInterval); this.autoRefreshInterval = null; } this.initialized = false; },

        load: async function() {
            var container = document.getElementById('tm-content');
            if (!container) return;
            try {
                container.innerHTML = '<div class="loading-state"><div class="loading-spinner"></div><div class="loading-text">' + __('common.loading') + '</div></div>';
                var plugin = await window.DashboardApi.getPlugin('traffic-metrics');
                var trafficData = await window.DashboardApi.getTrafficData(5);
                this.render(plugin, trafficData, container);
                this.updateRefreshTime();
            } catch (error) {
                container.innerHTML = '<div class="alert alert-danger">' + __('common.loadFailed') + ': ' + (error.message || error) + '</div>';
            }
        },

        render: function(plugin, trafficData, container) {
            var enabled = plugin && plugin.enabled;
            var html = '';
            html += '<div class="row mb-3">';
            html += '<div class="col-md-3"><div class="stat-mini-card"><div class="stat-mini-value ' + (enabled ? 'text-success' : 'text-muted') + '">' + (enabled ? __('common.enabled') : __('common.disabled')) + '</div><div class="stat-mini-label">' + __('common.status') + '</div></div></div>';
            html += '<div class="col-md-3"><div class="stat-mini-card"><div class="stat-mini-value">' + (plugin ? plugin.version || '1.0' : '-') + '</div><div class="stat-mini-label">' + __('common.version') + '</div></div></div>';
            html += '<div class="col-md-3"><div class="stat-mini-card"><div class="stat-mini-value">' + (trafficData && trafficData.totalRequests != null ? trafficData.totalRequests : '-') + '</div><div class="stat-mini-label">' + __('trafficMetrics.totalRequests5min') + '</div></div></div>';
            html += '<div class="col-md-3"><div class="stat-mini-card"><div class="stat-mini-value">' + (trafficData && trafficData.avgLatencyMs != null ? trafficData.avgLatencyMs.toFixed(1) + 'ms' : '-') + '</div><div class="stat-mini-label">' + __('trafficMetrics.avgLatency') + '</div></div></div>';
            html += '</div>';

            // Status code distribution
            if (trafficData && trafficData.statusCodes) {
                html += '<h6 class="mb-2">' + __('trafficMetrics.statusCodes') + '</h6>';
                html += '<div class="row mb-3">';
                var codes = trafficData.statusCodes;
                for (var key in codes) {
                    html += '<div class="col-md-2"><div class="stat-mini-card"><div class="stat-mini-value">' + codes[key] + '</div><div class="stat-mini-label">' + key + '</div></div></div>';
                }
                html += '</div>';
            }

            // Route breakdown
            if (trafficData && trafficData.topRoutes && trafficData.topRoutes.length > 0) {
                html += '<div class="table-responsive"><table class="table table-hover"><thead><tr><th>' + __('trafficMetrics.routeId') + '</th><th>' + __('trafficMetrics.requestCount') + '</th><th>' + __('trafficMetrics.avgLatency') + '</th></tr></thead><tbody>';
                trafficData.topRoutes.forEach(function(r) {
                    html += '<tr><td><code>' + (r.name || '-') + '</code></td>';
                    html += '<td>' + (r.count || 0) + '</td>';
                    html += '<td>' + ((r.avgLatency || 0).toFixed(1)) + 'ms</td></tr>';
                });
                html += '</tbody></table></div>';
            } else {
                html += '<div class="text-center text-muted py-3">' + __('trafficMetrics.noData') + ' ' + __('trafficMetrics.noDataHint') + '</div>';
            }

            container.innerHTML = html;
        },

        updateRefreshTime: function() { var el = document.getElementById('tm-refresh-time'); if (el) el.textContent = new Date().toLocaleTimeString(); }
    };

    window.TrafficMetricsModule = TrafficMetricsModule;
})();
