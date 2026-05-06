/**
 * Dashboard State Management - Centralized state management
 */
(function() {
    'use strict';

    window.DashboardState = window.DashboardState || {};

    // ===== State Storage =====
    const state = {
        // App state
        app: {
            initialized: false,
            currentTab: 'overview',
            currentLocale: 'zh-CN',
            loading: false
        },

        // Auth state
        auth: {
            token: null,
            isAuthenticated: false,
            authMode: 'None'
        },

        // Data state
        data: {
            info: null,
            clusters: [],
            routes: [],
            logs: []
        },

        // Filter state
        filters: {
            clusters: {
                search: '',
                health: 'all',
                editable: 'all',
                source: 'all'
            },
            routes: {
                search: '',
                clusterId: '',
                source: 'all',
                method: 'all'
            },
            logs: {
                search: '',
                routeId: '',
                status: 'all',
                level: 'all',
                autoRefresh: false,
                refreshInterval: 3000, // 3 seconds
                maxCount: 100
            }
        },

        // UI state
        ui: {
            expandedClusters: new Set(),
            expandedRoutes: new Set(),
            expandedLogs: new Set(),
            modals: {},
            notifications: []
        },

        // Editor state
        editor: {
            mode: 'form', // 'form' or 'json'
            draft: null,
            validation: null,
            history: []
        }
    };

    // ===== State Subscribers =====
    const subscribers = {};

    // ===== Initialization =====
    window.DashboardState.init = function() {
        // Restore from localStorage
        this.restoreState();
        
        // Setup auto-save
        setInterval(() => this.saveState(), 5000);
        
        console.log('[State] Initialized');
    };

    // ===== Get State =====
    window.DashboardState.get = function(path) {
        if (!path) return state;
        
        const keys = path.split('.');
        let result = state;
        
        for (const key of keys) {
            if (result === null || result === undefined) {
                return undefined;
            }
            result = result[key];
        }
        
        return result;
    };

    // ===== Set State =====
    window.DashboardState.set = function(path, value, notify = true) {
        const keys = path.split('.');
        let target = state;
        
        // Navigate to parent object
        for (let i = 0; i < keys.length - 1; i++) {
            const key = keys[i];
            if (!target[key]) {
                target[key] = {};
            }
            target = target[key];
        }
        
        // Set value
        const lastKey = keys[keys.length - 1];
        const oldValue = target[lastKey];
        target[lastKey] = value;
        
        // Notify subscribers
        if (notify) {
            this.notify(path, value, oldValue);
        }
        
        return true;
    };

    // ===== Subscribe to State Changes =====
    window.DashboardState.subscribe = function(path, callback) {
        if (!subscribers[path]) {
            subscribers[path] = [];
        }
        subscribers[path].push(callback);
        
        // Return unsubscribe function
        return () => {
            subscribers[path] = subscribers[path].filter(cb => cb !== callback);
        };
    };

    // ===== Notify Subscribers =====
    window.DashboardState.notify = function(path, newValue, oldValue) {
        if (!subscribers[path]) return;
        
        subscribers[path].forEach(callback => {
            try {
                callback(newValue, oldValue, path);
            } catch (error) {
                console.error('[State] Subscriber error:', error);
            }
        });
    };

    // ===== Filter Helpers =====
    window.DashboardState.getFilteredClusters = function() {
        const { clusters } = state.data;
        const { search, health, editable, source } = state.filters.clusters;
        
        return clusters.filter(cluster => {
            // Search filter - search in clusterId AND destinations addresses
            if (search) {
                const searchLower = search.toLowerCase();
                const matchClusterId = cluster.clusterId.toLowerCase().includes(searchLower);
                // Also search in destination addresses
                let matchAddress = false;
                if (cluster.destinations && Array.isArray(cluster.destinations)) {
                    matchAddress = cluster.destinations.some(dest => 
                        dest.address && dest.address.toLowerCase().includes(searchLower)
                    );
                }
                if (!matchClusterId && !matchAddress) {
                    return false;
                }
            }
            
            // Health filter - use healthyCount/unknownCount/unhealthyCount
            if (health !== 'all') {
                if (health === 'Healthy' && cluster.healthyCount === 0) {
                    return false;
                } else if (health === 'Unknown' && cluster.unknownCount === 0) {
                    return false;
                } else if (health === 'Unhealthy' && cluster.unhealthyCount === 0) {
                    return false;
                }
            }
            
            // Editable filter
            if (editable !== 'all' && cluster.isEditable !== (editable === 'editable')) {
                return false;
            }
            
            return true;
        });
    };

    window.DashboardState.getFilteredRoutes = function() {
        const { routes } = state.data;
        const { search, clusterId, source, method } = state.filters.routes;
        
        return routes.filter(route => {
            // Search filter - search in routeId, clusterId, AND match path
            if (search) {
                const searchLower = search.toLowerCase();
                const matchRouteId = route.routeId.toLowerCase().includes(searchLower);
                const matchClusterId = route.clusterId && route.clusterId.toLowerCase().includes(searchLower);
                // Also search in match path
                let matchPath = false;
                if (route.matchPath && route.matchPath.toLowerCase().includes(searchLower)) {
                    matchPath = true;
                }
                if (!matchRouteId && !matchClusterId && !matchPath) {
                    return false;
                }
            }
            
            // Cluster filter
            if (clusterId && route.clusterId !== clusterId) {
                return false;
            }
            
            // Method filter
            if (method !== 'all') {
                if (!route.methods || !route.methods.includes(method)) {
                    return false;
                }
            }
            
            return true;
        });
    };

    window.DashboardState.getFilteredLogs = function() {
        const { logs } = state.data;
        const { search, routeId, status, level, maxCount } = state.filters.logs;
        
        let filtered = logs.filter(log => {
            // Search filter
            if (search && !log.message?.toLowerCase().includes(search.toLowerCase())) {
                return false;
            }
            
            // Route filter
            if (routeId && log.routeId !== routeId) {
                return false;
            }
            
            // Status filter
            if (status !== 'all') {
                const statusCode = log.statusCode || 0;
                if (status === 'success' && (statusCode < 200 || statusCode >= 300)) {
                    return false;
                }
                if (status === 'error' && statusCode < 400) {
                    return false;
                }
            }
            
            // Level filter
            if (level !== 'all' && log.level !== level) {
                return false;
            }
            
            return true;
        });
        
        // Limit count
        if (maxCount && filtered.length > maxCount) {
            filtered = filtered.slice(0, maxCount);
        }
        
        return filtered;
    };

    // ===== Persistence =====
    window.DashboardState.saveState = function() {
        try {
            const toSave = {
                filters: state.filters,
                ui: {
                    expandedClusters: Array.from(state.ui.expandedClusters),
                    expandedRoutes: Array.from(state.ui.expandedRoutes)
                },
                editor: {
                    mode: state.editor.mode
                }
            };
            
            localStorage.setItem('dashboard_state', JSON.stringify(toSave));
        } catch (error) {
            console.error('[State] Save failed:', error);
        }
    };

    window.DashboardState.restoreState = function() {
        try {
            const saved = localStorage.getItem('dashboard_state');
            if (!saved) return;
            
            const parsed = JSON.parse(saved);
            
            // Restore filters
            if (parsed.filters) {
                Object.assign(state.filters, parsed.filters);
            }
            
            // Restore UI state
            if (parsed.ui) {
                state.ui.expandedClusters = new Set(parsed.ui.expandedClusters || []);
                state.ui.expandedRoutes = new Set(parsed.ui.expandedRoutes || []);
            }
            
            // Restore editor state
            if (parsed.editor) {
                Object.assign(state.editor, parsed.editor);
            }
            
            console.log('[State] Restored from localStorage');
        } catch (error) {
            console.error('[State] Restore failed:', error);
        }
    };

    // ===== Clear State =====
    window.DashboardState.clear = function() {
        state.data.clusters = [];
        state.data.routes = [];
        state.data.logs = [];
        state.ui.expandedClusters.clear();
        state.ui.expandedRoutes.clear();
        state.ui.expandedLogs.clear();
        
        console.log('[State] Cleared');
    };

    // ===== Refresh Data with UI Preservation =====
    window.DashboardState.refreshData = async function(preserveUI = true) {
        // Preserve UI state before refresh
        const preservedState = preserveUI ? {
            expandedClusters: new Set(state.ui.expandedClusters),
            expandedRoutes: new Set(state.ui.expandedRoutes),
            expandedLogs: new Set(state.ui.expandedLogs)
        } : null;

        try {
            // Reload data from API
            if (window.DashboardApi) {
                const [clusters, routes] = await Promise.all([
                    window.DashboardApi.endpoints.getClusters(),
                    window.DashboardApi.endpoints.getRoutes()
                ]);

                state.data.clusters = clusters.data || clusters || [];
                state.data.routes = routes.data || routes || [];
            }

            // Restore UI state
            if (preservedState) {
                state.ui.expandedClusters = preservedState.expandedClusters;
                state.ui.expandedRoutes = preservedState.expandedRoutes;
                state.ui.expandedLogs = preservedState.expandedLogs;
                
                // Notify subscribers
                this.notify('ui.expandedClusters', state.ui.expandedClusters);
                this.notify('ui.expandedRoutes', state.ui.expandedRoutes);
            }

            console.log('[State] Data refreshed');
        } catch (error) {
            console.error('[State] Refresh failed:', error);
            throw error;
        }
    };

    // ===== Debug =====
    window.DashboardState.debug = function() {
        console.log('[State] Current state:', JSON.parse(JSON.stringify(state)));
    };

})();
