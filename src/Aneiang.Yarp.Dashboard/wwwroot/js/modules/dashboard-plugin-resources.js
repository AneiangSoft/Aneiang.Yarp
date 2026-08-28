/**
 * Plugin Resources Module — Enterprise resource monitoring
 */
(function() {
    'use strict';

    var TREND_METRICS = ['throughput', 'latency', 'errors'];
    var TREND_HISTORY_MAX = 60; // 60 × 5s = 5 minutes

    var PluginResourcesModule = {
        initialized: false,
        _autoRefreshTimer: null,
        _autoRefreshIntervalMs: 5000,
        // Rolling trend window: pluginId → { requests[], errors[], latency[] } (cumulative counters / instant values)
        _history: {},
        _lastData: null,
        _trendMetric: 'throughput',

        init: function() {
            if (this.initialized) return;
            this.initialized = true;
            try {
                var saved = localStorage.getItem('pr-trend-metric');
                if (TREND_METRICS.indexOf(saved) >= 0) this._trendMetric = saved;
            } catch (e) { /* ignore */ }
            this._bindTrendChips();
            this.updateTrendChips();
            this.startAutoRefresh();
        },

        destroy: function() {
            this.initialized = false;
            this.stopAutoRefresh();
        },

        startAutoRefresh: function() {
            this.stopAutoRefresh();
            this._autoRefreshTimer = setInterval(function() {
                // Skip while tab is hidden — avoids wasted polling and flicker.
                if (document.hidden) return;
                PluginResourcesModule.load({ silent: true });
            }, this._autoRefreshIntervalMs);
        },

        stopAutoRefresh: function() {
            if (this._autoRefreshTimer) {
                clearInterval(this._autoRefreshTimer);
                this._autoRefreshTimer = null;
            }
        },

        load: async function(options) {
            var container = document.getElementById('pr-content');
            if (!container) return;
            var silent = options && options.silent;
            try {
                if (!silent) {
                    container.innerHTML = '<div class="loading-state"><div class="loading-spinner"></div><div class="loading-text">' + __('common.loading') + '</div></div>';
                }
                var data = await window.DashboardApi.getPluginResources();
                this._recordHistory(data && data.items);
                this._lastData = data;
                this.render(data, container);
                this.updateRefreshTime();
            } catch (error) {
                if (!silent) {
                    container.innerHTML = '<div class="alert alert-danger">' + __('common.loadFailed') + ': ' + (error.message || error) + '</div>';
                }
            }
        },

        setTrendMetric: function(metric) {
            if (TREND_METRICS.indexOf(metric) < 0) return;
            this._trendMetric = metric;
            try { localStorage.setItem('pr-trend-metric', metric); } catch (e) { /* ignore */ }
            this.updateTrendChips();
            var container = document.getElementById('pr-content');
            if (container && this._lastData) this.render(this._lastData, container);
        },

        _bindTrendChips: function() {
            var bar = document.getElementById('pr-trend-chips');
            if (!bar || bar._bound) return;
            bar._bound = true;
            bar.addEventListener('click', function(e) {
                var btn = e.target.closest('[data-metric]');
                if (btn) PluginResourcesModule.setTrendMetric(btn.getAttribute('data-metric'));
            });
        },

        updateTrendChips: function() {
            var bar = document.getElementById('pr-trend-chips');
            if (!bar) return;
            bar.querySelectorAll('[data-metric]').forEach(function(btn) {
                btn.classList.toggle('pr-chip--active', btn.getAttribute('data-metric') === PluginResourcesModule._trendMetric);
            });
        },

        // Keep a rolling 5-minute window of counters; throughput/errors sparklines
        // are computed as per-interval deltas later (counters may reset on plugin restart → clamp to 0).
        _recordHistory: function(items) {
            var self = this;
            (items || []).forEach(function(item) {
                var h = self._history[item.pluginId];
                if (!h) {
                    h = { requests: [], errors: [], latency: [] };
                    self._history[item.pluginId] = h;
                }
                h.requests.push(item.requestCount || 0);
                h.errors.push(item.errorCount || 0);
                h.latency.push(item.averageLatencyMs || 0);
                if (h.requests.length > TREND_HISTORY_MAX) {
                    h.requests.shift();
                    h.errors.shift();
                    h.latency.shift();
                }
            });
        },

        render: function(data, container) {
            var items = (data && data.items) || [];
            var totals = (data && data.totals) || {};
            var esc = escapeHtml;

            var html = '';

            // --- KPI meta line: segmented pills with icons ---
            var kpis = [
                { icon: 'bi-grid-fill', label: __('resources.totalPlugins'), value: totals.totalPlugins || 0 },
                { icon: 'bi-check-circle', label: __('common.enabled'), value: totals.enabledPlugins || 0, tone: 'ok' },
                { icon: 'bi-arrow-repeat', label: __('resources.totalRequests'), value: formatNum(totals.totalRequestCount || 0) },
                { icon: 'bi-exclamation-triangle', label: __('resources.totalErrors'), value: formatNum(totals.totalErrorCount || 0), tone: (totals.totalErrorCount || 0) > 0 ? 'danger' : null },
                // API returns overallAverageLatencyMs (PluginResourceUsageTotals record);
                // averageLatencyMs kept as fallback for older payloads.
                { icon: 'bi-lightning', label: __('resources.totalAvgLatency'), value: ((totals.overallAverageLatencyMs || totals.averageLatencyMs || 0).toFixed(1)) + 'ms' }
            ];
            html += '<div class="pc-meta-line">';
            kpis.forEach(function(k) {
                var toneCls = k.tone ? ' pc-meta-item--' + k.tone : '';
                html += '<span class="pc-meta-item' + toneCls + '">' +
                    '<i class="bi ' + k.icon + '"></i>' +
                    '<span class="pc-meta-num">' + k.value + '</span>' +
                    '<span class="pc-meta-label">' + k.label + '</span>' +
                '</span>';
            });
            html += '</div>';

            // --- Plugin table (compact rows) ---
            if (items.length === 0) {
                html += '<div class="pr-empty"><i class="bi bi-inbox"></i><p>' + __('resources.noData') + '</p></div>';
            } else {
                items.sort(function(a, b) {
                    if (a.enabled !== b.enabled) return a.enabled ? -1 : 1;
                    return (b.requestCount || 0) - (a.requestCount || 0);
                });

                html += '<div class="pr-table-wrap"><table class="pr-table"><thead><tr>' +
                    '<th><i class="bi bi-puzzle me-1"></i>' + __('resources.plugin') + '</th>' +
                    '<th><i class="bi bi-activity me-1"></i>' + __('resources.health') + '</th>' +
                    '<th class="pr-th-num"><i class="bi bi-arrow-repeat me-1"></i>' + __('resources.requestCount') + '</th>' +
                    '<th class="pr-th-num"><i class="bi bi-exclamation-triangle me-1"></i>' + __('resources.errorCount') + '</th>' +
                    '<th class="pr-th-num"><i class="bi bi-lightning me-1"></i>' + __('resources.avgLatency') + '</th>' +
                    '<th class="pr-th-num"><i class="bi bi-stopwatch me-1"></i>' + __('resources.uptime') + '</th>' +
                    '<th><i class="bi bi-graph-up me-1"></i>' + __('resources.trend') + '</th>' +
                    '<th class="pr-th-num"><i class="bi bi-clock me-1"></i>' + __('resources.colUpdated') + '</th>' +
                    '</tr></thead><tbody>';
                items.forEach(function(item) {
                    html += renderPluginRow(item, esc);
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

    function renderPluginRow(item, esc) {
        var health = getHealthInfo(item.overallHealth);
        var hasTraffic = (item.requestCount || 0) > 0;
        var errorRate = hasTraffic ? (item.errorCount || 0) / item.requestCount * 100 : null;
        var danger = (item.errorCount || 0) > 0;

        // Row tooltip: custom statistics (memory etc.) + no-traffic hint
        var tips = [];
        if (item.customStatistics) {
            for (var key in item.customStatistics) {
                var displayKey = key === 'memoryBytes' ? __('resources.memory') : key;
                tips.push(displayKey + ': ' + formatCustomValue(key, item.customStatistics[key]));
            }
        }
        if (item.enabled && !hasTraffic) tips.push(__('resources.noTraffic'));

        var html = '<tr' + (item.enabled ? '' : ' class="pr-row--off"') + (tips.length ? ' title="' + esc(tips.join(' · ')) + '"' : '') + '>';

        // Plugin + tags
        html += '<td><div class="pr-cell-plugin">';
        html += '<span class="pr-dot" style="background:' + health.color + ';" title="' + health.label + '"></span>';
        html += '<div>';
        html += '<div class="pr-name">' + esc(item.displayName || item.pluginId) + '</div>';
        html += '<div class="pr-plugin-id">' + esc(item.pluginId);
        if (item.totalResources > 0) html += ' · ' + __('resources.resourcesTag') + ' ' + (item.activeResources || 0) + '/' + item.totalResources;
        html += '</div>';
        html += '</div>';
        html += '<span class="pr-tag">' + (item.isBuiltIn ? __('common.builtIn') : __('common.external')) + '</span>';
        html += item.enabled
            ? '<span class="pr-tag pr-tag--on">' + __('common.enabled') + '</span>'
            : '<span class="pr-tag pr-tag--off">' + __('common.disabled') + '</span>';
        html += '</div></td>';

        // Health
        html += '<td><span class="pr-health" style="color:' + health.color + ';">' + health.label + '</span></td>';

        // Numbers
        html += '<td class="pr-num">' + formatNum(item.requestCount || 0) + '</td>';
        html += '<td class="pr-num' + (danger ? ' pr-num--danger' : '') + '">' + formatNum(item.errorCount || 0);
        if (errorRate !== null) html += ' <span class="pr-sub' + (danger ? ' pr-sub--danger' : '') + '">' + errorRate.toFixed(1) + '%</span>';
        html += '</td>';
        html += '<td class="pr-num">' + (item.averageLatencyMs || 0).toFixed(1) + 'ms</td>';
        html += '<td class="pr-num">' + formatUptime(item.uptime) + '</td>';

        // Trend sparkline
        html += '<td class="pr-td-spark">' + renderSpark(item) + '</td>';

        // Last updated
        var ago = item.lastUpdated && window.DashboardUtils && DashboardUtils.timeAgo ? DashboardUtils.timeAgo(item.lastUpdated) : '';
        html += '<td class="pr-td-upd">' + ago + '</td>';

        html += '</tr>';
        return html;
    }

    // ── Trend sparkline ────────────────────────────────────────────────
    // Renders an inline SVG polyline from the in-memory rolling window.
    // Throughput/errors are per-interval deltas (÷5s window), latency uses the instant value.

    function getTrendSeries(h, metric) {
        if (!h) return null;
        if (metric === 'latency') return h.latency.slice();
        var src = (metric === 'errors') ? h.errors : h.requests;
        var out = [];
        for (var i = 1; i < src.length; i++) {
            var delta = src[i] - src[i - 1];
            out.push(delta > 0 ? delta : 0); // clamp: counters reset on plugin restart
        }
        return out;
    }

    function getTrendColor(metric) {
        switch (metric) {
            case 'latency': return '#f59e0b';
            case 'errors': return '#ef4444';
            default: return '#6366f1';
        }
    }

    function renderSpark(item) {
        var h = PluginResourcesModule._history[item.pluginId];
        var series = getTrendSeries(h, PluginResourcesModule._trendMetric);
        if (!series || series.length < 3) return '';

        var color = getTrendColor(PluginResourcesModule._trendMetric);
        var n = series.length;
        var max = 0;
        for (var i = 0; i < n; i++) { if (series[i] > max) max = series[i]; }

        var pts = [];
        var area = ['0,22'];
        for (var j = 0; j < n; j++) {
            var x = (n === 1 ? 0 : (j / (n - 1)) * 100).toFixed(2);
            var y = (20 - (max > 0 ? (series[j] / max) * 18 : 0)).toFixed(2);
            pts.push(x + ',' + y);
            area.push(x + ',' + y);
        }
        area.push('100,22');

        return '<svg viewBox="0 0 100 22" preserveAspectRatio="none" aria-hidden="true" title="' + __('resources.trendSpan') + '">' +
            '<polygon points="' + area.join(' ') + '" fill="' + color + '"></polygon>' +
            '<polyline points="' + pts.join(' ') + '" stroke="' + color + '"></polyline>' +
            '</svg>';
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
        // API serializes PluginResourceHealthStatus as a number:
        // 0=Stopped, 1=Starting, 2=Healthy, 3=Degraded, 4=Faulted, 5=Stopping
        var numeric = { '0': 'Stopped', '1': 'Starting', '2': 'Healthy', '3': 'Degraded', '4': 'Faulted', '5': 'Stopping' };
        var name = numeric[String(health)] || String(health);
        switch (name) {
            case 'Healthy': return { label: __('resources.healthy'), color: '#10b981' };
            case 'Starting': return { label: __('resources.starting'), color: '#38bdf8' };
            case 'Degraded': return { label: __('resources.degraded'), color: '#f59e0b' };
            case 'Faulted': return { label: __('resources.faulted'), color: '#ef4444' };
            case 'Stopped': return { label: __('resources.stopped'), color: '#94a3b8' };
            case 'Stopping': return { label: __('resources.stopping'), color: '#94a3b8' };
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
