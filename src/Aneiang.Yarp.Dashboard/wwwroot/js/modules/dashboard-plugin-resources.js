/**
 * Plugin Resources Module — Enterprise resource monitoring
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
            var esc = escapeHtml;

            var html = '';

            // --- KPI strip: 5 metrics (no memory) ---
            var kpis = [
                { label: __('resources.totalPlugins'), value: totals.totalPlugins || 0 },
                { label: __('common.enabled'), value: totals.enabledPlugins || 0, accent: true },
                { label: __('resources.totalRequests'), value: formatNum(totals.totalRequestCount || 0) },
                { label: __('resources.totalErrors'), value: formatNum(totals.totalErrorCount || 0), danger: (totals.totalErrorCount || 0) > 0 },
                { label: __('resources.totalAvgLatency'), value: ((totals.averageLatencyMs || 0).toFixed(1)) + 'ms' }
            ];
            html += '<div class="pr-kpi-strip">';
            kpis.forEach(function(k) {
                var cls = 'pr-kpi-num';
                if (k.danger) cls += ' pr-kpi-num--danger';
                else if (k.accent) cls += ' pr-kpi-num--accent';
                html += '<div class="pr-kpi-item">' +
                    '<div class="' + cls + '">' + k.value + '</div>' +
                    '<div class="pr-kpi-tag">' + k.label + '</div>' +
                '</div>';
            });
            html += '</div>';

            // --- Plugin cards ---
            if (items.length === 0) {
                html += '<div class="pr-empty"><i class="bi bi-inbox"></i><p>' + __('resources.noData') + '</p></div>';
            } else {
                items.sort(function(a, b) {
                    if (a.enabled !== b.enabled) return a.enabled ? -1 : 1;
                    return (b.requestCount || 0) - (a.requestCount || 0);
                });

                html += '<div class="pr-card-grid">';
                items.forEach(function(item) {
                    html += renderPluginCard(item, esc);
                });
                html += '</div>';
            }

            container.innerHTML = html;
        },

        updateRefreshTime: function() {
            var el = document.getElementById('pr-refresh-time');
            if (el) el.textContent = new Date().toLocaleTimeString();
        }
    };

    function renderPluginCard(item, esc) {
        var health = getHealthInfo(item.overallHealth);
        var enabled = item.enabled;
        var typeLabel = item.isBuiltIn ? __('common.builtIn') : __('common.external');

        var html = '<div class="pr-card' + (enabled ? '' : ' pr-card--off') + '">';

        // Card header
        html += '<div class="pr-card-head">';
        html += '<div class="pr-card-title-group">';
        html += '<span class="pr-card-dot" style="background:' + health.color + ';" title="' + health.label + '"></span>';
        html += '<div>';
        html += '<div class="pr-card-name">' + esc(item.displayName || item.pluginId) + '</div>';
        html += '<div class="pr-card-id">' + esc(item.pluginId) + '</div>';
        html += '</div>';
        html += '</div>';
        html += '<div class="pr-card-tags">';
        html += '<span class="pr-tag">' + typeLabel + '</span>';
        if (enabled) {
            html += '<span class="pr-tag pr-tag--on">' + __('common.enabled') + '</span>';
        } else {
            html += '<span class="pr-tag pr-tag--off">' + __('common.disabled') + '</span>';
        }
        html += '</div>';
        html += '</div>';

        // Core metrics: 2×2 grid (requests, errors, latency, uptime)
        var hasTraffic = (item.requestCount || 0) > 0;
        var metrics = [
            { label: __('resources.requestCount'), value: formatNum(item.requestCount || 0) },
            { label: __('resources.errorCount'), value: formatNum(item.errorCount || 0), danger: (item.errorCount || 0) > 0 },
            { label: __('resources.avgLatency'), value: ((item.averageLatencyMs || 0).toFixed(1)) + 'ms' },
            { label: __('resources.uptime'), value: formatUptime(item.uptime) }
        ];
        html += '<div class="pr-metrics">';
        metrics.forEach(function(m) {
            var valClass = 'pr-metric-val';
            if (m.danger) valClass += ' pr-metric-val--danger';
            html += '<div class="pr-metric-cell">';
            html += '<div class="pr-metric-lbl">' + m.label + '</div>';
            html += '<div class="' + valClass + '">' + m.value + '</div>';
            html += '</div>';
        });
        html += '</div>';

        // No-traffic hint
        if (enabled && !hasTraffic) {
            html += '<div class="pr-hint"><i class="bi bi-info-circle"></i>' + __('resources.noTraffic') + '</div>';
        }

        // Custom statistics (inline, subtle) — plugins can report memory/resources here
        if (item.customStatistics && Object.keys(item.customStatistics).length > 0) {
            html += '<div class="pr-extras">';
            var parts = [];
            for (var key in item.customStatistics) {
                var displayKey = key;
                if (key === 'memoryBytes') displayKey = __('resources.memory');
                parts.push(displayKey + ': <strong>' + formatCustomValue(key, item.customStatistics[key]) + '</strong>');
            }
            html += parts.join('  ·  ');
            html += '</div>';
        }

        html += '</div>';
        return html;
    }

    function formatCustomValue(key, val) {
        if (key === 'memoryBytes' && typeof val === 'number') return formatBytes(val);
        if (typeof val === 'number' && val >= 1000) return formatNum(val);
        return String(val);
    }

    function formatBytes(bytes) {
        if (bytes === 0) return '0 B';
        var k = 1024;
        var sizes = ['B', 'KB', 'MB', 'GB'];
        var i = Math.floor(Math.log(bytes) / Math.log(k));
        return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
    }

    function formatNum(n) {
        if (n >= 1000000) return (n / 1000000).toFixed(1) + 'M';
        if (n >= 1000) return (n / 1000).toFixed(1) + 'K';
        return String(n);
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

    function getHealthInfo(health) {
        switch (health) {
            case 'Healthy': return { label: __('resources.healthy'), color: '#10b981' };
            case 'Degraded': return { label: __('resources.degraded'), color: '#f59e0b' };
            case 'Faulted': return { label: __('resources.faulted'), color: '#ef4444' };
            case 'Stopped': return { label: __('resources.stopped'), color: '#94a3b8' };
            default: return { label: health || '-', color: '#94a3b8' };
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
