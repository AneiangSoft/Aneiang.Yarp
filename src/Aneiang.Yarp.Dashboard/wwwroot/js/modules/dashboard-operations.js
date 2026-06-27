/**
 * Operations Module - DevOps Dashboard functionality
 * 运维监控模块 - 提供运维监控台专属功能
 */
(function() {
    'use strict';

    var trafficChart = null;
    var trafficUpdateInterval = null;
    var currentTimeRange = 15;

    var endpoints = {
        alertSummary: '/api/operations/alert-summary',
        traffic: '/api/operations/traffic',
        topIssues: '/api/operations/top-issues',
        healthSummary: '/api/operations/health-summary',
        snapshot: '/api/operations/snapshot'
    };

    /**
     * Initialize the operations module
     */
    function init() {
        registerEndpoints();
    }

    /**
     * Register API endpoints with DashboardApi
     */
    function registerEndpoints() {
        if (window.DashboardApi && DashboardApi.endpoints) {
            DashboardApi.endpoints.getAlertSummary = function() { return DashboardApi.get(endpoints.alertSummary); };
            DashboardApi.endpoints.getTrafficData = function(minutes) { return DashboardApi.get(endpoints.traffic + '?minutes=' + (minutes || 15)); };
            DashboardApi.endpoints.getTopIssues = function(count) { return DashboardApi.get(endpoints.topIssues + '?count=' + (count || 5)); };
            DashboardApi.endpoints.getHealthSummary = function() { return DashboardApi.get(endpoints.healthSummary); };
            DashboardApi.endpoints.exportSnapshot = function() { return DashboardApi.get(endpoints.snapshot); };
        }
    }

    /**
     * Load alert summary data for the alert bar
     * 加载告警摘要数据
     */
    async function loadAlertSummary() {
        try {
            var data = await DashboardApi.endpoints.getAlertSummary();
            if (!data) return;
            
            // Update alert cards
            updateElement('alert-unhealthy', data.unhealthyCount > 0 ? data.unhealthyCount : '-');
            updateElement('alert-circuit', data.circuitBreakerCount > 0 ? data.circuitBreakerCount : '-');
            updateElement('alert-rate-limit', data.recentErrors > 0 ? data.recentErrors : '-');
            updateElement('alert-events', data.unhandledEvents > 0 ? data.unhandledEvents : '-');

            // Add pulse animation to critical alerts
            togglePulse('alert-unhealthy', data.unhealthyCount > 0);
            togglePulse('alert-circuit', data.circuitBreakerCount > 0);
        } catch (e) {
            console.error('[OpsModule] Failed to load alert summary:', e);
        }
    }

    /**
     * Load and render traffic chart
     * 加载并渲染流量图表
     */
    async function loadTrafficChart() {
        try {
            var data = await DashboardApi.endpoints.getTrafficData(currentTimeRange);
            if (!data) return;
            renderTrafficChart(data);
            
            // Update current QPS badge (traffic chart header)
            var qpsEl = document.getElementById('current-qps');
            if (qpsEl) {
                var qpsValue = (data.currentQps != null && !isNaN(data.currentQps)) ? data.currentQps : 0;
                qpsEl.textContent = qpsValue + ' req/s';
            }

            // Update stat card QPS value (overview page)
            var statQpsEl = document.getElementById('stat-qps');
            if (statQpsEl) {
                var qpsVal = (data.currentQps != null && !isNaN(data.currentQps)) ? data.currentQps : 0;
                statQpsEl.textContent = qpsVal;
            }

            // Update QPS trend indicator
            var trendEl = document.getElementById('trend-qps');
            if (trendEl) {
                var trend = getQpsTrend(data);
                trendEl.innerHTML = trend;
            }
        } catch (e) {
            console.error('[OpsModule] Failed to load traffic data:', e);
        }
    }

    /**
     * Determine QPS trend direction from data
     */
    function getQpsTrend(data) {
        var qps = (data && data.qps) || [];
        if (qps.length < 3) return '<i class="bi bi-dash"></i> --';
        var last = qps[qps.length - 1];
        var prev = qps[qps.length - 2];
        if (last == null || prev == null) return '<i class="bi bi-dash"></i> --';
        if (last > prev * 1.1) return '<i class="bi bi-arrow-up text-danger"></i> ↑';
        if (last < prev * 0.9) return '<i class="bi bi-arrow-down text-success"></i> ↓';
        return '<i class="bi bi-arrow-right text-muted"></i> →';
    }

    /**
     * Render traffic chart using Chart.js
     */
    function renderTrafficChart(data) {
        var ctx = document.getElementById('traffic-chart');
        if (!ctx) return;

        var labels = data.labels || [];
        var qpsData = data.qps || [];
        var errorData = data.errors || [];

        if (trafficChart) {
            trafficChart.destroy();
        }

        trafficChart = new Chart(ctx, {
            type: 'line',
            data: {
                labels: labels,
                datasets: [
                    {
                        label: 'QPS',
                        data: qpsData,
                        borderColor: '#6366f1',
                        backgroundColor: 'rgba(99, 102, 241, 0.1)',
                        borderWidth: 2,
                        fill: true,
                        tension: 0.4,
                        pointRadius: 2,
                        pointHoverRadius: 5
                    },
                    {
                        label: 'Errors',
                        data: errorData,
                        borderColor: '#ef4444',
                        backgroundColor: 'rgba(239, 68, 68, 0.1)',
                        borderWidth: 2,
                        fill: true,
                        tension: 0.4,
                        pointRadius: 2,
                        pointHoverRadius: 5,
                        yAxisID: 'y1'
                    }
                ]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                interaction: {
                    mode: 'index',
                    intersect: false
                },
                plugins: {
                    legend: {
                        position: 'top',
                        labels: {
                            usePointStyle: true,
                            padding: 15,
                            font: { size: 12 }
                        }
                    },
                    tooltip: {
                        backgroundColor: 'rgba(15, 23, 42, 0.9)',
                        padding: 12,
                        cornerRadius: 8,
                        titleFont: { size: 13 },
                        bodyFont: { size: 12 }
                    }
                },
                scales: {
                    x: {
                        grid: { display: false },
                        ticks: { font: { size: 11 }, maxRotation: 0 }
                    },
                    y: {
                        beginAtZero: true,
                        grid: { color: 'rgba(0,0,0,0.05)' },
                        ticks: { font: { size: 11 } },
                        title: { display: true, text: 'Requests' }
                    },
                    y1: {
                        position: 'right',
                        beginAtZero: true,
                        grid: { display: false },
                        ticks: { font: { size: 11 }, color: '#ef4444' },
                        title: { display: true, text: 'Errors', color: '#ef4444' }
                    }
                }
            }
        });
    }

    /**
     * Change time range for traffic chart
     */
    function changeTimeRange(minutes) {
        currentTimeRange = parseInt(minutes);
        loadTrafficChart();
    }

    /**
     * Load top errors and slow clusters
     * 加载异常路由和高延迟集群数据
     */
    async function loadTopErrors() {
        try {
            var data = await DashboardApi.endpoints.getTopIssues(5);
            if (!data) return;
            renderErrorRoutes(data.errorRoutes || []);
            renderSlowClusters(data.slowClusters || []);
        } catch (e) {
            console.error('[OpsModule] Failed to load top issues:', e);
        }
    }

    /**
     * Render error routes table
     */
    function renderErrorRoutes(routes) {
        var tbody = document.getElementById('error-routes-tbody');
        var countBadge = document.getElementById('error-routes-count');
        if (!tbody) return;

        if (countBadge) {
            countBadge.textContent = routes.length;
        }

        if (routes.length === 0) {
            tbody.innerHTML = '<tr><td colspan="3" class="text-center text-muted py-4">' + __('overview.top.empty') + '</td></tr>';
            return;
        }

        var html = '';
        routes.forEach(function(route) {
            var errorRateClass = route.errorRate >= 50 ? 'text-danger' : route.errorRate >= 20 ? 'text-warning' : 'text-muted';
            html += '<tr>';
            html += '<td style="padding:10px 16px;"><code>' + escapeHtml(route.routeId) + '</code></td>';
            html += '<td style="padding:10px 16px;"><span class="badge bg-danger">' + route.errorCount + '</span></td>';
            html += '<td style="padding:10px 16px;"><span class="' + errorRateClass + '">' + route.errorRate + '%</span></td>';
            html += '</tr>';
        });
        tbody.innerHTML = html;
    }

    /**
     * Render slow clusters table
     */
    function renderSlowClusters(clusters) {
        var tbody = document.getElementById('slow-clusters-tbody');
        var countBadge = document.getElementById('slow-clusters-count');
        if (!tbody) return;

        if (countBadge) {
            countBadge.textContent = clusters.length;
        }

        if (clusters.length === 0) {
            tbody.innerHTML = '<tr><td colspan="3" class="text-center text-muted py-4">' + __('overview.top.empty') + '</td></tr>';
            return;
        }

        var html = '';
        clusters.forEach(function(cluster) {
            var p99Class = cluster.p99Latency >= 1000 ? 'text-danger' : cluster.p99Latency >= 500 ? 'text-warning' : 'text-muted';
            html += '<tr>';
            html += '<td style="padding:10px 16px;"><code>' + escapeHtml(cluster.clusterId) + '</code></td>';
            html += '<td style="padding:10px 16px;">' + cluster.avgLatency.toFixed(0) + 'ms</td>';
            html += '<td style="padding:10px 16px;"><span class="' + p99Class + '">' + cluster.p99Latency.toFixed(0) + 'ms</span></td>';
            html += '</tr>';
        });
        tbody.innerHTML = html;
    }

    /**
     * Refresh health check for all clusters
     * 刷新所有集群健康状态
     */
    async function refreshHealth() {
        try {
            // Trigger health check refresh
            var result = await DashboardApi.get('/api/health-check/clusters');
            if (result.code === 200) {
                window.DashboardModals.showSuccess(__('overview.quickActions.healthRefreshed') || '健康状态已刷新');
                loadAlertSummary();
            }
        } catch (e) {
            console.error('[OpsModule] Health refresh failed:', e);
        }
    }

    /**
     * Export system snapshot
     * 导出系统快照
     */
    async function exportSnapshot() {
        try {
            var result = await DashboardApi.endpoints.exportSnapshot();
            if (result.code === 200 && result.data) {
                var dataStr = JSON.stringify(result.data, null, 2);
                var blob = new Blob([dataStr], { type: 'application/json' });
                var url = URL.createObjectURL(blob);
                var a = document.createElement('a');
                a.href = url;
                a.download = 'gateway-snapshot-' + new Date().toISOString().slice(0,19).replace(/:/g,'-') + '.json';
                document.body.appendChild(a);
                a.click();
                document.body.removeChild(a);
                URL.revokeObjectURL(url);
            }
        } catch (e) {
            console.error('[OpsModule] Export snapshot failed:', e);
        }
    }

    /**
     * Load system health metrics
     */
    async function loadSystemHealth() {
        try {
            // Use endpoints.getInfo (the correct API path), not DashboardApi.getInfo (doesn't exist)
            var info = await window.DashboardApi.endpoints.getInfo();
            if (!info) return;

            // Memory
            var memMB = info.memoryWorkingSet || (info.memoryMb ? info.memoryMb * 1024 * 1024 : 0);
            var memMBVal = Math.round(memMB / (1024 * 1024));
            updateElement('sys-mem-value', memMBVal + ' MB');
            var memPct = info.totalMemory > 0
                ? Math.min(100, Math.round((memMB / info.totalMemory) * 100))
                : Math.min(100, Math.round((memMB / (8 * 1024 * 1024 * 1024)) * 100)); // fallback: 8GB
            var memBar = document.getElementById('sys-mem-bar');
            if (memBar) memBar.style.width = memPct + '%';

            // CPU (approximate from process CPU time)
            var cpuPct = info.cpuUsage || 0;
            updateElement('sys-cpu-value', cpuPct + '%');
            var cpuBar = document.getElementById('sys-cpu-bar');
            if (cpuBar) cpuBar.style.width = Math.min(100, cpuPct) + '%';

            // GC + Threads
            updateElement('sys-gc-value', info.gcCount ?? '--');
            updateElement('sys-thread-value', info.threadCount ?? '--');
        } catch (e) {
            console.error('[OpsModule] System health load failed:', e);
        }
    }

    function updateElement(id, value) {
        var el = document.getElementById(id);
        if (el) el.textContent = value;
    }

    function togglePulse(id, enable) {
        var el = document.getElementById(id);
        if (el && el.parentElement && el.parentElement.parentElement) {
            var card = el.parentElement.parentElement;
            if (enable) {
                card.classList.add('pulse-alert');
            } else {
                card.classList.remove('pulse-alert');
            }
        }
    }

    function escapeHtml(text) {
        if (!text) return '';
        var div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    function __(key) {
        if (window.I18N && I18N[key]) return I18N[key];
        return key;
    }

    window.OpsModule = {
        init: init,
        loadAlertSummary: loadAlertSummary,
        loadTrafficChart: loadTrafficChart,
        loadTopErrors: loadTopErrors,
        loadSystemHealth: loadSystemHealth,
        changeTimeRange: changeTimeRange,
        refreshHealth: refreshHealth,
        exportSnapshot: exportSnapshot
    };

    if (window.DashboardApp) {
        DashboardApp.registerModule('operations', window.OpsModule);
    }

})();
