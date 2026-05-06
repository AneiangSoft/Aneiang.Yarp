/**
 * Dashboard Home Module - Overview page
 */
(function() {
    'use strict';

    const HomeModule = {
        name: 'home',
        initialized: false,

        // ===== Initialization =====
        init: async function() {
            if (this.initialized) return;
            
            console.log('[Home] Initializing...');
            
            try {
                // Load initial data
                await this.loadInfo();
                
                // Setup event listeners
                this.setupEvents();
                
                this.initialized = true;
                console.log('[Home] Initialized');
            } catch (error) {
                console.error('[Home] Init failed:', error);
                throw error;
            }
        },

        // ===== Load Gateway Info =====
        loadInfo: async function() {
            try {
                console.log('[Home] Loading gateway info...');
                // Note: Gateway info is displayed in #stat-bar, not a separate container
                const info = await window.DashboardApi.endpoints.getInfo();
                console.log('[Home] Gateway info loaded:', info);
                
                // Update state
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

        // ===== Render Gateway Info =====
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

        // ===== Update Stat Cards =====
        updateStatCards: async function() {
            try {
                // Load clusters and routes in parallel
                const [clusters, routes] = await Promise.all([
                    window.DashboardApi.endpoints.getClusters(),
                    window.DashboardApi.endpoints.getRoutes()
                ]);

                // Update state
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

                // Render preview lists
                this.renderClusterPreview(clusters || []);
                this.renderRoutePreview(routes || []);

            } catch (error) {
                console.error('[Home] Update stats failed:', error);
            }
        },

        // ===== Render Cluster Preview (top 5) =====
        renderClusterPreview: function(clusters) {
            const tbody = window.DashboardDOM.safe('#cluster-preview-tbody');
            if (!tbody) return;

            const countEl = window.DashboardDOM.safe('#cluster-preview-count');
            if (countEl) countEl.textContent = `共 ${clusters.length} 个`;

            // Show only top 5
            const previewClusters = clusters.slice(0, 5);

            if (previewClusters.length === 0) {
                tbody.innerHTML = '<tr><td colspan="2" class="text-center text-muted py-3">暂无数据</td></tr>';
                return;
            }

            const fragment = document.createDocumentFragment();
            previewClusters.forEach(cluster => {
                const tr = window.DashboardDOM.create('tr', { style: { cursor: 'pointer' } });
                
                // Cluster name
                const tdName = window.DashboardDOM.create('td', {
                    innerHTML: `<strong>${this.escapeHtml(cluster.clusterId)}</strong>`
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

        // ===== Render Route Preview (top 5) =====
        renderRoutePreview: function(routes) {
            const tbody = window.DashboardDOM.safe('#route-preview-tbody');
            if (!tbody) return;

            const countEl = window.DashboardDOM.safe('#route-preview-count');
            if (countEl) countEl.textContent = `共 ${routes.length} 条`;

            // Show only top 5
            const previewRoutes = routes.slice(0, 5);

            if (previewRoutes.length === 0) {
                tbody.innerHTML = '<tr><td colspan="2" class="text-center text-muted py-3">暂无数据</td></tr>';
                return;
            }

            const fragment = document.createDocumentFragment();
            previewRoutes.forEach(route => {
                const tr = window.DashboardDOM.create('tr', { style: { cursor: 'pointer' } });
                
                // Route name
                const tdName = window.DashboardDOM.create('td', {
                    innerHTML: `<strong>${this.escapeHtml(route.routeId)}</strong>`
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

        // ===== Create Health Badge =====
        createHealthBadge: function(cluster) {
            const badge = window.DashboardDOM.create('span', {
                className: 'badge',
                style: { fontSize: '12px', padding: '4px 8px' }
            });

            if (cluster.healthyCount > 0) {
                badge.className += ' bg-success';
                badge.textContent = `健康 ${cluster.healthyCount}`;
            } else if (cluster.unhealthyCount > 0) {
                badge.className += ' bg-danger';
                badge.textContent = `异常 ${cluster.unhealthyCount}`;
            } else {
                badge.className += ' bg-secondary';
                badge.textContent = `未知 ${cluster.unknownCount}`;
            }

            return badge;
        },

        // ===== Escape HTML =====
        escapeHtml: function(text) {
            if (!text) return '';
            const div = document.createElement('div');
            div.textContent = text;
            return div.innerHTML;
        },

        // ===== Format Uptime =====
        formatUptime: function(uptimeMs) {
            if (!uptimeMs) return '-';
            
            const seconds = Math.floor(uptimeMs / 1000);
            const days = Math.floor(seconds / 86400);
            const hours = Math.floor((seconds % 86400) / 3600);
            const minutes = Math.floor((seconds % 3600) / 60);
            
            if (days > 0) {
                return `${days}天 ${hours}小时 ${minutes}分钟`;
            } else if (hours > 0) {
                return `${hours}小时 ${minutes}分钟`;
            } else {
                return `${minutes}分钟`;
            }
        },

        // ===== Setup Events =====
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

        // ===== Refresh =====
        refresh: async function() {
            await Promise.all([
                this.loadInfo(),
                this.updateStatCards()
            ]);
        }
    };

    // Register module
    if (window.DashboardApp) {
        window.DashboardApp.registerModule('home', HomeModule);
    }

    // Expose to window for backward compatibility
    window.loadInfo = HomeModule.loadInfo.bind(HomeModule);
    window.refreshHome = HomeModule.refresh.bind(HomeModule);

})();
