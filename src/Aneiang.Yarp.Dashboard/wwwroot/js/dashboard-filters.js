/**
 * Dashboard Filters - Aneiang.Yarp Gateway Dashboard
 * Filter toolbar components for clusters, routes, and logs
 */
(function() {
    'use strict';

    var __ = window.__ || function(key) { return key; };

    // ===== Filter Toolbar Renderer =====
    var FilterToolbar = {
        /**
         * Render cluster filter toolbar
         */
        renderClusterToolbar() {
            var state = window.dashboardState;
            var filters = state.clusters.filters;

            var html = '<div class="filter-toolbar mb-3 p-3 bg-light rounded">';
            html += '<div class="row g-2 align-items-center">';
            
            // Search input
            html += '<div class="col-md-4">';
            html += '<div class="input-group input-group-sm">';
            html += '<span class="input-group-text"><i class="bi bi-search"></i></span>';
            html += '<input type="text" class="form-control" id="cluster-search" placeholder="Search cluster ID..." value="' + window.escapeHtml(filters.search) + '">';
            html += '</div>';
            html += '</div>';

            // Health status filter
            html += '<div class="col-md-3">';
            html += '<select class="form-select form-select-sm" id="cluster-health-filter">';
            html += '<option value="all"' + (filters.healthStatus === 'all' ? ' selected' : '') + '>All Health Status</option>';
            html += '<option value="healthy"' + (filters.healthStatus === 'healthy' ? ' selected' : '') + '>Healthy</option>';
            html += '<option value="unhealthy"' + (filters.healthStatus === 'unhealthy' ? ' selected' : '') + '>Unhealthy</option>';
            html += '<option value="unknown"' + (filters.healthStatus === 'unknown' ? ' selected' : '') + '>Unknown</option>';
            html += '</select>';
            html += '</div>';

            // Editable filter
            html += '<div class="col-md-3">';
            html += '<div class="form-check form-switch">';
            html += '<input class="form-check-input" type="checkbox" id="cluster-editable-only"' + (filters.editableOnly ? ' checked' : '') + '>';
            html += '<label class="form-check-label" for="cluster-editable-only">Editable Only</label>';
            html += '</div>';
            html += '</div>';

            // Reset button
            html += '<div class="col-md-2 text-end">';
            html += '<button class="btn btn-sm btn-outline-secondary" id="cluster-reset-filters">';
            html += '<i class="bi bi-x-circle"></i> Reset';
            html += '</button>';
            html += '</div>';

            html += '</div>';
            html += '</div>';

            return html;
        },

        /**
         * Render route filter toolbar
         */
        renderRouteToolbar() {
            var state = window.dashboardState;
            var filters = state.routes.filters;

            var html = '<div class="filter-toolbar mb-3 p-3 bg-light rounded">';
            html += '<div class="row g-2 align-items-center">';
            
            // Search input
            html += '<div class="col-md-3">';
            html += '<div class="input-group input-group-sm">';
            html += '<span class="input-group-text"><i class="bi bi-search"></i></span>';
            html += '<input type="text" class="form-control" id="route-search" placeholder="Search route/cluster/path..." value="' + window.escapeHtml(filters.search) + '">';
            html += '</div>';
            html += '</div>';

            // Source filter
            html += '<div class="col-md-2">';
            html += '<select class="form-select form-select-sm" id="route-source-filter">';
            html += '<option value="all"' + (filters.source === 'all' ? ' selected' : '') + '>All Sources</option>';
            html += '<option value="dynamic"' + (filters.source === 'dynamic' ? ' selected' : '') + '>Dynamic</option>';
            html += '<option value="static"' + (filters.source === 'static' ? ' selected' : '') + '>Static</option>';
            html += '</select>';
            html += '</div>';

            // Method filter
            html += '<div class="col-md-2">';
            html += '<select class="form-select form-select-sm" id="route-method-filter">';
            html += '<option value="all"' + (filters.method === 'all' ? ' selected' : '') + '>All Methods</option>';
            html += '<option value="GET"' + (filters.method === 'GET' ? ' selected' : '') + '>GET</option>';
            html += '<option value="POST"' + (filters.method === 'POST' ? ' selected' : '') + '>POST</option>';
            html += '<option value="PUT"' + (filters.method === 'PUT' ? ' selected' : '') + '>PUT</option>';
            html += '<option value="DELETE"' + (filters.method === 'DELETE' ? ' selected' : '') + '>DELETE</option>';
            html += '<option value="PATCH"' + (filters.method === 'PATCH' ? ' selected' : '') + '>PATCH</option>';
            html += '</select>';
            html += '</div>';

            // Editable filter
            html += '<div class="col-md-3">';
            html += '<div class="form-check form-switch">';
            html += '<input class="form-check-input" type="checkbox" id="route-editable-only"' + (filters.editableOnly ? ' checked' : '') + '>';
            html += '<label class="form-check-label" for="route-editable-only">Editable Only</label>';
            html += '</div>';
            html += '</div>';

            // Reset button
            html += '<div class="col-md-2 text-end">';
            html += '<button class="btn btn-sm btn-outline-secondary" id="route-reset-filters">';
            html += '<i class="bi bi-x-circle"></i> Reset';
            html += '</button>';
            html += '</div>';

            html += '</div>';
            html += '</div>';

            return html;
        },

        /**
         * Render log filter toolbar
         */
        renderLogToolbar() {
            var state = window.dashboardState;
            var filters = state.logs.filters;

            var html = '<div class="filter-toolbar mb-3 p-3 bg-light rounded">';
            html += '<div class="row g-2 align-items-center">';
            
            // Search input
            html += '<div class="col-md-3">';
            html += '<div class="input-group input-group-sm">';
            html += '<span class="input-group-text"><i class="bi bi-search"></i></span>';
            html += '<input type="text" class="form-control" id="log-search" placeholder="Search message/route..." value="' + window.escapeHtml(filters.search) + '">';
            html += '</div>';
            html += '</div>';

            // Level filter
            html += '<div class="col-md-2">';
            html += '<select class="form-select form-select-sm" id="log-level-filter">';
            html += '<option value="all"' + (filters.level === 'all' ? ' selected' : '') + '>All Levels</option>';
            html += '<option value="Information"' + (filters.level === 'Information' ? ' selected' : '') + '>Info</option>';
            html += '<option value="Warning"' + (filters.level === 'Warning' ? ' selected' : '') + '>Warning</option>';
            html += '<option value="Error"' + (filters.level === 'Error' ? ' selected' : '') + '>Error</option>';
            html += '<option value="Critical"' + (filters.level === 'Critical' ? ' selected' : '') + '>Critical</option>';
            html += '</select>';
            html += '</div>';

            // Gateway only filter
            html += '<div class="col-md-2">';
            html += '<div class="form-check form-switch">';
            html += '<input class="form-check-input" type="checkbox" id="log-gateway-only"' + (filters.gatewayOnly ? ' checked' : '') + '>';
            html += '<label class="form-check-label" for="log-gateway-only">Gateway Only</label>';
            html += '</div>';
            html += '</div>';

            // Polling controls
            html += '<div class="col-md-3">';
            html += '<div class="btn-group btn-group-sm" role="group">';
            html += '<button type="button" class="btn btn-outline-primary" id="log-polling-toggle">';
            html += '<i class="bi bi-arrow-repeat"></i> <span id="log-polling-status">Auto-Poll: OFF</span>';
            html += '</button>';
            html += '</div>';
            html += '</div>';

            // Action buttons
            html += '<div class="col-md-2 text-end">';
            html += '<button class="btn btn-sm btn-outline-danger me-1" id="log-clear-all">';
            html += '<i class="bi bi-trash"></i> Clear';
            html += '</button>';
            html += '<button class="btn btn-sm btn-outline-secondary" id="log-reset-filters">';
            html += '<i class="bi bi-x-circle"></i> Reset';
            html += '</button>';
            html += '</div>';

            html += '</div>';
            html += '</div>';

            return html;
        },

        /**
         * Initialize cluster toolbar event handlers
         */
        initClusterToolbar() {
            var state = window.dashboardState;
            var self = this;

            // Search input
            var searchInput = document.getElementById('cluster-search');
            if (searchInput) {
                var debounceTimer;
                searchInput.addEventListener('input', function() {
                    clearTimeout(debounceTimer);
                    debounceTimer = setTimeout(function() {
                        state.setClusterFilter('search', searchInput.value);
                        if (window.renderClusters) window.renderClusters();
                    }, 300);
                });
            }

            // Health filter
            var healthFilter = document.getElementById('cluster-health-filter');
            if (healthFilter) {
                healthFilter.addEventListener('change', function() {
                    state.setClusterFilter('healthStatus', healthFilter.value);
                    if (window.renderClusters) window.renderClusters();
                });
            }

            // Editable filter
            var editableFilter = document.getElementById('cluster-editable-only');
            if (editableFilter) {
                editableFilter.addEventListener('change', function() {
                    state.setClusterFilter('editableOnly', editableFilter.checked);
                    if (window.renderClusters) window.renderClusters();
                });
            }

            // Reset button
            var resetBtn = document.getElementById('cluster-reset-filters');
            if (resetBtn) {
                resetBtn.addEventListener('click', function() {
                    state.resetFilters('clusters');
                    if (window.renderClusters) window.renderClusters();
                });
            }
        },

        /**
         * Initialize route toolbar event handlers
         */
        initRouteToolbar() {
            var state = window.dashboardState;
            var self = this;

            // Search input
            var searchInput = document.getElementById('route-search');
            if (searchInput) {
                var debounceTimer;
                searchInput.addEventListener('input', function() {
                    clearTimeout(debounceTimer);
                    debounceTimer = setTimeout(function() {
                        state.setRouteFilter('search', searchInput.value);
                        if (window.renderRoutes) window.renderRoutes();
                    }, 300);
                });
            }

            // Source filter
            var sourceFilter = document.getElementById('route-source-filter');
            if (sourceFilter) {
                sourceFilter.addEventListener('change', function() {
                    state.setRouteFilter('source', sourceFilter.value);
                    if (window.renderRoutes) window.renderRoutes();
                });
            }

            // Method filter
            var methodFilter = document.getElementById('route-method-filter');
            if (methodFilter) {
                methodFilter.addEventListener('change', function() {
                    state.setRouteFilter('method', methodFilter.value);
                    if (window.renderRoutes) window.renderRoutes();
                });
            }

            // Editable filter
            var editableFilter = document.getElementById('route-editable-only');
            if (editableFilter) {
                editableFilter.addEventListener('change', function() {
                    state.setRouteFilter('editableOnly', editableFilter.checked);
                    if (window.renderRoutes) window.renderRoutes();
                });
            }

            // Reset button
            var resetBtn = document.getElementById('route-reset-filters');
            if (resetBtn) {
                resetBtn.addEventListener('click', function() {
                    state.resetFilters('routes');
                    if (window.renderRoutes) window.renderRoutes();
                });
            }
        },

        /**
         * Initialize log toolbar event handlers
         */
        initLogToolbar() {
            var state = window.dashboardState;
            var self = this;

            // Search input
            var searchInput = document.getElementById('log-search');
            if (searchInput) {
                var debounceTimer;
                searchInput.addEventListener('input', function() {
                    clearTimeout(debounceTimer);
                    debounceTimer = setTimeout(function() {
                        state.setLogFilter('search', searchInput.value);
                        if (window.renderLogs) window.renderLogs();
                    }, 300);
                });
            }

            // Level filter
            var levelFilter = document.getElementById('log-level-filter');
            if (levelFilter) {
                levelFilter.addEventListener('change', function() {
                    state.setLogFilter('level', levelFilter.value);
                    if (window.renderLogs) window.renderLogs();
                });
            }

            // Gateway only filter
            var gatewayFilter = document.getElementById('log-gateway-only');
            if (gatewayFilter) {
                gatewayFilter.addEventListener('change', function() {
                    state.setLogFilter('gatewayOnly', gatewayFilter.checked);
                    if (window.renderLogs) window.renderLogs();
                });
            }

            // Polling toggle
            var pollingBtn = document.getElementById('log-polling-toggle');
            if (pollingBtn) {
                pollingBtn.addEventListener('click', function() {
                    if (window.toggleLogPolling) window.toggleLogPolling();
                });
            }

            // Clear logs button
            var clearBtn = document.getElementById('log-clear-all');
            if (clearBtn) {
                clearBtn.addEventListener('click', function() {
                    if (window.clearLogsConfirm) window.clearLogsConfirm();
                });
            }

            // Reset button
            var resetBtn = document.getElementById('log-reset-filters');
            if (resetBtn) {
                resetBtn.addEventListener('click', function() {
                    state.resetFilters('logs');
                    if (window.renderLogs) window.renderLogs();
                });
            }
        }
    };

    // ===== Export to global =====
    window.dashboardFilters = FilterToolbar;

})();
