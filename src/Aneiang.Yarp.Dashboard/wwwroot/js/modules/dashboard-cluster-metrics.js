/**
 * Cluster Metrics Module - Per-cluster statistics
 */
(function() {
    'use strict';

    var ClusterMetricsModule = {
        initialized: false,
        autoRefreshInterval: null,
        init: function() { if (this.initialized) return; this.initialized = true; },
        destroy: function() { if (this.autoRefreshInterval) { clearInterval(this.autoRefreshInterval); this.autoRefreshInterval = null; } this.initialized = false; },

        load: async function() {
            var container = document.getElementById('cm-content');
            if (!container) return;
            try {
                container.innerHTML = '<div class="loading-state"><div class="loading-spinner"></div><div class="loading-text">' + __('common.loading') + '</div></div>';
                var plugin = await window.DashboardApi.getPlugin('cluster-metrics');
                var clusters = await window.DashboardApi.getClusters().catch(function() { return []; });
                this.render(plugin, clusters, container);
                this.updateRefreshTime();
            } catch (error) {
                container.innerHTML = '<div class="alert alert-danger">' + __('common.loadFailed') + ': ' + (error.message || error) + '</div>';
            }
        },

        render: function(plugin, clusters, container) {
            var enabled = plugin && plugin.enabled;
            var html = '';
            html += '<div class="row mb-3">';
            html += '<div class="col-md-3"><div class="stat-mini-card"><div class="stat-mini-value ' + (enabled ? 'text-success' : 'text-muted') + '">' + (enabled ? __('common.enabled') : __('common.disabled')) + '</div><div class="stat-mini-label">' + __('common.status') + '</div></div></div>';
            html += '<div class="col-md-3"><div class="stat-mini-card"><div class="stat-mini-value">' + (plugin ? plugin.version || '1.0' : '-') + '</div><div class="stat-mini-label">' + __('common.version') + '</div></div></div>';
            html += '<div class="col-md-3"><div class="stat-mini-card"><div class="stat-mini-value">' + (Array.isArray(clusters) ? clusters.length : 0) + '</div><div class="stat-mini-label">' + __('clusterMetrics.totalClusters') + '</div></div></div>';
            html += '<div class="col-md-3"><div class="stat-mini-card"><div class="stat-mini-value">' + (plugin && plugin.isBuiltIn ? __('common.builtIn') : __('common.external')) + '</div><div class="stat-mini-label">' + __('common.type') + '</div></div></div>';
            html += '</div>';
            html += '<div class="table-responsive"><table class="table table-hover"><thead><tr><th>' + __('clusterMetrics.clusterId') + '</th><th>' + __('clusterMetrics.destCount') + '</th><th>' + __('clusterMetrics.healthStatus') + '</th><th>' + __('clusterMetrics.loadBalancing') + '</th></tr></thead><tbody>';
            if (Array.isArray(clusters) && clusters.length > 0) {
                clusters.forEach(function(c) {
                    var destCount = c.destinations ? c.destinations.length : 0;
                    var health = c.healthCheck ? (c.healthCheck.enabled ? __('common.enabled') : __('common.disabled')) : __('clusterMetrics.notConfigured');
                    var lb = c.loadBalancingPolicy || 'PowerOfTwoChoices';
                    html += '<tr><td><code>' + (c.id || c.clusterId || '-') + '</code></td>';
                    html += '<td>' + destCount + '</td>';
                    html += '<td>' + health + '</td>';
                    html += '<td>' + lb + '</td></tr>';
                });
            } else {
                html += '<tr><td colspan="4" class="text-center text-muted py-3">' + __('clusterMetrics.noData') + '</td></tr>';
            }
            html += '</tbody></table></div>';
            container.innerHTML = html;
        },

        updateRefreshTime: function() { var el = document.getElementById('cm-refresh-time'); if (el) el.textContent = new Date().toLocaleTimeString(); }
    };

    window.ClusterMetricsModule = ClusterMetricsModule;
})();
