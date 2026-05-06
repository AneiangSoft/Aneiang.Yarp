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
                const container = window.DashboardDOM.safe('#info-container');
                if (!container) return;

                window.DashboardDOM.showLoading(container, __('index.loading'));

                const info = await window.DashboardApi.endpoints.getInfo();
                
                // Update state
                window.DashboardState.set('data.info', info);

                // Render info
                this.renderInfo(info);

                // Update stat cards
                await this.updateStatCards();

            } catch (error) {
                console.error('[Home] Load info failed:', error);
                const container = window.DashboardDOM.safe('#info-container');
                if (container) {
                    window.DashboardDOM.showError(container, __('index.error.loadFailed'));
                }
            }
        },

        // ===== Render Gateway Info =====
        renderInfo: function(info) {
            // Version
            const versionEl = window.DashboardDOM.safe('#info-version');
            if (versionEl) versionEl.textContent = info.version || '-';

            // Environment
            const envEl = window.DashboardDOM.safe('#info-env');
            if (envEl) envEl.textContent = info.environmentName || '-';

            // Start time
            const startEl = window.DashboardDOM.safe('#info-start');
            if (startEl && info.startTime) {
                startEl.textContent = window.DashboardI18n.formatDateTime(new Date(info.startTime));
            }

            // Uptime
            const uptimeEl = window.DashboardDOM.safe('#info-uptime');
            if (uptimeEl && info.uptime) {
                uptimeEl.textContent = this.formatUptime(info.uptime);
            }

            // Memory
            const memoryEl = window.DashboardDOM.safe('#info-memory');
            if (memoryEl && info.memoryBytes !== null) {
                memoryEl.textContent = window.DashboardI18n.formatBytes(info.memoryBytes);
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
                
                const healthy = (clusters || []).filter(c => c.healthStatus === 'Healthy').length;
                const unknown = (clusters || []).filter(c => c.healthStatus === 'Unknown' || !c.healthStatus).length;
                const unhealthy = (clusters || []).filter(c => c.healthStatus === 'Unhealthy').length;

                // Update DOM
                const clustersEl = window.DashboardDOM.safe('#stat-clusters');
                if (clustersEl) clustersEl.textContent = clusterCount;

                const healthyEl = window.DashboardDOM.safe('#stat-healthy');
                if (healthyEl) healthyEl.textContent = `${healthy} / ${unknown} / ${unhealthy}`;

                const routesEl = window.DashboardDOM.safe('#stat-routes');
                if (routesEl) routesEl.textContent = routeCount;

            } catch (error) {
                console.error('[Home] Update stats failed:', error);
            }
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
