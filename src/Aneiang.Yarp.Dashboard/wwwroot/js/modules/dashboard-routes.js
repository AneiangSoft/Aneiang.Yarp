/**
 * Dashboard Routes Module - Route management and configuration
 */
(function() {
    'use strict';

    const RoutesModule = {
        name: 'routes',
        initialized: false,

        // ===== Initialization =====
        init: async function() {
            if (this.initialized) return;

            console.log('[Routes] Initializing...');

            this.setupEvents();

            this.initialized = true;
            console.log('[Routes] Initialized');
        },

        // ===== Load Routes =====
        loadRoutes: async function(forceReload) {
            try {
                const container = window.DashboardDOM.safe('#route-tbody');
                if (!container) return;

                const cached = window.DashboardState.get('data.routes');

                // Use cached data if already loaded and not forcing reload
                if (!forceReload && Array.isArray(cached) && cached.length > 0) {
                    window.DashboardState.set('data.routes', cached);
                    // Ensure clusters are also loaded for Add Route modal
                    if (!window.DashboardState.get('data.clusters')?.length) {
                        await this.ensureClustersLoaded();
                    }
                    this.renderRoutes();
                    return;
                }

                window.DashboardDOM.showLoading(container, __('index.route.loading'));

                // Load both routes and clusters in parallel (clusters needed for Add Route modal)
                const [routes, clusters] = await Promise.all([
                    window.DashboardApi.endpoints.getRoutes(),
                    window.DashboardApi.endpoints.getClusters()
                ]);

                // Update state
                window.DashboardState.set('data.routes', routes || []);
                window.DashboardState.set('data.clusters', clusters || []);

                // Render routes
                this.renderRoutes();

            } catch (error) {
                console.error('[Routes] Load failed:', error);
                const container = window.DashboardDOM.safe('#route-tbody');
                if (container) {
                    window.DashboardDOM.showError(container, __('index.route.loadFailed'));
                }
            }
        },

        // ===== Ensure clusters are loaded (for Add Route modal) =====
        ensureClustersLoaded: async function() {
            try {
                const clusters = await window.DashboardApi.endpoints.getClusters();
                window.DashboardState.set('data.clusters', clusters || []);
            } catch (e) {
                console.warn('[Routes] Failed to load clusters:', e);
            }
        },

        // ===== Render Routes =====
        renderRoutes: function() {
            const state = window.DashboardState;
            const routes = state.getFilteredRoutes();
                    
            // Render filter toolbar (only once, then update counts)
            this.renderFilterToolbar();

            // Render table view
            const tbody = window.DashboardDOM.safe('#route-tbody');
            if (tbody) {
                window.DashboardDOM.clear(tbody);
                        
                if (routes.length === 0) {
                    const emptyRow = document.createElement('tr');
                    emptyRow.innerHTML = `
                        <td colspan="7" class="text-center py-5">
                            <div class="empty-state">
                                <i class="bi bi-signpost-split" style="font-size: 2.5rem; opacity: 0.4; color: #64748b;"></i>
                                <div class="mt-3 text-muted" style="font-size: 14px;">${__('index.route.empty')}</div>
                                <div class="mt-2 text-muted small">${__('index.route.emptyHelp')}</div>
                            </div>
                        </td>
                    `; 
                    tbody.appendChild(emptyRow);
                } else {
                    this.renderRouteRows(routes, tbody);
                }
            }
        
            // Update refresh time
            this.updateRefreshTime();
        },
        
        // ===== Render Filter Toolbar =====
        renderFilterToolbar: function() {
            const container = window.DashboardDOM.safe('#route-filter-container');
            if (!container) return;
        
            // Check if toolbar already exists - only update counts, don't recreate
            const existingToolbar = document.getElementById('route-search-input');
            if (existingToolbar) {
                this._updateFilterCounts();
                return;
            }
                    
            // First time - create toolbar
            const state = window.DashboardState;
            const allRoutes = state.get('data.routes') || [];
            const methodCounts = this._getMethodCounts(allRoutes);
            const sourceCounts = this._getSourceCounts(allRoutes);
        
            container.innerHTML = `
                <div class="card-body py-2 border-bottom">
                    <div class="row g-2 align-items-center">
                        <div class="col">
                            <div class="input-group input-group-sm">
                                <span class="input-group-text bg-light border-end-0">
                                    <i class="bi bi-search text-muted"></i>
                                </span>
                                <input type="text" class="form-control border-start-0" id="route-search-input" 
                                       placeholder="${__('index.route.search')}" 
                                       autocomplete="off">
                            </div>
                        </div>
                        <div class="col-auto">
                            <select class="form-select form-select-sm" id="route-method-select" style="width:100px;">
                                <option value="all">${__('index.route.method.all')} (${methodCounts.all})</option>
                                <option value="GET">GET (${methodCounts.GET})</option>
                                <option value="POST">POST (${methodCounts.POST})</option>
                                <option value="PUT">PUT (${methodCounts.PUT})</option>
                                <option value="DELETE">DELETE (${methodCounts.DELETE})</option>
                            </select>
                        </div>
                        <div class="col-auto">
                            <select class="form-select form-select-sm" id="route-source-select" style="width:110px;">
                                <option value="all">${__('index.source.all')} (${sourceCounts.all})</option>
                                <option value="config">${__('index.source.config')} (${sourceCounts.config})</option>
                                <option value="dynamic">${__('index.source.dynamic')} (${sourceCounts.dynamic})</option>
                                <option value="dashboard">${__('index.source.dashboard')} (${sourceCounts.dashboard})</option>
                                <option value="auto-register">${__('index.source.autoRegister')} (${sourceCounts['auto-register']})</option>
                            </select>
                        </div>
                        <div class="col-auto">
                            <span class="text-muted small" id="route-search-result"></span>
                        </div>
                        <div class="col-auto">
                            <div class="btn-group">
                                <button class="btn btn-sm btn-outline-secondary" id="route-refresh-btn" title="${__('index.btn.refresh')}">
                                    <i class="bi bi-arrow-clockwise"></i>
                                </button>
                                <button class="btn btn-sm btn-outline-danger" id="route-clear-btn" title="${__('index.search.clear')}" style="display:none;">
                                    <i class="bi bi-x-circle"></i>
                                </button>
                                <button class="btn btn-sm btn-outline-secondary" id="route-add-btn" title="${__('modal.addRoute') || 'Add Route'}">
                                    <i class="bi bi-plus-circle"></i>
                                </button>
                            </div>
                        </div>
                    </div>
                </div>
            `;
        
            // Bind event handlers (only once)
            this._bindFilterEvents();
        },
                
        // ===== Update Filter Counts (called after search, not recreating toolbar) =====
        _updateFilterCounts: function() {
            const state = window.DashboardState;
            const allRoutes = state.get('data.routes') || []; 
            const filteredRoutes = state.getFilteredRoutes();
            const searchValue = state.get('filters.routes.search') || ''; 
            const methodValue = state.get('filters.routes.method') || 'all';
            
            // Update display count in header
            const displayCountEl = document.getElementById('route-display-count');
            const totalCountEl = document.getElementById('route-total-count');
            if (displayCountEl) displayCountEl.textContent = filteredRoutes.length;
            if (totalCountEl) totalCountEl.textContent = allRoutes.length;
                    
            // Update result count
            const resultEl = document.getElementById('route-search-result');
            if (resultEl) {
                if (searchValue || methodValue !== 'all') {
                    resultEl.textContent = `${filteredRoutes.length}/${allRoutes.length}`;
                    resultEl.style.display = '';
                } else {
                    resultEl.textContent = '';
                }
            }
                    
            // Update clear button visibility
            const clearBtn = document.getElementById('route-clear-btn');
            if (clearBtn) {
                clearBtn.style.display = searchValue ? '' : 'none';
            }
                    
            // Update method select value (don't recreate)
            const methodSelect = document.getElementById('route-method-select');
            if (methodSelect && methodSelect.value !== methodValue) {
                methodSelect.value = methodValue;
            }
        },
                        
        // ===== Get Method Counts =====
        _getMethodCounts: function(routes) {
            const counts = { all: routes.length, GET: 0, POST: 0, PUT: 0, DELETE: 0 }; 
            routes.forEach(route => {
                if (route.methods && Array.isArray(route.methods)) {
                    route.methods.forEach(method => {
                        if (counts[method] !== undefined) {
                            counts[method]++; 
                        }
                    });
                }
            });
            return counts;
        },
        
        // ===== Get Source Counts =====
        _getSourceCounts: function(routes) {
            return {
                all: routes.length,
                'config': routes.filter(r => (r.source || 'config') === 'config').length,
                'dynamic': routes.filter(r => r.source === 'dynamic').length,
                'dashboard': routes.filter(r => r.source === 'dashboard').length,
                'auto-register': routes.filter(r => r.source === 'auto-register').length
            };
        },
                        
        // ===== Bind Filter Events (Optimized) =====
        _bindFilterEvents: function() {
            const self = this;
            
            // Search input - optimized debounced live search
            const searchInput = document.getElementById('route-search-input');
            if (searchInput) {
                let searchTimeout = null;
                let lastValue = '';
                
                searchInput.addEventListener('input', function(e) {
                    const value = e.target.value;
                    
                    // Clear existing timeout
                    clearTimeout(searchTimeout);
                    
                    // Empty value or first character - immediate response
                    if (!value || !lastValue) {
                        window.DashboardState.set('filters.routes.search', value);
                        self.renderRoutes();
                    } else {
                        // Debounced for subsequent typing
                        searchTimeout = setTimeout(function() {
                            window.DashboardState.set('filters.routes.search', value);
                            self.renderRoutes();
                        }, 200); // Reduced from 300ms to 200ms for better responsiveness
                    }
                    
                    lastValue = value;
                    
                    // Update clear button visibility immediately
                    const clearBtn = document.getElementById('route-clear-btn');
                    if (clearBtn) {
                        clearBtn.style.display = value ? '' : 'none';
                    }
                });

                // Handle Escape key to clear search
                searchInput.addEventListener('keydown', function(e) {
                    if (e.key === 'Escape') {
                        searchInput.value = '';
                        window.DashboardState.set('filters.routes.search', '');
                        self.renderRoutes();
                        searchInput.blur();
                    }
                });
            }
            
            // Clear button
            const clearBtn = document.getElementById('route-clear-btn');
            if (clearBtn) {
                clearBtn.addEventListener('click', function() {
                    const input = document.getElementById('route-search-input');
                    if (input) {
                        input.value = '';
                        input.focus();
                    }
                    window.DashboardState.set('filters.routes.search', '');
                    window.DashboardState.set('filters.routes.method', 'all');
                    window.DashboardState.set('filters.routes.source', 'all');
                    self.renderRoutes();
                    
                    // Update clear button
                    clearBtn.style.display = 'none';
                });
            }
            
            // Refresh button
            const refreshBtn = document.getElementById('route-refresh-btn');
            if (refreshBtn) {
                refreshBtn.addEventListener('click', function() {
                    self.loadRoutes();
                });
            }
            
            // Method select - immediate filter
            const methodSelect = document.getElementById('route-method-select');
            if (methodSelect) {
                methodSelect.addEventListener('change', function(e) {
                    window.DashboardState.set('filters.routes.method', e.target.value);
                    self.renderRoutes();
                });
            }
            
            // Source select - immediate filter
            const sourceSelect = document.getElementById('route-source-select');
            if (sourceSelect) {
                sourceSelect.addEventListener('change', function(e) {
                    window.DashboardState.set('filters.routes.source', e.target.value);
                    self.renderRoutes();
                });
            }
            
            // Add button
            const addBtn = document.getElementById('route-add-btn');
            if (addBtn) {
                addBtn.disabled = false;
                addBtn.removeAttribute('aria-disabled');
                addBtn.addEventListener('click', function(e) {
                    e.preventDefault();
                    self.showAddModal();
                });
            }
        },

                
        // ===== Render Route Rows =====
        // ===== Render Route Cards (Optimized with DocumentFragment) =====
        renderRouteCards: function(routes) {
            var container = document.getElementById('route-cards-view');
            if (!container) return;

            if (routes.length === 0) {
                container.innerHTML = '<div style="text-align:center;padding:48px 0;">' +
                    '<i class="bi bi-signpost-split" style="font-size:2.5rem;opacity:0.4;color:#64748b;display:block;margin-bottom:12px;"></i>' +
                    '<div style="font-size:14px;color:#64748b;">' + __('index.route.empty') + '</div>' +
                    '<div style="font-size:12px;color:#94a3b8;margin-top:6px;">' + __('index.route.emptyHelp') + '</div></div>';
                return;
            }

            // Sort by order
            var sortedRoutes = routes.slice().sort(function(a, b) {
                var orderA = a.order !== null && a.order !== undefined ? a.order : 999999;
                var orderB = b.order !== null && b.order !== undefined ? b.order : 999999;
                return orderA - orderB;
            });

            // Use DocumentFragment for better performance
            var fragment = document.createDocumentFragment();
            var gridContainer = document.createElement('div');
            gridContainer.style.cssText = 'display:grid;grid-template-columns:repeat(auto-fill,minmax(400px,1fr));gap:16px;';

            // Batch render for large lists
            if (sortedRoutes.length > 50) {
                this._renderCardsBatched(sortedRoutes, gridContainer, function() {
                    container.innerHTML = '';
                    fragment.appendChild(gridContainer);
                    container.appendChild(fragment);
                });
                return;
            }

            // Standard render for smaller lists
            sortedRoutes.forEach(function(route) {
                var card = this.createRouteCardDOM(route);
                if (card) gridContainer.appendChild(card);
            }.bind(this));

            container.innerHTML = '';
            fragment.appendChild(gridContainer);
            container.appendChild(fragment);
        },

        // ===== Batch Render Cards using requestAnimationFrame =====
        _renderCardsBatched: function(routes, container, onComplete) {
            var batchSize = 30;
            var index = 0;
            var total = routes.length;

            var renderBatch = function() {
                var end = Math.min(index + batchSize, total);
                var fragment = document.createDocumentFragment();

                for (; index < end; index++) {
                    var card = RoutesModule.createRouteCardDOM(routes[index]);
                    if (card) fragment.appendChild(card);
                }

                container.appendChild(fragment);

                if (index < total) {
                    requestAnimationFrame(renderBatch);
                } else {
                    if (onComplete) onComplete();
                }
            };

            renderBatch();
        },

        // ===== Render Route Cards (Legacy - falls back to string HTML) =====
        _renderRouteCardsLegacy: function(routes) {
            var container = document.getElementById('route-cards-view');
            if (!container) return;

            if (routes.length === 0) {
                container.innerHTML = '<div style="text-align:center;padding:48px 0;">' +
                    '<i class="bi bi-signpost-split" style="font-size:2.5rem;opacity:0.4;color:#64748b;display:block;margin-bottom:12px;"></i>' +
                    '<div style="font-size:14px;color:#64748b;">' + __('index.route.empty') + '</div>' +
                    '<div style="font-size:12px;color:#94a3b8;margin-top:6px;">' + __('index.route.emptyHelp') + '</div></div>';
                return;
            }

            // Sort by order
            var sortedRoutes = routes.slice().sort(function(a, b) {
                var orderA = a.order !== null && a.order !== undefined ? a.order : 999999;
                var orderB = b.order !== null && b.order !== undefined ? b.order : 999999;
                return orderA - orderB;
            });

            var html = '<div style="display:grid;grid-template-columns:repeat(auto-fill,minmax(400px,1fr));gap:16px;">';

            sortedRoutes.forEach(function(route) {
                var pathText = route.match && route.match.path || '-';
                var methods = route.match && route.match.methods || [];
                var hostText = route.match && route.match.hosts && route.match.hosts.length > 0 ? route.match.hosts[0] : '';
                var hasTransforms = route.transforms && route.transforms.length > 0;
                var hasAuthorization = route.authorizationPolicy || route.authorizationPolicy === '';

                // Determine card accent color by cluster
                var accentColor = route.clusterId ? '#3b82f6' : '#94a3b8';

                html += '<div style="border:1px solid var(--border-color);border-left:4px solid ' + accentColor +
                    ';border-radius:12px;background:var(--card-bg);overflow:hidden;transition:box-shadow 0.2s,transform 0.15s;cursor:pointer;"' +
                    ' onmouseover="this.style.boxShadow=\'0 4px 12px rgba(0,0,0,0.08)\';this.style.transform=\'translateY(-1px)\'"' +
                    ' onmouseout="this.style.boxShadow=\'none\';this.style.transform=\'none\'"' +
                    ' onclick="window.DashboardApp.modules.routes.toggleRoute(\'' + (route.routeId || '').replace(/'/g, "\\'") + '\')"' +
                    ' data-route-id="' + (route.routeId || '') + '">';

                // Card header
                html += '<div style="padding:14px 16px;border-bottom:1px solid var(--border-color);display:flex;align-items:center;justify-content:space-between;gap:8px;">';
                html += '<div style="display:flex;align-items:center;gap:10px;min-width:0;flex:1;">';

                // Order badge
                if (route.order !== null && route.order !== undefined) {
                    html += '<span style="display:inline-flex;align-items:center;justify-content:center;width:28px;height:28px;border-radius:6px;background:' +
                        (route.order < 100 ? '#fef3c7' : route.order < 1000 ? '#e0e7ff' : '#f1f5f9') +
                        ';color:' + (route.order < 100 ? '#92400e' : route.order < 1000 ? '#3730a3' : '#64748b') +
                        ';font-size:12px;font-weight:700;flex-shrink:0;">' + route.order + '</span>';
                } else {
                    html += '<span style="display:inline-flex;align-items:center;justify-content:center;width:28px;height:28px;border-radius:6px;background:#f1f5f9;color:#94a3b8;font-size:12px;flex-shrink:0;">-</span>';
                }

                html += '<div style="min-width:0;flex:1;">';
                html += '<div style="font-weight:600;font-size:14px;color:var(--text-primary);white-space:nowrap;overflow:hidden;text-overflow:ellipsis;" title="' + (window.DashboardUtils ? DashboardUtils.escapeHtml(route.routeId) : route.routeId) + '">' + (window.DashboardUtils ? DashboardUtils.escapeHtml(route.routeId) : route.routeId) + '</div>';
                html += '<div style="display:flex;align-items:center;gap:6px;margin-top:3px;">';
                html += '<span>' + (window.DashboardUtils ? DashboardUtils.createSourceBadge(route.source) : route.source || '-') + '</span>';
                if (hostText) {
                    html += '<span style="color:#cbd5e1;">|</span><span style="font-size:11px;color:#64748b;"><i class="bi bi-globe me-1"></i>' + (window.DashboardUtils ? DashboardUtils.escapeHtml(hostText) : hostText) + '</span>';
                }
                var indicators = [];
                if (hasTransforms) indicators.push('<i class="bi bi-arrow-left-right" style="color:#8b5cf6;" title="Transforms"></i>');
                if (hasAuthorization) indicators.push('<i class="bi bi-shield-lock" style="color:#f59e0b;" title="Authorization"></i>');
                if (indicators.length > 0) {
                    html += '<span style="color:#cbd5e1;">|</span><span style="display:inline-flex;gap:4px;">' + indicators.join('') + '</span>';
                }
                html += '</div></div></div>';

                // Action buttons
                html += '<div style="display:flex;gap:4px;flex-shrink:0;" onclick="event.stopPropagation()">';
                html += '<button style="border:1px solid var(--border-color);background:var(--card-bg);border-radius:6px;padding:4px 8px;font-size:12px;cursor:pointer;color:var(--primary-color);transition:background 0.15s;" onmouseover="this.style.background=\'#eff6ff\'" onmouseout="this.style.background=\'var(--card-bg)\'" onclick="event.stopPropagation();window.DashboardApp.modules.routes.showEditModal(\'' + (route.routeId || '').replace(/'/g, "\\'") + '\')" title="' + (window.__ && __("index.route.edit")) + '"><i class="bi bi-pencil"></i></button>';
                html += '<button style="border:1px solid var(--border-color);background:var(--card-bg);border-radius:6px;padding:4px 8px;font-size:12px;cursor:pointer;color:#ef4444;transition:background 0.15s;" onmouseover="this.style.background=\'#fef2f2\'" onmouseout="this.style.background=\'var(--card-bg)\'" onclick="event.stopPropagation();window.DashboardApp.modules.routes.deleteRoute(\'' + (route.routeId || '').replace(/'/g, "\\'") + '\')" title="' + (window.__ && __("index.route.delete")) + '"><i class="bi bi-trash"></i></button>';
                html += '</div></div>';

                // Card body
                html += '<div style="padding:12px 16px;">';

                // Path
                html += '<div style="display:flex;align-items:center;gap:8px;margin-bottom:10px;">';
                html += '<span style="display:inline-flex;align-items:center;justify-content:center;width:24px;height:24px;border-radius:6px;background:#eff6ff;color:#3b82f6;font-size:12px;flex-shrink:0;"><i class="bi bi-signpost-2"></i></span>';
                html += '<code style="font-size:13px;color:var(--text-secondary);background:var(--bg-secondary,#f8fafc);padding:3px 8px;border-radius:4px;word-break:break-all;border:1px solid var(--border-color);">' + (window.DashboardUtils ? DashboardUtils.escapeHtml(pathText) : pathText) + '</code>';
                html += '</div>';

                // Cluster + Methods row
                html += '<div style="display:flex;align-items:center;gap:8px;flex-wrap:wrap;">';

                // Cluster
                if (route.clusterId) {
                    html += '<span style="display:inline-flex;align-items:center;gap:5px;padding:4px 10px;border-radius:6px;font-size:12px;font-weight:500;background:#eff6ff;color:#1d4ed8;border:1px solid #bfdbfe;"><i class="bi bi-diagram-3"></i>' + (window.DashboardUtils ? DashboardUtils.escapeHtml(route.clusterId) : route.clusterId) + '</span>';
                }

                // Methods
                if (methods.length > 0) {
                    methods.forEach(function(m) {
                        var mColors = { 'GET': '#22c55e', 'POST': '#3b82f6', 'PUT': '#f59e0b', 'DELETE': '#ef4444', 'PATCH': '#8b5cf6', 'HEAD': '#64748b', 'OPTIONS': '#94a3b8' };
                        var mBg = { 'GET': '#f0fdf4', 'POST': '#eff6ff', 'PUT': '#fffbeb', 'DELETE': '#fef2f2', 'PATCH': '#f5f3ff', 'HEAD': '#f8fafc', 'OPTIONS': '#f8fafc' };
                        var mBorder = { 'GET': '#bbf7d0', 'POST': '#bfdbfe', 'PUT': '#fde68a', 'DELETE': '#fecaca', 'PATCH': '#ddd6fe', 'HEAD': '#e2e8f0', 'OPTIONS': '#e2e8f0' };
                        var c = mColors[m] || '#64748b';
                        html += '<span style="display:inline-flex;align-items:center;padding:3px 8px;border-radius:4px;font-size:11px;font-weight:700;font-family:monospace;background:' + (mBg[m] || '#f8fafc') + ';color:' + c + ';border:1px solid ' + (mBorder[m] || '#e2e8f0') + ';">' + m + '</span>';
                    });
                } else {
                    html += '<span style="font-size:11px;color:#94a3b8;">ANY</span>';
                }

                html += '</div>';
                html += '</div></div>';
            });

            html += '</div>';
            container.innerHTML = html;
        },

        renderRouteRows: function(routes, tbody) {
            if (!tbody) return;

            // Sort by order
            const sortedRoutes = [...routes].sort((a, b) => {
                const orderA = a.order !== null && a.order !== undefined ? a.order : 999999;
                const orderB = b.order !== null && b.order !== undefined ? b.order : 999999;
                return orderA - orderB;
            });

            // Check if this is first render or we should use diff
            const existingRowCount = tbody.querySelectorAll('tr[data-route-id]').length;
            const isFirstRender = existingRowCount === 0;
            
            // Use diff rendering for updates with many items
            if (!isFirstRender && existingRowCount > 10) {
                this._renderRouteRowsDiff(sortedRoutes, tbody);
            } else {
                this._renderRouteRowsStandard(sortedRoutes, tbody);
            }
        },

        // ===== Standard Render (full rebuild - used for first render) =====
        _renderRouteRowsStandard: function(routes, tbody) {
            window.DashboardDOM.clear(tbody);
            const fragment = document.createDocumentFragment();

            routes.forEach(function(route) {
                var isExpanded = (window.DashboardState.get('ui.expandedRoutes') || new Set()).has(route.routeId);
                var rows = this.createRouteRows(route, isExpanded);
                rows.forEach(function(row) { fragment.appendChild(row); });
            }.bind(this));

            tbody.appendChild(fragment);
        },

        // ===== Diff Render (only update changed rows) =====
        _renderRouteRowsDiff: function(routes, tbody) {
            const startTime = performance.now();
            
            // Get existing rows grouped by routeId
            const existingRows = new Map();
            tbody.querySelectorAll('tr[data-route-id]').forEach(row => {
                const routeId = row.dataset.routeId;
                if (!existingRows.has(routeId)) {
                    existingRows.set(routeId, []);
                }
                existingRows.get(routeId).push(row);
            });

            // Track which routeIds should exist
            const newRouteIds = new Set(routes.map(r => r.routeId));
            
            // Remove rows that no longer exist
            existingRows.forEach((rows, routeId) => {
                if (!newRouteIds.has(routeId)) {
                    rows.forEach(row => row.remove());
                }
            });

            // Build new order and update/create rows
            const rowOrder = [];

            routes.forEach(function(route) {
                const routeRows = existingRows.get(route.routeId);
                var isExpanded = (window.DashboardState.get('ui.expandedRoutes') || new Set()).has(route.routeId);

                if (routeRows && routeRows.length > 0) {
                    // Reuse existing rows
                    const mainRow = routeRows[0];
                    const detailRow = routeRows[1];
                    
                    // Update main row content (check for changes)
                    this._updateRouteRowContent(mainRow, route, isExpanded);
                    
                    if (isExpanded && !detailRow) {
                        // Need to add detail row
                        const newDetailRow = this.createRouteDetailRow(route);
                        newDetailRow.dataset.routeId = route.routeId;
                        rowOrder.push(mainRow, newDetailRow);
                    } else if (!isExpanded && detailRow) {
                        // Need to remove detail row
                        detailRow.remove();
                        rowOrder.push(mainRow);
                    } else {
                        // Same structure
                        rowOrder.push(mainRow);
                        if (detailRow) {
                            detailRow.dataset.routeId = route.routeId;
                            rowOrder.push(detailRow);
                        }
                    }
                } else {
                    // Create new rows
                    var rows = this.createRouteRows(route, isExpanded);
                    rows.forEach(row => rowOrder.push(row));
                }
            }.bind(this));

            // Reorder rows in DOM efficiently
            rowOrder.forEach(function(row) {
                tbody.appendChild(row);
            });

            const endTime = performance.now();
            console.log(`[Routes] Diff render: ${routes.length} routes in ${(endTime - startTime).toFixed(2)}ms`);
        },

        // ===== Update Route Row Content (for diff updates) =====
        _updateRouteRowContent: function(row, route, isExpanded) {
            // Update expand icon
            const expandIcon = row.querySelector('.row-expand-icon');
            if (expandIcon) {
                expandIcon.classList.toggle('expanded', isExpanded);
            }
        },

        // ===== Create Route Rows =====
        createRouteRows: function(route, isExpanded) {
            var rows = [];

            // Main row
            var mainTr = this.createRouteMainRow(route, isExpanded);
            rows.push(mainTr);

            // Expanded detail row
            if (isExpanded) {
                var detailTr = this.createRouteDetailRow(route);
                rows.push(detailTr);
            }

            return rows;
        },

        // ===== Create Route Main Row =====
        createRouteMainRow: function(route, isExpanded) {
            var tr = window.DashboardDOM.create('tr', {
                className: 'route-row',
                attributes: { 'data-route-id': route.routeId },
                style: { cursor: 'pointer' }
            });

            // Expand icon
            var tdExpand = window.DashboardDOM.create('td', {
                style: { width: '36px', verticalAlign: 'middle', textAlign: 'center' }
            });
            var expandIcon = window.DashboardDOM.create('i', {
                className: 'bi bi-chevron-right row-expand-icon' + (isExpanded ? ' expanded' : '')
            });
            tdExpand.appendChild(expandIcon);
            tr.appendChild(tdExpand);

            // Order
            var tdOrder = window.DashboardDOM.create('td', {
                style: { width: '56px', verticalAlign: 'middle', textAlign: 'center' }
            });
            if (route.order !== null && route.order !== undefined) {
                var orderSpan = document.createElement('span');
                orderSpan.className = 'priority-badge';
                if (route.order < 50) orderSpan.classList.add('priority-high');
                else if (route.order < 100) orderSpan.classList.add('priority-medium');
                else orderSpan.classList.add('priority-low');
                orderSpan.textContent = route.order;
                tdOrder.appendChild(orderSpan);
            } else {
                tdOrder.innerHTML = '<span class="priority-badge priority-none">-</span>';
            }
            tr.appendChild(tdOrder);

            // Route name
            var tdName = window.DashboardDOM.create('td', {
                style: { overflow: 'hidden' }
            });
            var nameDiv = document.createElement('div');
            nameDiv.style.cssText = 'display:flex;align-items:center;gap:6px;';
            var nameStrong = document.createElement('strong');
            nameStrong.style.cssText = 'font-size:14px;color:var(--text-primary);white-space:nowrap;overflow:hidden;text-overflow:ellipsis;max-width:200px;';
            nameStrong.textContent = route.routeId;
            nameStrong.title = route.routeId;
            nameDiv.appendChild(nameStrong);

            var sourceSpan = document.createElement('span');
            sourceSpan.innerHTML = this.createSourceBadge(route.source);
            nameDiv.appendChild(sourceSpan);
            // Indicators (transforms, auth)
            var indicators = [];
            if (route.transforms && route.transforms.length > 0) indicators.push('<i class="bi bi-arrow-left-right" style="color:#8b5cf6;font-size:12px;" title="Transforms"></i>');
            if (route.authorizationPolicy || route.authorizationPolicy === '') indicators.push('<i class="bi bi-shield-lock" style="color:#f59e0b;font-size:12px;" title="Authorization"></i>');
            if (indicators.length > 0) {
                var indSpan = document.createElement('span');
                indSpan.style.cssText = 'display:inline-flex;gap:3px;';
                indSpan.innerHTML = indicators.join('');
                nameDiv.appendChild(indSpan);
            }
            var nameCopyBtn = this.createCopyButton(route.routeId);
            nameDiv.appendChild(nameCopyBtn);
            tdName.appendChild(nameDiv);
            tr.appendChild(tdName);

            // Path
            var tdPath = window.DashboardDOM.create('td', {
                style: { overflow: 'hidden' }
            });
            var pathText = route.match && route.match.path || '-';
            var pathDiv = document.createElement('div');
            pathDiv.style.cssText = 'display:flex;align-items:center;gap:6px;';
            var pathCode = document.createElement('code');
            pathCode.style.cssText = 'font-size:13px;background:var(--bg-secondary,#f8fafc);padding:3px 8px;border-radius:4px;border:1px solid var(--border-color);word-break:break-all;';
            pathCode.textContent = pathText;
            pathDiv.appendChild(pathCode);
            var pathCopyBtn = this.createCopyButton(route.match && route.match.path || '');
            pathDiv.appendChild(pathCopyBtn);
            tdPath.appendChild(pathDiv);
            tr.appendChild(tdPath);

            // Cluster
            var tdCluster = window.DashboardDOM.create('td', {
                style: { width: '140px', verticalAlign: 'middle', overflow: 'hidden' }
            });
            if (route.clusterId) {
                var clusterDiv = document.createElement('div');
                clusterDiv.style.cssText = 'display:flex;align-items:center;gap:4px;';
                var clusterBadge = document.createElement('span');
                clusterBadge.style.cssText = 'display:inline-flex;align-items:center;gap:4px;padding:3px 8px;border-radius:5px;font-size:12px;font-weight:600;background:#eff6ff;color:#1d4ed8;border:1px solid #bfdbfe;white-space:nowrap;max-width:130px;overflow:hidden;text-overflow:ellipsis;';
                clusterBadge.innerHTML = '<i class="bi bi-diagram-3" style="font-size:11px;flex-shrink:0;"></i><span style="overflow:hidden;text-overflow:ellipsis;">' + (window.DashboardUtils ? DashboardUtils.escapeHtml(route.clusterId) : route.clusterId) + '</span>';
                clusterBadge.title = route.clusterId;
                clusterDiv.appendChild(clusterBadge);
                tdCluster.appendChild(clusterDiv);
            } else {
                tdCluster.innerHTML = '<span class="text-muted">-</span>';
            }
            tr.appendChild(tdCluster);

            // Methods
            var tdMethods = window.DashboardDOM.create('td', {
                style: { width: '120px', verticalAlign: 'middle' }
            });
            var methods = route.match && route.match.methods || [];
            var methDiv = document.createElement('div');
            methDiv.style.cssText = 'display:flex;align-items:center;gap:3px;flex-wrap:wrap;';
            if (methods.length > 0) {
                var mColors = { 'GET': '#22c55e', 'POST': '#3b82f6', 'PUT': '#f59e0b', 'DELETE': '#ef4444', 'PATCH': '#8b5cf6', 'HEAD': '#64748b', 'OPTIONS': '#94a3b8' };
                var mBg = { 'GET': '#f0fdf4', 'POST': '#eff6ff', 'PUT': '#fffbeb', 'DELETE': '#fef2f2', 'PATCH': '#f5f3ff', 'HEAD': '#f8fafc', 'OPTIONS': '#f8fafc' };
                var mBorder = { 'GET': '#bbf7d0', 'POST': '#bfdbfe', 'PUT': '#fde68a', 'DELETE': '#fecaca', 'PATCH': '#ddd6fe', 'HEAD': '#e2e8f0', 'OPTIONS': '#e2e8f0' };
                methods.forEach(function(m) {
                    var methSpan = document.createElement('span');
                    var c = mColors[m] || '#64748b';
                    methSpan.style.cssText = 'display:inline-flex;align-items:center;padding:2px 6px;border-radius:4px;font-size:10px;font-weight:700;font-family:monospace;background:' + (mBg[m] || '#f8fafc') + ';color:' + c + ';border:1px solid ' + (mBorder[m] || '#e2e8f0') + ';';
                    methSpan.textContent = m;
                    methDiv.appendChild(methSpan);
                });
            } else {
                var anySpan = document.createElement('span');
                anySpan.className = 'text-muted';
                anySpan.style.cssText = 'font-size:12px;';
                anySpan.textContent = 'ANY';
                methDiv.appendChild(anySpan);
            }
            tdMethods.appendChild(methDiv);
            tr.appendChild(tdMethods);

            // Actions
            var tdActions = window.DashboardDOM.create('td', {
                style: { width: '80px', verticalAlign: 'middle', textAlign: 'center' }
            });
            tdActions.appendChild(this.createActionButtons(route));
            tr.appendChild(tdActions);

            // Click to expand
            tr.addEventListener('click', function(e) {
                if (e.target.closest('.copy-btn') || e.target.closest('.btn-group')) return;
                this.toggleRoute(route.routeId);
            }.bind(this));

            return tr;
        },

        // ===== Create Order Badge =====
        createOrderBadge: function(order) {
            const displayOrder = (order !== null && order !== undefined) ? order : '-';
            
            // Priority levels: High (< 50), Medium (50-100), Low (> 100), None
            let cssClass, iconClass;
            if (order !== null && order !== undefined) {
                if (order < 50) {
                    cssClass = 'priority-high';
                    iconClass = 'bi-arrow-up-circle-fill';
                } else if (order < 100) {
                    cssClass = 'priority-medium';
                    iconClass = 'bi-arrow-right-circle-fill';
                } else {
                    cssClass = 'priority-low';
                    iconClass = 'bi-arrow-down-circle-fill';
                }
            } else {
                cssClass = 'priority-none';
                iconClass = 'bi-dash-circle';
            }
            
            const badge = window.DashboardDOM.create('span', {
                className: `priority-badge ${cssClass}`
            });
            
            const icon = window.DashboardDOM.create('i', {
                className: `bi ${iconClass}`
            });
            badge.appendChild(icon);
            badge.appendChild(document.createTextNode(' ' + displayOrder));
            
            return badge;
        },


        // ===== Create Method Badge =====
        createMethodBadge: function(method) {
            const methodMap = {
                'GET': { css: 'bg-success', icon: 'bi-arrow-down-circle-fill' },
                'POST': { css: 'bg-primary', icon: 'bi-plus-circle-fill' },
                'PUT': { css: 'bg-info', icon: 'bi-pencil-circle-fill' },
                'DELETE': { css: 'bg-danger', icon: 'bi-trash-circle-fill' },
                'PATCH': { css: 'bg-warning text-dark', icon: 'bi-gear-circle-fill' }
            }; 
                    
            const config = methodMap[method] || { css: 'bg-secondary', icon: 'bi-circle-fill' };
                    
            const badge = window.DashboardDOM.create('span', {
                className: `badge ${config.css} me-1`,
                style: { fontSize: '11px', display: 'inline-flex', alignItems: 'center', gap: '4px' }
            });
                    
            const icon = window.DashboardDOM.create('i', {
                className: `bi ${config.icon}`
            });
            badge.appendChild(icon);
            badge.appendChild(document.createTextNode(' ' + method));
                    
            return badge;
        },

        // ===== Create Action Buttons =====
        createActionButtons: function(route) {
            const container = window.DashboardDOM.create('div', {
                className: 'btn-group btn-group-sm'
            });

            // Edit button
            const editBtn = window.DashboardDOM.create('button', {
                className: 'btn btn-outline-primary',
                attributes: { title: __('index.route.edit') },
                events: {
                    click: (e) => {
                        e.stopPropagation();
                        this.showEditModal(route.routeId);
                    }
                }
            });
            const editIcon = window.DashboardDOM.create('i', {
                className: 'bi bi-pencil'
            });
            editBtn.appendChild(editIcon);
            container.appendChild(editBtn);

            // Delete button
            const deleteBtn = window.DashboardDOM.create('button', {
                className: 'btn btn-outline-danger',
                attributes: { title: __('index.route.delete') },
                events: {
                    click: (e) => {
                        e.stopPropagation();
                        this.deleteRoute(route.routeId);
                    }
                }
            });
            const deleteIcon = window.DashboardDOM.create('i', {
                className: 'bi bi-trash'
            });
            deleteBtn.appendChild(deleteIcon);
            container.appendChild(deleteBtn);

            return container;
        },


        // ===== Create Route Detail Row =====
        createRouteDetailRow: function(route) {
            const tr = window.DashboardDOM.create('tr', {
                className: 'route-detail-row'
            });
                
            const td = window.DashboardDOM.create('td', {
                attributes: { colspan: '7' }
            });
                
            const detailHtml = []; 
            detailHtml.push('<div class="detail-panel">');

            // Quick actions bar
            detailHtml.push('<div class="detail-actions-bar">');
            detailHtml.push(`<div class="detail-actions-left"><span class="detail-actions-label"><i class="bi bi-signpost-split"></i> ${__('index.route.title')}</span></div>`);
            detailHtml.push('<div class="detail-actions-right">');
            detailHtml.push(`<button class="btn btn-sm btn-outline-secondary detail-action-btn" onclick="RoutesModule.showEditModal('${window.DashboardUtils.escapeHtml(route.routeId)}')" title="${__('index.route.edit')}"><i class="bi bi-pencil"></i> ${__('index.route.edit')}</button>`);
            detailHtml.push(`<button class="btn btn-sm btn-outline-primary detail-action-btn" onclick="RoutesModule.copyRouteJson('${window.DashboardUtils.escapeHtml(route.routeId)}')" title="${__('index.copyJson.title')}"><i class="bi bi-clipboard-data"></i> ${__('index.copyJson')}</button>`);
            detailHtml.push('</div>');
            detailHtml.push('</div>');

            // Overview (compact key-value)
            const sourceBadge = this.createSourceBadge(route.source);
            detailHtml.push('<div class="detail-section">');
            detailHtml.push(`<div class="detail-section-title"><i class="bi bi-info-circle"></i>${__('index.route.basicInfo')}</div>`);
            detailHtml.push('<div class="detail-structured-config">');
            detailHtml.push(`<div class="detail-kv-row"><span class="detail-kv-key">RouteId</span><span class="detail-kv-value"><code>${window.DashboardUtils.escapeHtml(route.routeId)}</code></span></div>`);
            detailHtml.push(`<div class="detail-kv-row"><span class="detail-kv-key">ClusterId</span><span class="detail-kv-value"><code>${window.DashboardUtils.escapeHtml(route.clusterId || '-')}</code></span></div>`);
            detailHtml.push(`<div class="detail-kv-row"><span class="detail-kv-key">Order</span><span class="detail-kv-value">${this.createOrderBadgeHtml(route.order)}</span></div>`);
            detailHtml.push(`<div class="detail-kv-row"><span class="detail-kv-key">Source</span><span class="detail-kv-value">${sourceBadge}</span></div>`);
            detailHtml.push('</div>');
            detailHtml.push('</div>');

            // Match Rules
            detailHtml.push('<div class="detail-section">');
            detailHtml.push(`<div class="detail-section-title"><i class="bi bi-filter"></i>${__('index.route.match')}</div>`);
            detailHtml.push('<div class="detail-structured-config">');
                                
            const match = route.match || {};
            if (match.path) {
                detailHtml.push(`<div class="detail-kv-row"><span class="detail-kv-key">Path</span><span class="detail-kv-value"><code>${window.DashboardUtils.escapeHtml(match.path)}</code></span></div>`);
            }
            if (match.hosts && match.hosts.length > 0) {
                const hostBadges = match.hosts.map(function(h) { return `<code>${h}</code>`; }).join(' ');
                detailHtml.push(`<div class="detail-kv-row"><span class="detail-kv-key">Hosts</span><span class="detail-kv-value">${hostBadges}</span></div>`);
            }
            if (match.methods && match.methods.length > 0) {
                const methodBadges = match.methods.map(m => this.createMethodBadgeInline(m)).join(' ');
                detailHtml.push(`<div class="detail-kv-row"><span class="detail-kv-key">Methods</span><span class="detail-kv-value">${methodBadges}</span></div>`);
            }
            detailHtml.push('</div>');
                        
            // Headers & Query Parameters in table format
            if ((match.headers && match.headers.length > 0) || (match.queryParameters && match.queryParameters.length > 0)) {
                detailHtml.push('<div class="table-responsive mt-2">');
                detailHtml.push('<table class="table table-sm detail-table">');
                detailHtml.push('<thead><tr><th style="width:100px;">' + (__('index.detail.type')) + '</th><th style="width:120px;">' + (__('index.detail.name')) + '</th><th>' + (__('index.detail.values')) + '</th><th style="width:80px;">' + (__('index.detail.mode')) + '</th></tr></thead>');
                detailHtml.push('<tbody>');
                if (match.headers && match.headers.length > 0) {
                    match.headers.forEach(h => {
                        detailHtml.push(`<tr><td><span class="badge bg-secondary">${__('index.detail.header')}</span></td><td><code>${h.name || '-'}</code></td><td>${(h.values || []).map(v => `<code>${v}</code>`).join(' ')}</td><td>${h.mode || '-'}</td></tr>`);
                    });
                }
                if (match.queryParameters && match.queryParameters.length > 0) {
                    match.queryParameters.forEach(q => {
                        detailHtml.push(`<tr><td><span class="badge bg-info">${__('index.detail.query')}</span></td><td><code>${q.name || '-'}</code></td><td>${(q.values || []).map(v => `<code>${v}</code>`).join(' ')}</td><td>${q.mode || '-'}</td></tr>`);
                    });
                }
                detailHtml.push('</tbody></table></div>');
            }
            detailHtml.push('</div>');

            // Destinations
            if (route.destinations && route.destinations.length > 0) {
                detailHtml.push('<div class="detail-section">');
                detailHtml.push(`<div class="detail-section-title"><i class="bi bi-server"></i>${__('index.cluster.destinations')} <span class="badge bg-light text-dark ms-2">${route.destinations.length}</span></div>`);
                detailHtml.push('<div class="table-responsive">');
                detailHtml.push('<table class="table table-sm detail-table">');
                detailHtml.push(`<thead><tr><th>${__('index.detail.name')}</th><th>${__('index.detail.address')}</th></tr></thead>`);
                detailHtml.push('<tbody>');
                route.destinations.forEach(d => {
                    detailHtml.push(`<tr><td><code>${window.DashboardUtils.escapeHtml(d.name || '-')}</code></td><td><a href="${d.address || '#'}" target="_blank" class="text-decoration-none"><code>${window.DashboardUtils.escapeHtml(d.address || '-')}</code></a></td></tr>`);
                });
                detailHtml.push('</tbody></table></div>');
                detailHtml.push('</div>');
            }

            // Transforms - structured display
            if (route.transforms && route.transforms.length > 0) {
                detailHtml.push('<div class="detail-section">');
                detailHtml.push(`<div class="detail-section-title"><i class="bi bi-arrow-repeat"></i>${__('index.route.transforms')}</div>`);
                detailHtml.push(this.renderStructuredTransforms(route.transforms));
                detailHtml.push('</div>');
            }

            // Policies (merged: Authorization + CORS + Timeout + RateLimiter)
            const hasPolicies = route.authorizationPolicy || route.corsPolicy || route.timeout || route.timeoutPolicy || route.rateLimiterPolicy;
            if (hasPolicies) {
                detailHtml.push('<div class="detail-section">');
                detailHtml.push(`<div class="detail-section-title"><i class="bi bi-shield-check"></i>${__('index.route.policies')}</div>`);
                detailHtml.push('<div class="detail-structured-config">');
                if (route.authorizationPolicy) {
                    detailHtml.push(`<div class="detail-kv-row"><span class="detail-kv-key"><i class="bi bi-shield-lock"></i> ${__('index.route.policy.authorization')}</span><span class="detail-kv-value"><code>${window.DashboardUtils.escapeHtml(route.authorizationPolicy)}</code></span></div>`);
                }
                if (route.corsPolicy) {
                    detailHtml.push(`<div class="detail-kv-row"><span class="detail-kv-key"><i class="bi bi-shield"></i> ${__('index.route.policy.cors')}</span><span class="detail-kv-value"><code>${window.DashboardUtils.escapeHtml(route.corsPolicy)}</code></span></div>`);
                }
                if (route.timeout || route.timeoutPolicy) {
                    const timeoutVal = route.timeout ? `<code>${route.timeout}</code>` : '<span class="text-muted">-</span>';
                    const timeoutNote = route.timeoutPolicy ? ` <span class="text-muted small">(${route.timeoutPolicy})</span>` : '';
                    detailHtml.push(`<div class="detail-kv-row"><span class="detail-kv-key"><i class="bi bi-clock"></i> ${__('index.route.policy.timeout')}</span><span class="detail-kv-value">${timeoutVal}${timeoutNote}</span></div>`);
                }
                if (route.rateLimiterPolicy) {
                    detailHtml.push(`<div class="detail-kv-row"><span class="detail-kv-key"><i class="bi bi-speedometer2"></i> ${__('index.route.policy.rateLimiter')}</span><span class="detail-kv-value"><code>${window.DashboardUtils.escapeHtml(route.rateLimiterPolicy)}</code></span></div>`);
                }
                detailHtml.push('</div>');
                detailHtml.push('</div>');
            }

            // Retry Configuration - dedicated section for better visibility
            const retryConfig = this.extractRetryConfig(route.metadata);
            if (retryConfig && retryConfig.enabled) {
                detailHtml.push('<div class="detail-section">');
                detailHtml.push(`<div class="detail-section-title"><i class="bi bi-arrow-repeat"></i>${__('index.route.retry')}</div>`);
                detailHtml.push('<div class="detail-structured-config">');
                detailHtml.push(`<div class="detail-kv-row"><span class="detail-kv-key"><i class="bi bi-toggle-on"></i> ${__('index.route.retry.enabled')}</span><span class="detail-kv-value"><span class="badge bg-success"><i class="bi bi-check-circle-fill"></i> ${__('index.bool.yes')}</span></span></div>`);
                if (retryConfig.maxRetries !== undefined) {
                    detailHtml.push(`<div class="detail-kv-row"><span class="detail-kv-key"><i class="bi bi-123"></i> ${__('index.route.retry.maxRetries')}</span><span class="detail-kv-value"><code>${retryConfig.maxRetries}</code></span></div>`);
                }
                if (retryConfig.retryOnStatusCodes) {
                    const codes = retryConfig.retryOnStatusCodes.split(',').map(c => `<code>${c.trim()}</code>`).join(' ');
                    detailHtml.push(`<div class="detail-kv-row"><span class="detail-kv-key"><i class="bi bi-exclamation-triangle"></i> ${__('index.route.retry.statusCodes')}</span><span class="detail-kv-value">${codes}</span></div>`);
                }
                if (retryConfig.retryNonIdempotent) {
                    detailHtml.push(`<div class="detail-kv-row"><span class="detail-kv-key"><i class="bi bi-shield-exclamation"></i> ${__('index.route.retry.nonIdempotent')}</span><span class="detail-kv-value"><span class="badge bg-warning text-dark"><i class="bi bi-check-circle-fill"></i> ${__('index.bool.yes')}</span></span></div>`);
                }
                detailHtml.push('</div>');
                detailHtml.push('</div>');
            }

            // Metadata - structured key-value display (excluding retry configs which are shown above)
            const nonRetryMetadata = route.metadata ? Object.fromEntries(
                Object.entries(route.metadata).filter(([key]) => !key.startsWith('Retry:'))
            ) : {};
            if (Object.keys(nonRetryMetadata).length > 0) {
                detailHtml.push('<div class="detail-section">');
                detailHtml.push(`<div class="detail-section-title"><i class="bi bi-tags"></i>${__('index.route.metadata')}</div>`);
                detailHtml.push(this.renderStructuredConfig(nonRetryMetadata, 'metadata'));
                detailHtml.push('</div>');
            }
                
            detailHtml.push('</div>');
            td.innerHTML = detailHtml.join('');
            tr.appendChild(td);
                
            return tr;
        },

        // ===== Render Structured Transforms =====
        renderStructuredTransforms: function(transforms) {
            const html = [];
            html.push('<div class="detail-transforms-list">');

            transforms.forEach(function(transform, index) {
                const keys = Object.keys(transform);
                // Try to build a human-readable summary
                let summary = '';
                let detailParts = [];

                keys.forEach(function(key) {
                    const val = transform[key];
                    if (typeof val === 'string') {
                        // Known transform types for readable labels
                        const labelMap = {
                            'PathPattern': 'Path Pattern',
                            'PathSet': 'Path Set',
                            'PathPrefix': 'Path Prefix',
                            'PathRemovePrefix': 'Remove Prefix',
                            'AddRequestHeader': 'Add Request Header',
                            'AddResponseHeader': 'Add Response Header',
                            'RemoveRequestHeader': 'Remove Request Header',
                            'RemoveResponseHeader': 'Remove Response Header',
                            'RequestHeader': 'Request Header',
                            'ResponseHeader': 'Response Header',
                            'QueryValueParameter': 'Query Param',
                            'AddQueryParameter': 'Add Query',
                            'RemoveQueryParameter': 'Remove Query',
                            'RequestHeadersCopy': 'Copy Request Headers',
                            'ResponseHeadersCopy': 'Copy Response Headers',
                            'UseOriginalHost': 'Use Original Host',
                            'Forwarded': 'Forwarded Header',
                            'PathTransform': 'Path Transform',
                        };
                        const label = labelMap[key] || key;
                        if (!summary) {
                            summary = `<strong>${label}</strong>: <code>${window.DashboardUtils.escapeHtml(val)}</code>`;
                        } else {
                            detailParts.push(`<span class="detail-kv-key">${label}</span> <code>${window.DashboardUtils.escapeHtml(val)}</code>`);
                        }
                    } else if (typeof val === 'boolean') {
                        const label = key.replace(/([A-Z])/g, ' $1').trim();
                        if (val) {
                            detailParts.push(`<span class="badge bg-success"><i class="bi bi-check-circle-fill"></i> ${__('index.bool.yes')}: ${label}</span>`);
                        }
                    } else if (typeof val === 'object' && val !== null) {
                        detailParts.push(`<span class="detail-kv-key">${key}</span> <code>${window.DashboardUtils.escapeHtml(JSON.stringify(val))}</code>`);
                    }
                });

                html.push(`<div class="detail-transform-item">`);
                html.push(`<div class="detail-transform-header">`);
                html.push(`<span class="detail-transform-index">#${index + 1}</span>`);
                html.push(`<span class="detail-transform-summary">${summary || JSON.stringify(transform)}</span>`);
                if (detailParts.length > 0) {
                    html.push(`<span class="detail-transform-detail">${detailParts.join(' &bull; ')}</span>`);
                }
                html.push('</div>');
                // Collapsible raw JSON
                html.push(`<details class="detail-transform-raw"><summary><i class="bi bi-code-slash"></i></summary><pre>${window.DashboardUtils.escapeHtml(JSON.stringify(transform, null, 2))}</pre></details>`);
                html.push('</div>');
            });

            html.push('</div>');
            return html.join('');
        },

        // ===== Extract Retry Configuration from Metadata =====
        extractRetryConfig: function(metadata) {
            if (!metadata || typeof metadata !== 'object') return null;
            
            const retryKeys = ['Retry:Enabled', 'Retry:MaxRetries', 'Retry:RetryOnStatusCodes', 'Retry:RetryNonIdempotent'];
            const hasRetryConfig = retryKeys.some(key => metadata.hasOwnProperty(key));
            
            if (!hasRetryConfig) return null;
            
            const enabled = metadata['Retry:Enabled'] === 'true' || metadata['Retry:Enabled'] === true;
            
            return {
                enabled: enabled,
                maxRetries: metadata['Retry:MaxRetries'],
                retryOnStatusCodes: metadata['Retry:RetryOnStatusCodes'],
                retryNonIdempotent: metadata['Retry:RetryNonIdempotent'] === 'true' || metadata['Retry:RetryNonIdempotent'] === true
            };
        },

        // ===== Render Structured Config =====
        renderStructuredConfig: function(obj, configType) {
            if (!obj || typeof obj !== 'object') return '';

            const keyLabels = {
                'Name': 'Name', 'Value': 'Value', 'Domain': 'Domain', 'HttpOnly': 'HttpOnly',
                'SameSite': 'SameSite', 'Expiration': 'Expiration', 'MaxAge': 'MaxAge',
            };

            const html = [];
            html.push('<div class="detail-structured-config">');

            const renderKeyValue = function(key, value, depth) {
                const label = keyLabels[key] || key;
                const indent = depth > 0 ? ` style="padding-left:${depth * 20}px;"` : '';

                if (value === null || value === undefined) {
                    html.push(`<div class="detail-kv-row"${indent}><span class="detail-kv-key">${label}</span><span class="detail-kv-value text-muted">-</span></div>`);
                } else if (typeof value === 'boolean') {
                    const badge = value
                        ? `<span class="badge bg-success"><i class="bi bi-check-circle-fill"></i> ${__('index.bool.yes')}</span>`
                        : `<span class="badge bg-secondary"><i class="bi bi-x-circle-fill"></i> ${__('index.bool.no')}</span>`;
                    html.push(`<div class="detail-kv-row"${indent}><span class="detail-kv-key">${label}</span><span class="detail-kv-value">${badge}</span></div>`);
                } else if (typeof value === 'number' || typeof value === 'string') {
                    const valHtml = `<code>${window.DashboardUtils.escapeHtml(String(value))}</code>`;
                    html.push(`<div class="detail-kv-row"${indent}><span class="detail-kv-key">${label}</span><span class="detail-kv-value">${valHtml}</span></div>`);
                } else if (Array.isArray(value)) {
                    const items = value.map(function(v) {
                        return `<code>${window.DashboardUtils.escapeHtml(String(v))}</code>`;
                    }).join(' ');
                    html.push(`<div class="detail-kv-row"${indent}><span class="detail-kv-key">${label}</span><span class="detail-kv-value">${items}</span></div>`);
                } else if (typeof value === 'object') {
                    html.push(`<div class="detail-kv-row detail-kv-group"${indent}><span class="detail-kv-key detail-kv-group-key"><i class="bi bi-chevron-right"></i> ${label}</span></div>`);
                    Object.keys(value).forEach(function(subKey) {
                        renderKeyValue(subKey, value[subKey], depth + 1);
                    });
                }
            };

            Object.keys(obj).forEach(function(key) {
                renderKeyValue(key, obj[key], 0);
            });

            // Collapsible raw JSON toggle
            html.push(`<div class="detail-raw-json-toggle"><details><summary><i class="bi bi-code-slash"></i> ${__('index.viewRawJson')}</summary><pre class="detail-raw-json">${window.DashboardUtils.escapeHtml(JSON.stringify(obj, null, 2))}</pre></details></div>`);

            html.push('</div>');
            return html.join('');
        },

        // ===== Copy Route JSON =====
        copyRouteJson: function(routeId) {
            const routes = window.DashboardState.get('data.routes') || [];
            const route = routes.find(function(r) { return r.routeId === routeId; });
            if (!route) return;

            const yarpRoute = {
                "ClusterId": route.clusterId || "",
                "Order": route.order || 50,
                "Match": {
                    "Path": route.match?.path || ""
                }
            };
            const methods = route.match?.methods || [];
            if (methods.length > 0) yarpRoute.Match.Methods = methods;
            const hosts = route.match?.hosts || [];
            if (hosts.length > 0) yarpRoute.Match.Hosts = hosts;
            if (route.match?.headers && route.match?.headers.length > 0) yarpRoute.Match.Headers = route.match.headers;
            if (route.transforms && route.transforms.length > 0) yarpRoute.Transforms = route.transforms;
            if (route.authorizationPolicy) yarpRoute.AuthorizationPolicy = route.authorizationPolicy;
            if (route.corsPolicy) yarpRoute.CorsPolicy = route.corsPolicy;
            if (route.timeout) yarpRoute.Timeout = route.timeout;
            if (route.timeoutPolicy) yarpRoute.TimeoutPolicy = route.timeoutPolicy;
            if (route.rateLimiterPolicy) yarpRoute.RateLimiterPolicy = route.rateLimiterPolicy;
            if (route.metadata && Object.keys(route.metadata).length > 0) {
                // Ensure retry config is included with proper defaults if not set
                yarpRoute.Metadata = {};
                const retryDefaults = {
                    "Retry:Enabled": "false",
                    "Retry:MaxRetries": "2",
                    "Retry:RetryOnStatusCodes": "502,503,504",
                    "Retry:RetryNonIdempotent": "false"
                };
                // Copy existing metadata
                Object.keys(route.metadata).forEach(function(key) {
                    yarpRoute.Metadata[key] = route.metadata[key];
                });
                // Add missing retry defaults
                Object.keys(retryDefaults).forEach(function(key) {
                    if (!yarpRoute.Metadata.hasOwnProperty(key)) {
                        yarpRoute.Metadata[key] = retryDefaults[key];
                    }
                });
            } else {
                // Add default retry config if no metadata exists
                yarpRoute.Metadata = {
                    "Retry:Enabled": "false",
                    "Retry:MaxRetries": "2",
                    "Retry:RetryOnStatusCodes": "502,503,504",
                    "Retry:RetryNonIdempotent": "false"
                };
            }

            const json = JSON.stringify(yarpRoute, null, 2);
            navigator.clipboard.writeText(json).then(function() {
                window.DashboardModals.showSuccess(__('index.copied'));
            });
        },
        
        // ===== Create Copy Button =====
        createCopyButton: function(text) {
            const btn = window.DashboardDOM.create('button', {
                className: 'copy-btn',
                attributes: { title: __('index.copy') },
                events: {
                    click: (e) => {
                        e.stopPropagation();
                        if (!text) return;
                        navigator.clipboard.writeText(text).then(() => {
                            btn.classList.add('copied');
                            const icon = btn.querySelector('i');
                            if (icon) {
                                icon.className = 'bi bi-check2';
                            }
                            setTimeout(() => {
                                btn.classList.remove('copied');
                                if (icon) {
                                    icon.className = 'bi bi-clipboard';
                                }
                            }, 1500);
                        });
                    }
                }
            });
            const icon = window.DashboardDOM.create('i', {
                className: 'bi bi-clipboard',
                style: { fontSize: '12px' }
            });
            btn.appendChild(icon);
            return btn;
        },

        // ===== Create Source Badge (delegates to shared utility) =====
        createSourceBadge: function(source) {
            return window.DashboardUtils.createSourceBadge(source);
        },
        
        // ===== Create Order Badge HTML =====
        createOrderBadgeHtml: function(order) {
            const displayOrder = (order !== null && order !== undefined) ? order : '-';
                    
            let cssClass, iconClass;
            if (order !== null && order !== undefined) {
                if (order < 50) {
                    cssClass = 'priority-high';
                    iconClass = 'bi-arrow-up-circle-fill';
                } else if (order < 100) {
                    cssClass = 'priority-medium';
                    iconClass = 'bi-arrow-right-circle-fill';
                } else {
                    cssClass = 'priority-low';
                    iconClass = 'bi-arrow-down-circle-fill';
                }
            } else {
                cssClass = 'priority-none';
                iconClass = 'bi-dash-circle';
            }
                    
            return `<span class="priority-badge ${cssClass}"><i class="bi ${iconClass}"></i> ${displayOrder}</span>`;
        },

        // ===== Create Method Badge Inline =====
        createMethodBadgeInline: function(method) {
            const methodMap = {
                'GET': { css: 'bg-success', icon: 'bi-arrow-down-circle-fill' },
                'POST': { css: 'bg-primary', icon: 'bi-plus-circle-fill' },
                'PUT': { css: 'bg-info', icon: 'bi-pencil-circle-fill' },
                'DELETE': { css: 'bg-danger', icon: 'bi-trash-circle-fill' },
                'PATCH': { css: 'bg-warning text-dark', icon: 'bi-gear-circle-fill' }
            }; 
            const config = methodMap[method] || { css: 'bg-secondary', icon: 'bi-circle-fill' }; 
            return `<span class="badge ${config.css}" style="display:inline-flex;align-items:center;gap:4px;font-size:11px;margin-right:4px;"><i class="bi ${config.icon}"></i>${method}</span>`;
        },


        // ===== Toggle Route (Direct DOM Manipulation) =====
        toggleRoute: function(routeId) {
            const state = window.DashboardState;
            const expandedSet = state.get('ui.expandedRoutes');
            const current = expandedSet ? expandedSet.has(routeId) : false;
            
            // Update state
            if (current) {
                expandedSet.delete(routeId);
            } else {
                expandedSet.add(routeId);
            }
            
            // Direct DOM manipulation for better performance
            const routeRow = document.querySelector(`.route-row[data-route-id="${CSS.escape(routeId)}"]`);
            if (routeRow) {
                // Update expand icon
                const expandIcon = routeRow.querySelector('.row-expand-icon');
                if (expandIcon) {
                    expandIcon.classList.toggle('expanded', !current);
                }
                
                // Toggle detail row visibility
                const detailRow = routeRow.nextElementSibling;
                if (detailRow && detailRow.classList.contains('route-detail-row')) {
                    // Remove existing detail row
                    detailRow.remove();
                } else if (!current) {
                    // Add detail row after main row
                    const route = state.get('data.routes').find(r => r.routeId === routeId);
                    if (route) {
                        const detailTr = this.createRouteDetailRow(route);
                        routeRow.after(detailTr);
                    }
                }
            }
        },

        // ===== Update Refresh Time =====
        updateRefreshTime: function() {
            const timeEl = window.DashboardDOM.safe('#route-refresh-time');
            if (timeEl) {
                timeEl.textContent = __('index.route.updated') + window.DashboardI18n.formatTime(new Date());
            }
        },

        // ===== Show Add Modal (Form Mode with JSON toggle) =====
        showAddModal: function() {
            this.showAddFormModal();
        },

        // ===== Show Add Form Modal =====
        showAddFormModal: async function() {
            const self = this;

            // Get available clusters (lazy-load if not yet fetched)
            let clusters = window.DashboardState.get('data.clusters') || [];
            if (clusters.length === 0) {
                await self.ensureClustersLoaded();
                clusters = window.DashboardState.get('data.clusters') || [];
            }
            const clusterIds = clusters.map(c => c.clusterId);

            // If no clusters, show warning
            if (clusterIds.length === 0) {
                window.DashboardModals.showWarning(__('index.route.noClusters'));
                return;
            }

            const clusterOptions = clusterIds.map(id => ({ value: id, label: id }));

            window.DashboardModals.showFormModal({
                title: __('modal.addRoute'),
                icon: 'bi-plus-circle',
                size: 'lg',
                fields: [
                    { name: 'routeId', label: 'Route ID', type: 'text', required: true, placeholder: 'my-route' },
                    { name: 'clusterId', label: __('index.route.clusterId') || 'Cluster ID', type: 'select', required: true, options: clusterOptions, value: clusterIds[0] },
                    { name: 'matchPath', label: __('index.route.matchPath') || 'Match Path', type: 'text', required: true, placeholder: '/api/service/{**catchAll}', value: '/api/service/{**catchAll}' },
                    { name: 'order', label: __('index.route.order') || 'Order', type: 'number', value: '50', min: '0', max: '1000' }
                ],
                data: { clusterId: clusterIds[0], matchPath: '/api/service/{**catchAll}', order: '50' },
                jsonModeCallback: function() {
                    self._showAddJsonModal();
                },
                onSave: function(formData) {
                    const routeConfig = {
                        ClusterId: formData.clusterId,
                        Order: parseInt(formData.order) || 50,
                        Match: {
                            Path: formData.matchPath || '/api/{**catchAll}'
                        },
                        Metadata: {}
                    };

                    if (!routeConfig.ClusterId || !routeConfig.ClusterId.trim()) {
                        window.DashboardModals.showError(__('index.route.invalidCluster'));
                        return false;
                    }
                    if (!routeConfig.Match.Path) {
                        window.DashboardModals.showError(__('index.route.invalidMatch'));
                        return false;
                    }

                    self.saveRouteFromJson(routeConfig, formData.routeId);
                    return true;
                }
            });
        },

        // ===== Show Add Modal (JSON Mode) =====
        _showAddJsonModal: async function() {
            const self = this;

            // Get available clusters (lazy-load if not yet fetched)
            let clusters = window.DashboardState.get('data.clusters') || [];
            if (clusters.length === 0) {
                await self.ensureClustersLoaded();
                clusters = window.DashboardState.get('data.clusters') || [];
            }
            const clusterIds = clusters.map(c => c.clusterId);

            // If no clusters, show warning
            if (clusterIds.length === 0) {
                window.DashboardModals.showWarning(__('index.route.noClusters'));
                return;
            }

            // Default route template for new route (includes retry config example)
            const defaultRoute = {
                "ClusterId": clusterIds[0] || "",
                "Order": 50,
                "Match": {
                    "Path": "/api/service/{**catchAll}"
                },
                "Metadata": {
                    "Retry:Enabled": "false",
                    "Retry:MaxRetries": "2",
                    "Retry:RetryOnStatusCodes": "502,503,504",
                    "Retry:RetryNonIdempotent": "false"
                }
            };

            window.DashboardModals.showJsonModal({
                title: __('modal.addRoute'),
                data: defaultRoute,
                schemaType: 'route',
                size: 'xl',
                onSave: function(parsedData) {
                    // Validate route config
                    if (!parsedData.ClusterId || !parsedData.ClusterId.trim()) {
                        window.DashboardModals.showError(__('index.route.invalidCluster'));
                        return false;
                    }
                    if (!parsedData.Match || (!parsedData.Match.Path && !parsedData.Match.Hosts)) {
                        window.DashboardModals.showError(__('index.route.invalidMatch'));
                        return false;
                    }
                    // Check cluster exists
                    if (clusterIds.indexOf(parsedData.ClusterId) === -1) {
                        window.DashboardModals.showWarning(__('index.route.clusterNotFound') + parsedData.ClusterId);
                        // Still allow save for flexibility
                    }

                    // Save route
                    self.saveRouteFromJson(parsedData);
                    return true;
                }
            });
        },
        
        // ===== Save Route from JSON =====
        saveRouteFromJson: async function(routeConfig, routeId) {
            try {
                // Generate routeId from user input or from existing
                if (!routeId) {
                    const id = await this.promptRouteId();
                    if (!id) return;
                    routeId = id;
                }
        
                window.DashboardModals.showInfo(__('index.route.saving'));
        
                // Convert to API format (YARP format is already correct)
                const response = await window.DashboardApi.endpoints.saveRoute(routeId, routeConfig);
        
                window.DashboardModals.showSuccess(__('index.route.saved'));
                await this.loadRoutes(true);
        
                document.dispatchEvent(new CustomEvent('dashboard:configChanged', {
                    detail: { type: 'route', id: routeId, action: 'save' }
                }));
            } catch (error) {
                console.error('[Routes] Save failed:', error);
                window.DashboardModals.showError(__('index.route.saveFailed') + error.message);
            }
        },
        
        // ===== Prompt Route ID =====
        promptRouteId: function() {
            return new Promise(function(resolve) {
                const modalId = 'route-id-prompt-' + Date.now();
                const modalHtml = `
                    <div class="modal fade" id="${modalId}" tabindex="-1">
                        <div class="modal-dialog modal-dialog-centered">
                            <div class="modal-content" style="border-radius:16px;border:none;box-shadow:0 25px 50px rgba(0,0,0,0.25);overflow:hidden;">
                                <div class="modal-header" style="background:linear-gradient(135deg,#f8fafc 0%,#e2e8f0 100%);border-bottom:1px solid #e2e8f0;padding:18px 24px;">
                                    <h5 class="modal-title" style="font-weight:600;font-size:16px;display:flex;align-items:center;gap:10px;">
                                        <span style="display:inline-flex;align-items:center;justify-content:center;width:32px;height:32px;border-radius:8px;background:linear-gradient(135deg,#6366f1,#818cf8);color:#fff;font-size:16px;">
                                            <i class="bi bi-tag"></i>
                                        </span>
                                        ${__('modal.routeId')}
                                    </h5>
                                    <button type="button" class="btn-close" data-bs-dismiss="modal"></button>
                                </div>
                                <div class="modal-body" style="padding:24px;">
                                    <div style="margin-bottom:12px;">
                                        <label class="form-label" style="font-weight:500;font-size:13px;color:#334155;">Route ID</label>
                                        <input type="text" class="form-control" id="${modalId}-input" 
                                               placeholder="${__('modal.routeIdPlaceholder')}"
                                               required
                                               style="border-radius:8px;padding:10px 14px;font-size:14px;border:1.5px solid #e2e8f0;transition:border-color 0.2s,box-shadow 0.2s;"
                                               onfocus="this.style.borderColor='#6366f1';this.style.boxShadow='0 0 0 3px rgba(99,102,241,0.1)'"
                                               onblur="this.style.borderColor='#e2e8f0';this.style.boxShadow='none'">
                                    </div>
                                    <div style="background:#f0f9ff;border:1px solid #bae6fd;border-radius:8px;padding:10px 14px;font-size:12px;color:#0369a1;display:flex;align-items:flex-start;gap:8px;">
                                        <i class="bi bi-info-circle" style="font-size:14px;margin-top:2px;flex-shrink:0;"></i>
                                        <span>${__('modal.routeIdHelp')}</span>
                                    </div>
                                </div>
                                <div class="modal-footer" style="background:#f8fafc;border-top:1px solid #e2e8f0;padding:14px 24px;gap:8px;">
                                    <button type="button" class="btn btn-secondary btn-sm" data-bs-dismiss="modal" style="min-width:70px;">${__('modal.cancelBtn')}</button>
                                    <button type="button" class="btn btn-primary btn-sm" id="${modalId}-confirm" style="min-width:70px;">${__('modal.confirmBtn')}</button>
                                </div>
                            </div>
                        </div>
                    </div>
                `;
                        
                document.body.insertAdjacentHTML('beforeend', modalHtml);
                const modalEl = document.getElementById(modalId);
                const bsModal = new bootstrap.Modal(modalEl, { backdrop: 'static', keyboard: false });
                const inputEl = document.getElementById(modalId + '-input');
        
                document.getElementById(modalId + '-confirm').addEventListener('click', function() {
                    const value = inputEl.value.trim();
                    if (!value) {
                        inputEl.classList.add('is-invalid');
                        return;
                    }
                    bsModal.hide();
                    resolve(value);
                });
        
                // Enter key to submit
                inputEl.addEventListener('keydown', function(e) {
                    if (e.key === 'Enter') {
                        e.preventDefault();
                        document.getElementById(modalId + '-confirm').click();
                    }
                });
        
                modalEl.addEventListener('hidden.bs.modal', function() {
                    modalEl.remove();
                    if (!inputEl.value.trim()) resolve(null);
                });
        
                bsModal.show();
                inputEl.focus();
            });
        },

        // ===== Show Edit Modal (JSON Mode) =====
        showEditModal: function(routeId) {
            const self = this;
                    
            // Get route data
            const routes = window.DashboardState.get('data.routes') || [];
            const route = routes.find(r => r.routeId === routeId);
                    
            if (!route) {
                window.DashboardModals.showError(__('index.route.notFound'));
                return;
            }

            // Build YARP format route config for JSON editor
            const yarpRoute = {
                "ClusterId": route.clusterId || "",
                "Order": route.order || 50,
                "Match": {
                    "Path": route.match?.path || route.path || ""
                }
            }; 
        
            // Add methods if exists
            const methods = route.match?.methods || route.methods || [];
            if (methods && methods.length > 0) {
                yarpRoute.Match.Methods = methods;
            }
        
            // Add hosts if exists
            const hosts = route.match?.hosts || [];
            if (hosts && hosts.length > 0) {
                yarpRoute.Match.Hosts = hosts;
            }
        
            // Add headers if exists
            if (route.match?.headers && Object.keys(route.match.headers).length > 0) {
                yarpRoute.Match.Headers = route.match.headers;
            }
        
            // Add transforms if exists
            if (route.transforms && route.transforms.length > 0) {
                yarpRoute.Transforms = route.transforms;
            }
        
            // Add authorization policy if exists
            if (route.authorizationPolicy) {
                yarpRoute.AuthorizationPolicy = route.authorizationPolicy;
            }
        
            // Add CORS policy if exists
            if (route.corsPolicy) {
                yarpRoute.CorsPolicy = route.corsPolicy;
            }
        
            // Add timeout if exists
            if (route.timeout) {
                yarpRoute.Timeout = route.timeout;
            }
        
            // Add timeout policy if exists
            if (route.timeoutPolicy) {
                yarpRoute.TimeoutPolicy = route.timeoutPolicy;
            }
        
            // Add metadata with retry config (ensure retry defaults are present)
            yarpRoute.Metadata = {};
            // Copy existing metadata
            if (route.metadata && Object.keys(route.metadata).length > 0) {
                Object.keys(route.metadata).forEach(function(key) {
                    yarpRoute.Metadata[key] = route.metadata[key];
                });
            }
            // Ensure retry config defaults are present
            const retryDefaults = {
                "Retry:Enabled": "false",
                "Retry:MaxRetries": "2",
                "Retry:RetryOnStatusCodes": "502,503,504",
                "Retry:RetryNonIdempotent": "false"
            };
            Object.keys(retryDefaults).forEach(function(key) {
                if (!yarpRoute.Metadata.hasOwnProperty(key)) {
                    yarpRoute.Metadata[key] = retryDefaults[key];
                }
            });
        
            window.DashboardModals.showJsonModal({
                title: __('modal.editRoute'),
                data: yarpRoute,
                schemaType: 'route',
                size: 'xl',
                editableId: {
                    label: 'Route ID',
                    value: routeId,
                    original: routeId,
                    placeholder: __('modal.routeIdPlaceholder')
                },
                onSave: function(parsedData, newId) {
                    // Validate route config
                    if (!parsedData.ClusterId || !parsedData.ClusterId.trim()) {
                        window.DashboardModals.showError(__('index.route.invalidCluster'));
                        return false;
                    }
                    if (!parsedData.Match || (!parsedData.Match.Path && !parsedData.Match.Hosts)) {
                        window.DashboardModals.showError(__('index.route.invalidMatch'));
                        return false;
                    }

                    // Handle rename: only if ID actually changed (case-sensitive comparison)
                    if (newId && newId !== routeId) {
                        self.renameRoute(routeId, newId, parsedData);
                    } else {
                        self.saveRouteFromJson(parsedData, routeId);
                    }
                    return true;
                }
            });
        },

        // ===== Rename Route =====
        renameRoute: async function(oldId, newId, routeConfig) {
            const self = this;
            try {
                window.DashboardModals.showInfo(__('index.route.renaming'));

                // Create new route with new ID FIRST (cluster already exists via old route)
                await window.DashboardApi.endpoints.saveRoute(newId, routeConfig);

                // Delete old route AFTER (new route already references the cluster, so don't delete it)
                if (oldId !== newId) {
                    try {
                        await window.DashboardApi.delete(`/api/config/routes/${encodeURIComponent(oldId)}?removeOrphanedCluster=false`);
                    } catch (e) {
                        console.warn('[Routes] Failed to delete old route after rename:', e);
                    }
                }

                window.DashboardModals.showSuccess(__('index.route.renamed'));
                await self.loadRoutes(true);

                document.dispatchEvent(new CustomEvent('dashboard:configChanged', {
                    detail: { type: 'route', id: newId, oldId: oldId, action: 'rename' }
                }));
            } catch (error) {
                console.error('[Routes] Rename failed:', error);
                window.DashboardModals.showError(__('index.route.renameFailed') + error.message);
            }
        },

        // ===== Delete Route =====
        deleteRoute: async function(routeId) {
            const self = this;
            
            window.DashboardModals.showConfirm(
                __('index.route.deleteConfirm').replace('{id}', routeId),
                async function() {
                    try {
                        window.DashboardModals.showInfo(__('index.route.deleting'));
                        
                        await window.DashboardApi.endpoints.deleteRouteConfig(routeId);
                        await self.loadRoutes(true);
                        
                        window.DashboardModals.showSuccess(__('index.route.deleted'));

                        // Trigger config deleted event
                        document.dispatchEvent(new CustomEvent('dashboard:configChanged', {
                            detail: { type: 'route', id: routeId, action: 'delete' }
                        }));
                    } catch (error) {
                        console.error('[Routes] Delete failed:', error);
                        window.DashboardModals.showError(__('index.route.deleteFailed') + error.message);
                    }
                },
                null,
                { title: __('modal.deleteRoute'), danger: true }
            );
        },

        // ===== Setup Events =====
        setupEvents: function() {
            // Refresh shortcut
            document.addEventListener('dashboard:shortcut:refresh', async () => {
                await this.loadRoutes();
            });

            // Locale change
            document.addEventListener('dashboard:localeChange', () => {
                this.renderRoutes();
            });

            // Setup event delegation for card and table views
            this._setupEventDelegation();
        },

        // ===== Event Delegation Setup =====
        _setupEventDelegation: function() {
            // Table view: individual row click handlers are set in createRouteMainRow (line ~855),
            // createActionButtons buttons have stopPropagation, so no delegation needed.
        },

        // ===== Helper: Find Route by ID =====
        _findRouteById: function(routeId) {
            const routes = window.DashboardState.get('data.routes') || [];
            return routes.find(r => r.routeId === routeId);
        },

        // ===== Optimized Render Methods =====

        // ===== Create Route Card (DOM-based for performance) =====
        createRouteCardDOM: function(route) {
            const pathText = route.match && route.match.path || '-';
            const methods = route.match && route.match.methods || [];
            const hostText = route.match && route.match.hosts && route.match.hosts.length > 0 ? route.match.hosts[0] : '';
            const hasTransforms = route.transforms && route.transforms.length > 0;
            const hasAuthorization = route.authorizationPolicy || route.authorizationPolicy === '';
            const accentColor = route.clusterId ? '#3b82f6' : '#94a3b8';

            // Create card element
            const card = document.createElement('div');
            card.className = 'route-card';
            card.dataset.routeId = route.routeId;
            card.dataset.key = route.routeId; // For diff tracking
            card.style.cssText = 'border:1px solid var(--border-color);border-left:4px solid ' + accentColor + 
                ';border-radius:12px;background:var(--card-bg);overflow:hidden;transition:box-shadow 0.2s,transform 0.15s;cursor:pointer;';
            
            // Hover effects
            card.addEventListener('mouseenter', function() {
                this.style.boxShadow = '0 4px 12px rgba(0,0,0,0.08)';
                this.style.transform = 'translateY(-1px)';
            });
            card.addEventListener('mouseleave', function() {
                this.style.boxShadow = 'none';
                this.style.transform = 'none';
            });

            // Card content HTML
            card.innerHTML = this._buildCardHTML(route, pathText, methods, hostText, hasTransforms, hasAuthorization);

            return card;
        },

        // ===== Build Card HTML (separate for easier maintenance) =====
        _buildCardHTML: function(route, pathText, methods, hostText, hasTransforms, hasAuthorization) {
            const orderBadge = route.order !== null && route.order !== undefined 
                ? '<span class="route-order-badge" style="background:' + (route.order < 100 ? '#fef3c7' : route.order < 1000 ? '#e0e7ff' : '#f1f5f9') + ';color:' + (route.order < 100 ? '#92400e' : route.order < 1000 ? '#3730a3' : '#64748b') + '">' + route.order + '</span>'
                : '<span class="route-order-badge" style="background:#f1f5f9;color:#94a3b8">-</span>';

            const hostBadge = hostText 
                ? '<span class="route-host"><i class="bi bi-globe"></i>' + window.DashboardUtils.escapeHtml(hostText) + '</span>' 
                : '';

            const indicators = [];
            if (hasTransforms) indicators.push('<i class="bi bi-arrow-left-right" style="color:#8b5cf6;" title="Transforms"></i>');
            if (hasAuthorization) indicators.push('<i class="bi bi-shield-lock" style="color:#f59e0b;" title="Authorization"></i>');
            const indicatorHtml = indicators.length > 0 ? '<span class="route-indicators">' + indicators.join('') + '</span>' : '';

            let methodsHtml = '';
            if (methods.length > 0) {
                const mColors = { 'GET': '#22c55e', 'POST': '#3b82f6', 'PUT': '#f59e0b', 'DELETE': '#ef4444', 'PATCH': '#8b5cf6', 'HEAD': '#64748b', 'OPTIONS': '#94a3b8' };
                const mBg = { 'GET': '#f0fdf4', 'POST': '#eff6ff', 'PUT': '#fffbeb', 'DELETE': '#fef2f2', 'PATCH': '#f5f3ff', 'HEAD': '#f8fafc', 'OPTIONS': '#f8fafc' };
                const mBorder = { 'GET': '#bbf7d0', 'POST': '#bfdbfe', 'PUT': '#fde68a', 'DELETE': '#fecaca', 'PATCH': '#ddd6fe', 'HEAD': '#e2e8f0', 'OPTIONS': '#e2e8f0' };
                methodsHtml = methods.map(m => {
                    const c = mColors[m] || '#64748b';
                    return '<span class="route-method" style="background:' + (mBg[m] || '#f8fafc') + ';color:' + c + ';border-color:' + (mBorder[m] || '#e2e8f0') + '">' + m + '</span>';
                }).join('');
            } else {
                methodsHtml = '<span class="route-method-any">ANY</span>';
            }

            const clusterHtml = route.clusterId 
                ? '<span class="route-cluster"><i class="bi bi-diagram-3"></i>' + window.DashboardUtils.escapeHtml(route.clusterId) + '</span>' 
                : '';

            return '<div class="route-card-header">' +
                '<div class="route-card-title">' +
                orderBadge +
                '<div class="route-name-wrap">' +
                '<div class="route-name" title="' + window.DashboardUtils.escapeHtml(route.routeId) + '">' + window.DashboardUtils.escapeHtml(route.routeId) + '</div>' +
                '<div class="route-meta">' +
                window.DashboardUtils.createSourceBadge(route.source) +
                hostBadge +
                indicatorHtml +
                '</div>' +
                '</div>' +
                '</div>' +
                '<div class="route-card-actions">' +
                '<button class="btn-edit btn btn-sm" title="' + (window.__ ? __("index.route.edit") : "编辑") + '" data-action="edit"><i class="bi bi-pencil"></i></button>' +
                '<button class="btn-delete btn btn-sm" title="' + (window.__ ? __("index.route.delete") : "删除") + '" data-action="delete"><i class="bi bi-trash"></i></button>' +
                '</div>' +
                '</div>' +
                '<div class="route-card-body">' +
                '<div class="route-path-row">' +
                '<span class="route-path-icon"><i class="bi bi-signpost-2"></i></span>' +
                '<code class="route-path">' + window.DashboardUtils.escapeHtml(pathText) + '</code>' +
                '</div>' +
                '<div class="route-bottom-row">' +
                clusterHtml +
                methodsHtml +
                '</div>' +
                '</div>';
        },

        // ===== Create Route Row for Table (with key for diff) =====
        createRouteRowsOptimized: function(route, isExpanded) {
            var rows = [];

            // Main row
            var mainTr = this.createRouteMainRow(route, isExpanded);
            mainTr.dataset.key = route.routeId; // For diff tracking
            rows.push(mainTr);

            // Expanded detail row
            if (isExpanded) {
                var detailTr = this.createRouteDetailRow(route);
                detailTr.dataset.key = route.routeId + '-detail';
                rows.push(detailTr);
            }

            return rows;
        }
    };

    // Register module
    if (window.DashboardApp) {
        window.DashboardApp.registerModule('routes', RoutesModule);
    }

    // Expose to window
    window.RoutesModule = RoutesModule;
    
    // Global function for external route creation triggers.
    window.showAddRouteModal = function() {
        if (RoutesModule && typeof RoutesModule.showAddModal === 'function') {
            RoutesModule.showAddModal();
        }
    };

})();
