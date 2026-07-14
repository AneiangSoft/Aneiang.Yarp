/**
 * Dashboard Home Module - Overview page
 */
(function() {
    'use strict';

    const HomeModule = {
        name: 'home',
        initialized: false,

        init: async function() {
            if (this.initialized) return;

            this.setupEvents();

            this.initialized = true;
        },

        loadInfo: async function() {
            try {
                // Note: Gateway info is displayed in #stat-bar, not a separate container
                const info = await window.DashboardApi.endpoints.getInfo();
                
                window.DashboardState.set('data.info', info);

                // Render info
                this.renderInfo(info);

                // Update stat cards
                await this.updateStatCards();

            } catch (error) {
                console.error('[Home] Load info failed:', error);
                console.error('[Home] Error stack:', error.stack);
            }
        },

        renderInfo: function(info) {
            // Version
            const versionEl = window.DashboardDOM.safe('#info-version');
            if (versionEl) versionEl.textContent = info.version || '-';

            // Environment
            const envEl = window.DashboardDOM.safe('#info-env');
            if (envEl) envEl.textContent = info.environment || '-';

            // Start time
            const startEl = window.DashboardDOM.safe('#info-start');
            if (startEl && info.startTime) {
                startEl.textContent = info.startTime;
            }

            // Uptime
            const uptimeEl = window.DashboardDOM.safe('#info-uptime');
            if (uptimeEl && info.uptime) {
                uptimeEl.textContent = info.uptime;
            }

            // Memory
            const memoryEl = window.DashboardDOM.safe('#info-memory');
            if (memoryEl && info.memoryMb) {
                memoryEl.textContent = info.memoryMb.toFixed(1) + ' MB';
            }

            // Machine name
            const machineEl = window.DashboardDOM.safe('#info-machine');
            if (machineEl) machineEl.textContent = info.machineName || '-';
        },

        updateStatCards: async function() {
            try {
                // Load clusters, routes, and traffic in parallel
                const [clusters, routes, trafficData] = await Promise.all([
                    window.DashboardApi.endpoints.getClusters(),
                    window.DashboardApi.endpoints.getRoutes(),
                    window.DashboardApi.endpoints.getTrafficData?.(15) || Promise.resolve(null)
                ]);

                // Update state — ServiceTabs will render from this without re-fetching
                window.DashboardState.set('data.clusters', clusters || []);
                window.DashboardState.set('data.routes', routes || []);

                // Calculate stats
                const clusterCount = (clusters || []).length;
                const routeCount = (routes || []).length;

                // Use healthyCount/unknownCount/unhealthyCount from backend
                const healthy = (clusters || []).reduce((sum, c) => sum + (c.healthyCount || 0), 0);
                const unknown = (clusters || []).reduce((sum, c) => sum + (c.unknownCount || 0), 0);
                const unhealthy = (clusters || []).reduce((sum, c) => sum + (c.unhealthyCount || 0), 0);

                // Update DOM
                const clustersEl = window.DashboardDOM.safe('#stat-clusters');
                if (clustersEl) clustersEl.textContent = clusterCount;

                const healthyEl = window.DashboardDOM.safe('#stat-healthy');
                if (healthyEl) healthyEl.textContent = `${healthy} / ${unknown} / ${unhealthy}`;

                const routesEl = window.DashboardDOM.safe('#stat-routes');
                if (routesEl) routesEl.textContent = routeCount;

                // Update QPS stat card (Index.cshtml)
                const qpsEl = window.DashboardDOM.safe('#stat-qps');
                if (qpsEl && trafficData) {
                    var qv = (trafficData.currentQps != null && !isNaN(trafficData.currentQps))
                        ? trafficData.currentQps : 0;
                    qpsEl.textContent = qv;
                }

                // Render preview lists
                this.renderClusterPreview(clusters || []);
                this.renderRoutePreview(routes || []);

                // Trigger ServiceTabs to render clusters if that tab is active (data already loaded)
                if (window.ServiceTabs && window.ServiceTabs.current === 'clusters') {
                    window.DashboardApp?.modules?.clusters?.init?.();
                    window.DashboardApp?.modules?.clusters?.renderClusters?.();
                }

            } catch (error) {
                console.error('[Home] Update stats failed:', error);
            }
        },

        renderClusterPreview: function(clusters) {
            const tbody = window.DashboardDOM.safe('#cluster-preview-tbody');
            if (!tbody) return;

            const countEl = document.querySelector('#cluster-preview-count');
            if (countEl) countEl.textContent = __('home.cluster.total', { count: clusters.length });

            // Show only top 5
            const previewClusters = clusters.slice(0, 5);

            if (previewClusters.length === 0) {
                tbody.innerHTML = `<tr><td colspan="2" class="text-center text-muted py-3">${__('home.noData')}</td></tr>`;
                return;
            }

            const fragment = document.createDocumentFragment();
            previewClusters.forEach(cluster => {
                const tr = window.DashboardDOM.create('tr', { style: { cursor: 'pointer' } });
                
                // Cluster name
                const tdName = window.DashboardDOM.create('td', {
                    innerHTML: `<strong>${window.DashboardUtils.escapeHtml(cluster.clusterId)}</strong>`
                });
                tr.appendChild(tdName);

                // Health status
                const tdHealth = window.DashboardDOM.create('td');
                const healthBadge = this.createHealthBadge(cluster);
                tdHealth.appendChild(healthBadge);
                tr.appendChild(tdHealth);

                fragment.appendChild(tr);
            });

            window.DashboardDOM.clear(tbody);
            tbody.appendChild(fragment);
        },

        renderRoutePreview: function(routes) {
            const tbody = window.DashboardDOM.safe('#route-preview-tbody');
            if (!tbody) return;

            const countEl = document.querySelector('#route-preview-count');
            if (countEl) countEl.textContent = __('home.route.total', { count: routes.length });

            // Show only top 5
            const previewRoutes = routes.slice(0, 5);

            if (previewRoutes.length === 0) {
                tbody.innerHTML = `<tr><td colspan="2" class="text-center text-muted py-3">${__('home.noData')}</td></tr>`;
                return;
            }

            const fragment = document.createDocumentFragment();
            previewRoutes.forEach(route => {
                const tr = window.DashboardDOM.create('tr', { style: { cursor: 'pointer' } });
                
                // Route name
                const tdName = window.DashboardDOM.create('td', {
                    innerHTML: `<strong>${window.DashboardUtils.escapeHtml(route.routeId)}</strong>`
                });
                tr.appendChild(tdName);

                // Cluster name
                const tdCluster = window.DashboardDOM.create('td', {
                    textContent: route.clusterId || '-'
                });
                tr.appendChild(tdCluster);

                fragment.appendChild(tr);
            });

            window.DashboardDOM.clear(tbody);
            tbody.appendChild(fragment);
        },

        createHealthBadge: function(cluster) {
            const badge = window.DashboardDOM.create('span', {
                className: 'badge',
                style: { fontSize: '12px', padding: '4px 8px' }
            });

            if (cluster.healthyCount > 0) {
                badge.className += ' bg-success';
                badge.textContent = `${__('home.healthy')} ${cluster.healthyCount}`;
            } else if (cluster.unhealthyCount > 0) {
                badge.className += ' bg-danger';
                badge.textContent = `${__('home.unhealthy')} ${cluster.unhealthyCount}`;
            } else {
                badge.className += ' bg-secondary';
                badge.textContent = `${__('home.unknown')} ${cluster.unknownCount}`;
            }

            return badge;
        },

        formatUptime: function(uptimeMs) {
            if (!uptimeMs) return '-';
            
            const seconds = Math.floor(uptimeMs / 1000);
            const days = Math.floor(seconds / 86400);
            const hours = Math.floor((seconds % 86400) / 3600);
            const minutes = Math.floor((seconds % 3600) / 60);
            
            if (days > 0) {
                return __('home.uptime.days', { days, hours, minutes });
            } else if (hours > 0) {
                return __('home.uptime.hours', { hours, minutes });
            } else {
                return __('home.uptime.minutes', { minutes });
            }
        },

        setupEvents: function() {
            // Refresh shortcut
            document.addEventListener('dashboard:shortcut:refresh', async () => {
                await this.refresh();
            });

            // Locale change
            document.addEventListener('dashboard:localeChange', () => {
                const info = window.DashboardState.get('data.info');
                if (info) {
                    this.renderInfo(info);
                }
            });
        },

        refresh: async function() {
            await Promise.all([
                this.loadInfo(),
                this.updateStatCards()
            ]);
        }
    };

    if (window.DashboardApp) {
        window.DashboardApp.registerModule('home', HomeModule);
    }

    // Expose to window for backward compatibility
    window.loadInfo = HomeModule.loadInfo.bind(HomeModule);
    window.refreshHome = HomeModule.refresh.bind(HomeModule);

})();
