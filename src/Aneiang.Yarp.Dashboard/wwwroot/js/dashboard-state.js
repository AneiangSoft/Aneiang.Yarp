/**
 * Dashboard State Management - Aneiang.Yarp Gateway Dashboard
 * Centralized state management for filters, expanded items, and UI state
 */
(function() {
    'use strict';

    // ===== State Manager =====
    var DashboardState = {
        // ===== Cluster State =====
        clusters: {
            data: [],
            filters: {
                search: '',
                editableOnly: false,
                healthStatus: 'all' // all, healthy, unhealthy, unknown
            },
            expanded: new Set(),
            loading: false
        },

        // ===== Route State =====
        routes: {
            data: [],
            filters: {
                search: '',
                clusterId: '',
                source: 'all', // all, dynamic, static
                method: 'all',
                editableOnly: false
            },
            expanded: new Set(),
            loading: false
        },

        // ===== Log State =====
        logs: {
            data: [],
            filters: {
                search: '',
                level: 'all', // all, info, warning, error, critical
                routeId: '',
                statusCode: 'all',
                gatewayOnly: false
            },
            expanded: new Set(),
            loading: false,
            polling: false,
            pollInterval: 3000
        },

        // ===== UI State =====
        ui: {
            currentTab: 'overview',
            loading: false,
            error: null,
            notifications: []
        },

        // ===== State Management Methods =====
        
        /**
         * Get cluster filter state
         */
        getClusterFilter(key) {
            return this.clusters.filters[key];
        },

        /**
         * Set cluster filter
         */
        setClusterFilter(key, value) {
            this.clusters.filters[key] = value;
            this.clusters.expanded.clear();
        },

        /**
         * Toggle cluster expansion
         */
        toggleCluster(clusterId) {
            if (this.clusters.expanded.has(clusterId)) {
                this.clusters.expanded.delete(clusterId);
            } else {
                this.clusters.expanded.add(clusterId);
            }
        },

        /**
         * Check if cluster is expanded
         */
        isClusterExpanded(clusterId) {
            return this.clusters.expanded.has(clusterId);
        },

        /**
         * Get route filter state
         */
        getRouteFilter(key) {
            return this.routes.filters[key];
        },

        /**
         * Set route filter
         */
        setRouteFilter(key, value) {
            this.routes.filters[key] = value;
            this.routes.expanded.clear();
        },

        /**
         * Toggle route expansion
         */
        toggleRoute(routeId) {
            if (this.routes.expanded.has(routeId)) {
                this.routes.expanded.delete(routeId);
            } else {
                this.routes.expanded.add(routeId);
            }
        },

        /**
         * Check if route is expanded
         */
        isRouteExpanded(routeId) {
            return this.routes.expanded.has(routeId);
        },

        /**
         * Get log filter state
         */
        getLogFilter(key) {
            return this.logs.filters[key];
        },

        /**
         * Set log filter
         */
        setLogFilter(key, value) {
            this.logs.filters[key] = value;
        },

        /**
         * Toggle log expansion
         */
        toggleLog(logKey) {
            if (this.logs.expanded.has(logKey)) {
                this.logs.expanded.delete(logKey);
            } else {
                this.logs.expanded.add(logKey);
            }
        },

        /**
         * Check if log is expanded
         */
        isLogExpanded(logKey) {
            return this.logs.expanded.has(logKey);
        },

        /**
         * Reset all filters for a section
         */
        resetFilters(section) {
            if (section === 'clusters') {
                this.clusters.filters = {
                    search: '',
                    editableOnly: false,
                    healthStatus: 'all'
                };
            } else if (section === 'routes') {
                this.routes.filters = {
                    search: '',
                    clusterId: '',
                    source: 'all',
                    method: 'all',
                    editableOnly: false
                };
            } else if (section === 'logs') {
                this.logs.filters = {
                    search: '',
                    level: 'all',
                    routeId: '',
                    statusCode: 'all',
                    gatewayOnly: false
                };
            }
        },

        /**
         * Get filtered clusters
         */
        getFilteredClusters() {
            var self = this;
            var filters = this.clusters.filters;
            var data = this.clusters.data;

            return data.filter(function(c) {
                // Search filter
                if (filters.search && !c.clusterId.toLowerCase().includes(filters.search.toLowerCase())) {
                    return false;
                }

                // Editable filter
                if (filters.editableOnly && !c.isEditable) {
                    return false;
                }

                // Health status filter
                if (filters.healthStatus !== 'all') {
                    var hasHealthy = c.healthyCount > 0;
                    var hasUnhealthy = c.unhealthyCount > 0;
                    var hasUnknown = c.unknownCount > 0;

                    if (filters.healthStatus === 'healthy' && !hasHealthy) return false;
                    if (filters.healthStatus === 'unhealthy' && !hasUnhealthy) return false;
                    if (filters.healthStatus === 'unknown' && !hasUnknown) return false;
                }

                return true;
            });
        },

        /**
         * Get filtered routes
         */
        getFilteredRoutes() {
            var self = this;
            var filters = this.routes.filters;
            var data = this.routes.data;

            return data.filter(function(r) {
                // Search filter
                if (filters.search) {
                    var searchLower = filters.search.toLowerCase();
                    var matchesRouteId = r.routeId && r.routeId.toLowerCase().includes(searchLower);
                    var matchesClusterId = r.clusterId && r.clusterId.toLowerCase().includes(searchLower);
                    var matchesPath = r.path && r.path.toLowerCase().includes(searchLower);
                    if (!matchesRouteId && !matchesClusterId && !matchesPath) {
                        return false;
                    }
                }

                // Cluster ID filter
                if (filters.clusterId && r.clusterId !== filters.clusterId) {
                    return false;
                }

                // Source filter
                if (filters.source !== 'all' && r.source !== filters.source) {
                    return false;
                }

                // Method filter
                if (filters.method !== 'all') {
                    if (!r.methods || !r.methods.includes(filters.method)) {
                        return false;
                    }
                }

                // Editable filter
                if (filters.editableOnly && !r.isEditable) {
                    return false;
                }

                return true;
            });
        },

        /**
         * Get filtered logs
         */
        getFilteredLogs() {
            var self = this;
            var filters = this.logs.filters;
            var data = this.logs.data;

            return data.filter(function(e) {
                // Search filter
                if (filters.search) {
                    var searchLower = filters.search.toLowerCase();
                    var matchesMessage = e.message && e.message.toLowerCase().includes(searchLower);
                    var matchesRouteId = e.routeId && e.routeId.toLowerCase().includes(searchLower);
                    if (!matchesMessage && !matchesRouteId) {
                        return false;
                    }
                }

                // Level filter
                if (filters.level !== 'all' && e.level !== filters.level) {
                    return false;
                }

                // Route ID filter
                if (filters.routeId && e.routeId !== filters.routeId) {
                    return false;
                }

                // Status code filter
                if (filters.statusCode !== 'all') {
                    if (e.statusCode !== parseInt(filters.statusCode)) {
                        return false;
                    }
                }

                // Gateway only filter
                if (filters.gatewayOnly && e.category !== 'Gateway') {
                    return false;
                }

                return true;
            });
        }
    };

    // ===== Export to global =====
    window.dashboardState = DashboardState;

})();
