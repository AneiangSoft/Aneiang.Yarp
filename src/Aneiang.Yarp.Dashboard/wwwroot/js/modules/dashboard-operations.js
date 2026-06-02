/**
 * Operations Module - DevOps Dashboard functionality
 * 运维监控模块 - 提供运维监控台专属功能
 */
(function() {
    'use strict';

    // Module state
    var trafficChart = null;
    var trafficUpdateInterval = null;
    var currentTimeRange = 15;

    // API Endpoints
    var endpoints = {
        alertSummary: '/api/operations/alert-summary',
        traffic: '/api/operations/traffic',
        topIssues: '/api/operations/top-issues',
        healthSummary: '/api/operations/health-summary',
        snapshot: '/api/operations/snapshot',
        emergencyDisable: '/api/operations/emergency-disable-route',
        emergencyEnable: '/api/operations/emergency-enable-route',
        routeList: '/api/operations/routes'
    };

    /**
     * Initialize the operations module
     */
    function init() {
        console.log('[OpsModule] Initializing...');
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
            DashboardApi.endpoints.emergencyDisableRoute = function(routeId) {
                return DashboardApi.post(endpoints.emergencyDisable + '/' + encodeURIComponent(routeId), {});
            };
            DashboardApi.endpoints.emergencyEnableRoute = function(routeId) {
                return DashboardApi.post(endpoints.emergencyEnable + '/' + encodeURIComponent(routeId), {});
            };
            DashboardApi.endpoints.getRouteList = function() { return DashboardApi.get(endpoints.routeList); };
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
            
            // Update current QPS badge
            var qpsEl = document.getElementById('current-qps');
            if (qpsEl) {
                qpsEl.textContent = data.currentQps + ' req/s';
            }
        } catch (e) {
            console.error('[OpsModule] Failed to load traffic data:', e);
        }
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
     * Emergency disable a route - shows a modal with route list
     * 紧急禁用路由 - 显示路由列表弹窗
     */
    async function emergencyDisable() {
        try {
            // Load route list
            var routes = await DashboardApi.endpoints.getRouteList();
            if (!routes || !Array.isArray(routes)) {
                alert(__('overview.quickActions.loadFailed') || '加载路由列表失败');
                return;
            }

            var disabledRoutes = routes.filter(function(r) { return r.disabled; });
            var enabledRoutes = routes.filter(function(r) { return !r.disabled; });

            // Show modal to select route
            var modalHtml = createRouteSelectModal(enabledRoutes, 'disable');
            showModal(modalHtml, function(selectedRouteId) {
                if (selectedRouteId) {
                    confirmAndDisableRoute(selectedRouteId);
                }
            });
        } catch (e) {
            console.error('[OpsModule] Failed to load routes:', e);
            alert(__('overview.quickActions.loadFailed') || '加载失败');
        }
    }

    /**
     * Create route selection modal HTML
     */
    function createRouteSelectModal(routes, action) {
        var isDisable = action === 'disable';
        var title = isDisable ? __('overview.quickActions.selectDisable') || '选择要禁用的路由' : __('overview.quickActions.selectEnable') || '选择要启用的路由';
        var btnClass = isDisable ? 'btn-danger' : 'btn-success';
        var btnText = isDisable ? __('overview.quickActions.disable') || '禁用' : __('overview.quickActions.enable') || '启用';

        var routeOptions = routes.map(function(r) {
            return '<option value="' + escapeHtml(r.routeId) + '">' +
                   escapeHtml(r.routeId) + ' → ' + escapeHtml(r.clusterId || '-') + ' (' + escapeHtml(r.matchPath || '-') + ')' +
                   '</option>';
        }).join('');

        if (routes.length === 0) {
            routeOptions = '<option value="">' + (isDisable ? __('overview.quickActions.noEnabledRoutes') || '没有可禁用的路由' : __('overview.quickActions.noDisabledRoutes') || '没有已禁用的路由') + '</option>';
        }

        return '<div class="modal fade" id="route-select-modal" tabindex="-1">' +
            '<div class="modal-dialog modal-dialog-centered">' +
                '<div class="modal-content">' +
                    '<div class="modal-header">' +
                        '<h5 class="modal-title">' + title + '</h5>' +
                        '<button type="button" class="btn-close" data-bs-dismiss="modal"></button>' +
                    '</div>' +
                    '<div class="modal-body">' +
                        '<select id="route-select" class="form-select" size="10" style="width:100%;">' +
                            routeOptions +
                        '</select>' +
                    '</div>' +
                    '<div class="modal-footer">' +
                        '<button type="button" class="btn btn-secondary" data-bs-dismiss="modal">' + __('modal.cancelBtn') + '</button>' +
                        '<button type="button" class="btn ' + btnClass + '" id="confirm-route-action">' + btnText + '</button>' +
                    '</div>' +
                '</div>' +
            '</div>' +
        '</div>';
    }

    /**
     * Show modal and handle selection
     */
    function showModal(html, onConfirm) {
        var existingModal = document.getElementById('route-select-modal');
        if (existingModal) existingModal.remove();

        document.body.insertAdjacentHTML('beforeend', html);
        var modalEl = document.getElementById('route-select-modal');
        var modal = new bootstrap.Modal(modalEl);

        document.getElementById('confirm-route-action').onclick = function() {
            var select = document.getElementById('route-select');
            var selectedId = select.value;
            modal.hide();
            setTimeout(function() { modalEl.remove(); }, 300);
            if (onConfirm) onConfirm(selectedId);
        };

        modal.show();
    }

    /**
     * Confirm and disable a route
     */
    async function confirmAndDisableRoute(routeId) {
        if (!routeId) return;

        var confirmed = confirm((__('overview.quickActions.confirmDisable') || '确认紧急禁用路由') + ' ' + routeId + '?');
        if (!confirmed) return;

        try {
            var result = await DashboardApi.endpoints.emergencyDisableRoute(routeId);
            if (result.code === 200) {
                alert((__('overview.quickActions.disabledSuccess') || '路由已禁用') + ': ' + routeId);
                // Refresh overview page
                if (window.DashboardApp && DashboardApp.modules && DashboardApp.modules.home) {
                    DashboardApp.modules.home.loadInfo();
                    DashboardApp.modules.home.updateStatCards();
                }
            } else {
                alert((__('overview.quickActions.disabledFailed') || '禁用失败') + ': ' + (result.message || ''));
            }
        } catch (e) {
            console.error('[OpsModule] Emergency disable failed:', e);
            alert((__('overview.quickActions.disabledFailed') || '禁用失败'));
        }
    }

    /**
     * Emergency enable a route - shows a modal with disabled route list
     * 紧急启用路由 - 显示已禁用路由列表弹窗
     */
    async function emergencyEnable() {
        try {
            var routes = await DashboardApi.endpoints.getRouteList();
            if (!routes || !Array.isArray(routes)) {
                alert(__('overview.quickActions.loadFailed') || '加载路由列表失败');
                return;
            }

            var disabledRoutes = routes.filter(function(r) { return r.disabled; });

            var modalHtml = createRouteSelectModal(disabledRoutes, 'enable');
            showModal(modalHtml, function(selectedRouteId) {
                if (selectedRouteId) {
                    confirmAndEnableRoute(selectedRouteId);
                }
            });
        } catch (e) {
            console.error('[OpsModule] Failed to load routes:', e);
            alert(__('overview.quickActions.loadFailed') || '加载失败');
        }
    }

    /**
     * Confirm and enable a route
     */
    async function confirmAndEnableRoute(routeId) {
        if (!routeId) return;

        try {
            var result = await DashboardApi.endpoints.emergencyEnableRoute(routeId);
            if (result.code === 200) {
                alert((__('overview.quickActions.enabledSuccess') || '路由已启用') + ': ' + routeId);
                if (window.DashboardApp && DashboardApp.modules && DashboardApp.modules.home) {
                    DashboardApp.modules.home.loadInfo();
                    DashboardApp.modules.home.updateStatCards();
                }
            } else {
                alert((__('overview.quickActions.enabledFailed') || '启用失败') + ': ' + (result.message || ''));
            }
        } catch (e) {
            console.error('[OpsModule] Emergency enable failed:', e);
            alert((__('overview.quickActions.enabledFailed') || '启用失败'));
        }
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
                alert(__('overview.quickActions.healthRefreshed') || '健康状态已刷新');
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

    // Utility functions
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

    // i18n helper
    function __(key) {
        if (window.I18N && I18N[key]) return I18N[key];
        return key;
    }

    // Public API
    window.OpsModule = {
        init: init,
        loadAlertSummary: loadAlertSummary,
        loadTrafficChart: loadTrafficChart,
        loadTopErrors: loadTopErrors,
        changeTimeRange: changeTimeRange,
        emergencyDisable: emergencyDisable,
        emergencyEnable: emergencyEnable,
        refreshHealth: refreshHealth,
        exportSnapshot: exportSnapshot
    };

    // Auto-init if DashboardApp exists
    if (window.DashboardApp) {
        DashboardApp.registerModule('operations', window.OpsModule);
    }

})();
