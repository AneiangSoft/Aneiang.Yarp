/**
 * Plugin Resources Module - Resource usage statistics viewer
 */
(function() {
    'use strict';

    var PluginResourcesModule = {
        initialized: false,

        init: function() {
            if (this.initialized) return;
            this.initialized = true;
        },

        destroy: function() {
            this.initialized = false;
        },

        load: async function() {
            var container = document.getElementById('pr-content');
            if (!container) return;
            try {
                container.innerHTML = '<div class="loading-state"><div class="loading-spinner"></div><div class="loading-text">' + __('common.loading') + '</div></div>';
                var data = await window.DashboardApi.getPluginResources();
                this.render(data, container);
                this.updateRefreshTime();
            } catch (error) {
                container.innerHTML = '<div class="alert alert-danger">' + __('common.loadFailed') + ': ' + (error.message || error) + '</div>';
            }
        },

        render: function(data, container) {
            var items = (data && data.items) || [];
            var totals = (data && data.totals) || {};

            var html = '';

            // Totals summary
            html += '<div class="row mb-4">';
            html += '<div class="col-md-2"><div class="stat-mini-card"><div class="stat-mini-value text-primary">' + (totals.totalPlugins || 0) + '</div><div class="stat-mini-label">' + __('resources.totalPlugins') + '</div></div></div>';
            html += '<div class="col-md-2"><div class="stat-mini-card"><div class="stat-mini-value text-success">' + (totals.enabledPlugins || 0) + '</div><div class="stat-mini-label">' + __('common.enabled') + '</div></div></div>';
            html += '<div class="col-md-2"><div class="stat-mini-card"><div class="stat-mini-value">' + formatBytes(totals.totalMemoryBytes || 0) + '</div><div class="stat-mini-label">' + __('resources.totalMemory') + '</div></div></div>';
            html += '<div class="col-md-2"><div class="stat-mini-card"><div class="stat-mini-value">' + (totals.totalRequestCount || 0) + '</div><div class="stat-mini-label">' + __('resources.totalRequests') + '</div></div></div>';
            html += '<div class="col-md-2"><div class="stat-mini-card"><div class="stat-mini-value text-danger">' + (totals.totalErrorCount || 0) + '</div><div class="stat-mini-label">' + __('resources.totalErrors') + '</div></div></div>';
            html += '<div class="col-md-2"><div class="stat-mini-card"><div class="stat-mini-value">' + (totals.totalActiveResources || 0) + '</div><div class="stat-mini-label">' + __('resources.activeResources') + '</div></div></div>';
            html += '</div>';

            // Plugin table
            html += '<div class="table-responsive">';
            html += '<table class="table table-hover align-middle">';
            html += '<thead><tr><th>' + __('resources.plugin') + '</th><th>' + __('common.status') + '</th><th>' + __('common.type') + '</th><th>' + __('resources.memory') + '</th><th>' + __('resources.requestCount') + '</th><th>' + __('resources.errorCount') + '</th><th>' + __('resources.avgLatency') + '</th><th>' + __('resources.activeCol') + '</th><th>' + __('resources.health') + '</th><th>' + __('resources.uptime') + '</th></tr></thead>';
            html += '<tbody>';

            if (items.length > 0) {
                items.forEach(function(item) {
                    var healthBadge = getHealthBadge(item.overallHealth);
                    var statusBadge = item.enabled
                        ? '<span class="badge bg-success">' + __('common.enabled') + '</span>'
                        : '<span class="badge bg-secondary">' + __('common.disabled') + '</span>';
                    var typeBadge = item.isBuiltIn
                        ? '<span class="badge bg-info">' + __('common.builtIn') + '</span>'
                        : '<span class="badge bg-warning">' + __('common.external') + '</span>';

                    html += '<tr>';
                    html += '<td><strong>' + escapeHtml(item.displayName || item.pluginId) + '</strong><br><small class="text-muted">' + escapeHtml(item.pluginId) + '</small></td>';
                    html += '<td>' + statusBadge + '</td>';
                    html += '<td>' + typeBadge + '</td>';
                    html += '<td>' + formatBytes(item.memoryBytes || 0) + '</td>';
                    html += '<td>' + (item.requestCount || 0) + '</td>';
                    html += '<td>' + (item.errorCount || 0) + '</td>';
                    html += '<td>' + ((item.averageLatencyMs || 0).toFixed(1)) + 'ms</td>';
                    html += '<td>' + (item.activeResources || 0) + '/' + (item.totalResources || 0) + '</td>';
                    html += '<td>' + healthBadge + '</td>';
                    html += '<td>' + formatUptime(item.uptime) + '</td>';
                    html += '</tr>';
                });
            } else {
                html += '<tr><td colspan="10" class="text-center text-muted py-3">' + __('resources.noData') + '</td></tr>';
            }

            html += '</tbody></table></div>';

            // Custom statistics
            var hasCustomStats = items.some(function(item) { return item.customStatistics && Object.keys(item.customStatistics).length > 0; });
            if (hasCustomStats) {
                html += '<h6 class="mt-4 mb-2">' + __('resources.customStats') + '</h6>';
                html += '<div class="table-responsive"><table class="table table-sm"><thead><tr><th>' + __('resources.plugin') + '</th><th>' + __('resources.metricKey') + '</th><th>' + __('resources.metricValue') + '</th></tr></thead><tbody>';
                items.forEach(function(item) {
                    if (item.customStatistics) {
                        for (var key in item.customStatistics) {
                            html += '<tr><td>' + escapeHtml(item.displayName || item.pluginId) + '</td><td>' + escapeHtml(key) + '</td><td>' + item.customStatistics[key] + '</td></tr>';
                        }
                    }
                });
                html += '</tbody></table></div>';
            }

            container.innerHTML = html;
        },

        updateRefreshTime: function() {
            var el = document.getElementById('pr-refresh-time');
            if (el) el.textContent = new Date().toLocaleTimeString();
        }
    };

    function formatBytes(bytes) {
        if (bytes === 0) return '0 B';
        var k = 1024;
        var sizes = ['B', 'KB', 'MB', 'GB'];
        var i = Math.floor(Math.log(bytes) / Math.log(k));
        return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
    }

    function formatUptime(timespan) {
        if (!timespan) return '-';
        var match = String(timespan).match(/^(?:(\d+)\.)?(\d{1,2}):(\d{2}):(\d{2})/);
        if (!match) return '-';
        var days = Number(match[1] || 0);
        var hours = Number(match[2] || 0);
        var minutes = Number(match[3] || 0);
        if (days > 0) return days + 'd ' + hours + 'h';
        if (hours > 0) return hours + 'h ' + minutes + 'm';
        return minutes + 'm';
    }

    function getHealthBadge(health) {
        switch (health) {
            case 'Healthy': return '<span class="badge bg-success">' + __('resources.healthy') + '</span>';
            case 'Degraded': return '<span class="badge bg-warning">' + __('resources.degraded') + '</span>';
            case 'Faulted': return '<span class="badge bg-danger">' + __('resources.faulted') + '</span>';
            case 'Stopped': return '<span class="badge bg-secondary">' + __('resources.stopped') + '</span>';
            default: return '<span class="badge bg-light text-dark">' + (health || '-') + '</span>';
        }
    }

    function escapeHtml(str) {
        if (!str) return '';
        return String(str).replace(/[&<>"']/g, function(c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }

    window.PluginResourcesModule = PluginResourcesModule;
})();
