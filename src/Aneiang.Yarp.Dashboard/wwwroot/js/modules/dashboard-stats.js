/**
 * Dashboard Statistics Module - Access statistics with charts
 */
(function() {
    'use strict';

    const StatsModule = {
        name: 'stats',
        initialized: false,

        init: async function() {
            if (this.initialized) return;
            this.setupEvents();
            this.initialized = true;
        },

        loadStats: async function() {
            try {
                const container = window.DashboardDOM.safe('#stats-content');
                if (!container) return;

                window.DashboardDOM.showLoading(container, __('stats.loading'));

                const [data, rateLimit] = await Promise.all([
                    window.DashboardApi.endpoints.getStats(),
                    window.DashboardApi.endpoints.getRateLimitStatus().catch(() => ({ enabled: false }))
                ]);

                // DashboardApi unwraps { code: 200, data: {...} } and converts PascalCase to camelCase
                if (!data || !data.hasData) {
                    this.renderNoData(container);
                    return;
                }

                this.renderStats(data, container, rateLimit);
            } catch (error) {
                console.error('[Stats] Load failed:', error);
                const container = window.DashboardDOM.safe('#stats-content');
                if (container) window.DashboardDOM.showError(container, __('stats.loadFailed'));
            }
        },

        renderNoData: function(container) {
            window.DashboardDOM.clear(container);
            container.innerHTML = `
                <div class="text-center py-5">
                    <i class="bi bi-bar-chart-line text-muted" style="font-size:48px;"></i>
                    <p class="text-muted mt-3">${__('stats.noData')}</p>
                    <p class="text-muted small">${__('stats.noDataHelp')}</p>
                </div>`;
        },

        renderStats: function(data, container, rateLimit) {
            window.DashboardDOM.clear(container);

            // Guard against undefined data or missing arrays
            if (!data || data.hasData === false) {
                this.renderNoData(container);
                return;
            }

            const errorRateColor = data.errorRate > 5 ? '#ef4444' : data.errorRate > 1 ? '#f59e0b' : '#22c55e';
            const successRateColor = data.successRate > 99 ? '#22c55e' : data.successRate > 95 ? '#f59e0b' : '#ef4444';

            const rlStatus = rateLimit && rateLimit.enabled
                ? `<span class="badge bg-warning text-dark" style="font-size:10px">${__('rateLimit.enabled')}: ${rateLimit.permitLimit}/${rateLimit.window}</span>`
                : `<span class="badge bg-secondary" style="font-size:10px">${__('rateLimit.disabled')}</span>`;

            container.innerHTML = `
                <!-- Stat Cards Row -->
                <div class="row g-3 mb-4">
                    <div class="col-md-3 col-sm-6">
                        <div class="stat-mini-card">
                            <div class="stat-mini-value">${data.totalRequests || 0}</div>
                            <div class="stat-mini-label">${__('stats.totalRequests')}</div>
                            <div class="stat-mini-sub">${data.requestsPerMin || 0} ${__('stats.requestsPerMin')}</div>
                        </div>
                    </div>
                    <div class="col-md-3 col-sm-6">
                        <div class="stat-mini-card">
                            <div class="stat-mini-value" style="color:${successRateColor}">${data.successRate || 0}%</div>
                            <div class="stat-mini-label">${__('stats.successRate')}</div>
                            <div class="stat-mini-sub">${data.successCount || 0} / ${data.errorCount || 0}</div>
                        </div>
                    </div>
                    <div class="col-md-3 col-sm-6">
                        <div class="stat-mini-card">
                            <div class="stat-mini-value">${data.avgLatency || 0}ms</div>
                            <div class="stat-mini-label">${__('stats.avgLatency')}</div>
                            <div class="stat-mini-sub">P50: ${data.p50 || 0}ms | P90: ${data.p90 || 0}ms</div>
                        </div>
                    </div>
                    <div class="col-md-3 col-sm-6">
                        <div class="stat-mini-card">
                            <div class="stat-mini-value" style="color:${errorRateColor}">${data.errorRate || 0}%</div>
                            <div class="stat-mini-label">${__('stats.errorRate')}</div>
                            <div class="stat-mini-sub">P99: ${data.p99 || 0}ms ${rlStatus}</div>
                        </div>
                    </div>
                </div>

                <!-- Charts Row -->
                <div class="row g-3 mb-4">
                    <div class="col-md-5">
                        <div class="stats-card">
                            <h6 class="stats-card-title"><i class="bi bi-bar-chart me-1"></i>${__('stats.statusCodes')}</h6>
                            <div id="stats-status-codes"></div>
                        </div>
                    </div>
                    <div class="col-md-3">
                        <div class="stats-card">
                            <h6 class="stats-card-title"><i class="bi bi-signpost-split me-1"></i>${__('stats.topRoutes')}</h6>
                            <div id="stats-top-routes"></div>
                        </div>
                    </div>
                    <div class="col-md-4">
                        <div class="stats-card">
                            <h6 class="stats-card-title"><i class="bi bi-diagram-3 me-1"></i>${__('stats.topClusters')}</h6>
                            <div id="stats-top-clusters"></div>
                        </div>
                    </div>
                </div>

                <div class="text-end">
                    <small class="text-muted">${__('index.log.updated')}${window.DashboardI18n.formatTime(new Date(data.computedAt || Date.now()))}</small>
                </div>`;

            // Render charts with safe array access
            this.renderStatusCodeChart(data.statusCodes || []);
            this.renderTopRoutes(data.topRoutes || [], data.totalRequests || 0);
            this.renderTopClusters(data.topClusters || [], data.totalRequests || 0);
        },

        renderStatusCodeChart: function(codes) {
            const el = window.DashboardDOM.safe('#stats-status-codes');
            if (!el || !codes || codes.length === 0) return;

            const maxCount = codes[0].count;
            el.innerHTML = codes.map(c => {
                const pct = maxCount > 0 ? (c.count / maxCount * 100) : 0;
                const color = c.code >= 500 ? '#ef4444' : c.code >= 400 ? '#f59e0b' : c.code >= 300 ? '#3b82f6' : '#22c55e';
                const bgPct = codes[0].count > 0 ? (c.count / codes[0].count * 100) : 0;
                return `<div class="stats-bar-row">
                    <span class="stats-bar-label"><span class="badge" style="background:${color}">${c.code}</span></span>
                    <div class="stats-bar-track"><div class="stats-bar-fill" style="width:${bgPct}%;background:${color}"></div></div>
                    <span class="stats-bar-value">${c.count}</span>
                </div>`;
            }).join('');
        },

        renderTopRoutes: function(routes, total) {
            const el = window.DashboardDOM.safe('#stats-top-routes');
            if (!el || !routes || routes.length === 0) return;

            const maxCount = routes[0].count;
            el.innerHTML = routes.map(r => {
                const name = r.name || r.route || 'unknown';
                const pct = total > 0 ? (r.count / total * 100).toFixed(1) : 0;
                const barPct = maxCount > 0 ? (r.count / maxCount * 100) : 0;
                return `<div class="stats-bar-row">
                    <span class="stats-bar-label" title="${window.DashboardUtils.escapeHtml(name)}">${window.DashboardUtils.escapeHtml(name.length > 18 ? name.substring(0, 18) + '...' : name)}</span>
                    <div class="stats-bar-track"><div class="stats-bar-fill" style="width:${barPct}%"></div></div>
                    <span class="stats-bar-value">${r.count} <small class="text-muted">(${pct}%)</small></span>
                </div>`;
            }).join('');
        },

        renderTopClusters: function(clusters, total) {
            const el = window.DashboardDOM.safe('#stats-top-clusters');
            if (!el || !clusters || clusters.length === 0) return;

            const maxCount = clusters[0].count;
            el.innerHTML = clusters.map(c => {
                const name = c.name || c.cluster || 'unknown';
                const pct = total > 0 ? (c.count / total * 100).toFixed(1) : 0;
                const barPct = maxCount > 0 ? (c.count / maxCount * 100) : 0;
                return `<div class="stats-bar-row">
                    <span class="stats-bar-label" title="${window.DashboardUtils.escapeHtml(name)}">${window.DashboardUtils.escapeHtml(name.length > 22 ? name.substring(0, 22) + '...' : name)}</span>
                    <div class="stats-bar-track"><div class="stats-bar-fill" style="width:${barPct}%;background:#6366f1"></div></div>
                    <span class="stats-bar-value">${c.count} <small class="text-muted">(${pct}%)</small></span>
                </div>`;
            }).join('');
        },

        setupEvents: function() {
            document.addEventListener('dashboard:shortcut:refresh', () => this.loadStats());
            document.addEventListener('dashboard:localeChange', () => this.loadStats());
        }
    };

    if (window.DashboardApp) {
        window.DashboardApp.registerModule('stats', StatsModule);
    }
    window.StatsModule = StatsModule;
})();
