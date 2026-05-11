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
            
            try {
                // Load initial data
                await this.loadRoutes();
                
                // Setup event listeners
                this.setupEvents();
                
                this.initialized = true;
                console.log('[Routes] Initialized');
            } catch (error) {
                console.error('[Routes] Init failed:', error);
                throw error;
            }
        },

        // ===== Load Routes =====
        loadRoutes: async function() {
            try {
                const container = window.DashboardDOM.safe('#route-tbody');
                if (!container) return;

                window.DashboardDOM.showLoading(container, __('index.route.loading'));

                const routes = await window.DashboardApi.endpoints.getRoutes();
                
                // Update state
                window.DashboardState.set('data.routes', routes || []);

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

        // ===== Render Routes =====
        renderRoutes: function() {
            const state = window.DashboardState;
            const routes = state.getFilteredRoutes();
                    
            // Render filter toolbar (only once, then update counts)
            this.renderFilterToolbar();
                    
            const tbody = window.DashboardDOM.safe('#route-tbody');
            if (!tbody) {
                console.error('[Routes] tbody not found, cannot render');
                return;
            }
        
            // Always clear tbody content, not the parent table
            window.DashboardDOM.clear(tbody);
                    
            if (routes.length === 0) {
                // Show empty state INSIDE tbody, not replacing it
                const emptyRow = document.createElement('tr');
                emptyRow.innerHTML = `
                    <td colspan="8" class="text-center py-5">
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
                                <button class="btn btn-sm btn-success" id="route-add-btn" title="${__('index.route.add')}">
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
                        
        // ===== Bind Filter Events =====
        _bindFilterEvents: function() {
            const self = this;
            
            // Search input - live search on input
            const searchInput = document.getElementById('route-search-input');
            if (searchInput) {
                // Debounced live search
                let searchTimeout = null;
                searchInput.addEventListener('input', function(e) {
                    clearTimeout(searchTimeout);
                    searchTimeout = setTimeout(function() {
                        window.DashboardState.set('filters.routes.search', searchInput.value);
                        self.renderRoutes();
                    }, 300);
                });
            }
            
            // Clear button
            const clearBtn = document.getElementById('route-clear-btn');
            if (clearBtn) {
                clearBtn.addEventListener('click', function() {
                    const input = document.getElementById('route-search-input');
                    if (input) input.value = ''; 
                    window.DashboardState.set('filters.routes.search', '');
                    window.DashboardState.set('filters.routes.method', 'all');
                    window.DashboardState.set('filters.routes.source', 'all');
                    self.renderRoutes();
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
                addBtn.addEventListener('click', function() {
                    self.showAddModal();
                });
            }
        },

                
        // ===== Render Route Rows =====
        renderRouteRows: function(routes, tbody) {
            window.DashboardDOM.clear(tbody);

            const fragment = document.createDocumentFragment();

            // Sort by order
            const sortedRoutes = [...routes].sort((a, b) => {
                const orderA = a.order !== null && a.order !== undefined ? a.order : 999999;
                const orderB = b.order !== null && b.order !== undefined ? b.order : 999999;
                return orderA - orderB;
            });

            sortedRoutes.forEach(route => {
                const isExpanded = (window.DashboardState.get('ui.expandedRoutes') || new Set()).has(route.routeId);
                const rows = this.createRouteRows(route, isExpanded);
                rows.forEach(row => fragment.appendChild(row));
            });

            tbody.appendChild(fragment);
        },

        // ===== Create Route Rows =====
        createRouteRows: function(route, isExpanded) {
            const rows = [];

            // Main row
            const mainTr = this.createRouteMainRow(route, isExpanded);
            rows.push(mainTr);

            // Expanded detail row
            if (isExpanded) {
                const detailTr = this.createRouteDetailRow(route);
                rows.push(detailTr);
            }

            return rows;
        },

        // ===== Create Route Main Row =====
        createRouteMainRow: function(route, isExpanded) {
            const tr = window.DashboardDOM.create('tr', {
                className: 'route-row',
                attributes: { 'data-route-id': route.routeId },
                style: { cursor: 'pointer' }
            });

            // Expand column
            const tdExpand = window.DashboardDOM.create('td', {
                style: { verticalAlign: 'middle', textAlign: 'center' }
            });
            const expandIcon = window.DashboardDOM.create('i', {
                className: `bi bi-chevron-right row-expand-icon ${isExpanded ? 'expanded' : ''}`
            });
            tdExpand.appendChild(expandIcon);
            tr.appendChild(tdExpand);

            // Order
            const tdOrder = window.DashboardDOM.create('td', {
                style: { textAlign: 'center' }
            });
            tdOrder.appendChild(this.createOrderBadge(route.order));
            tr.appendChild(tdOrder);

            // Route ID - with copy button
            const tdId = window.DashboardDOM.create('td', {
                style: { fontWeight: '500', overflow: 'hidden' }
            });
            const nameWrapper = window.DashboardDOM.create('div', {
                className: 'cell-with-copy'
            });
            const nameSpan = window.DashboardDOM.create('span', {
                className: 'cell-text',
                textContent: route.routeId,
                attributes: { title: route.routeId }
            });
            const nameCopyBtn = this.createCopyButton(route.routeId);
            nameWrapper.appendChild(nameSpan);
            nameWrapper.appendChild(nameCopyBtn);
            tdId.appendChild(nameWrapper);
            tr.appendChild(tdId);

            // Source column
            const tdSource = window.DashboardDOM.create('td', {});
            const sourceBadgeSpan = window.DashboardDOM.create('span', {});
            sourceBadgeSpan.innerHTML = this.createSourceBadge(route.source);
            tdSource.appendChild(sourceBadgeSpan);
            tr.appendChild(tdSource);

            // Click to expand (on the row)
            tr.addEventListener('click', (e) => {
                if (e.target.closest('.copy-btn')) return;
                this.toggleRoute(route.routeId);
            });

            // Path - with copy button
            const pathText = route.match?.path || '-';
            const tdPath = window.DashboardDOM.create('td', {
                style: { overflow: 'hidden' }
            });
            const pathWrapper = window.DashboardDOM.create('div', {
                className: 'cell-with-copy'
            });
            const pathSpan = window.DashboardDOM.create('span', {
                className: 'cell-text'
            });
            const pathCode = window.DashboardDOM.create('code', {
                textContent: pathText,
                attributes: { title: pathText }
            });
            pathSpan.appendChild(pathCode);
            const pathCopyBtn = this.createCopyButton(route.match?.path || '');
            pathWrapper.appendChild(pathSpan);
            pathWrapper.appendChild(pathCopyBtn);
            tdPath.appendChild(pathWrapper);
            tr.appendChild(tdPath);

            // Cluster - with copy button and ellipsis
            const tdCluster = window.DashboardDOM.create('td', {
                style: { overflow: 'hidden' }
            });
            if (route.clusterId) {
                const clusterWrapper = window.DashboardDOM.create('div', {
                    className: 'cell-with-copy'
                });
                const clusterBadge = window.DashboardDOM.create('span', {
                    className: 'badge bg-primary cell-text',
                    textContent: route.clusterId,
                    attributes: { title: route.clusterId },
                    style: { fontSize: '12px', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', maxWidth: '100%' }
                });
                const clusterCopyBtn = this.createCopyButton(route.clusterId);
                clusterWrapper.appendChild(clusterBadge);
                clusterWrapper.appendChild(clusterCopyBtn);
                tdCluster.appendChild(clusterWrapper);
            } else {
                const span = window.DashboardDOM.create('span', {
                    className: 'text-muted',
                    textContent: '-'
                });
                tdCluster.appendChild(span);
            }
            tr.appendChild(tdCluster);

            // Methods
            const tdMethods = window.DashboardDOM.create('td', {});
            const methods = route.match?.methods;
            if (methods && methods.length > 0) {
                methods.forEach(method => {
                    const methodBadge = this.createMethodBadge(method);
                    tdMethods.appendChild(methodBadge);
                });
            } else {
                const span = window.DashboardDOM.create('span', {
                    className: 'text-muted',
                    textContent: __('index.route.allMethods')
                });
                tdMethods.appendChild(span);
            }
            tr.appendChild(tdMethods);

            // Actions
            const tdActions = window.DashboardDOM.create('td', {});
            tdActions.appendChild(this.createActionButtons(route));
            tr.appendChild(tdActions);

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
                attributes: { colspan: '8' }
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

            // Metadata - structured key-value display
            if (route.metadata && Object.keys(route.metadata).length > 0) {
                detailHtml.push('<div class="detail-section">');
                detailHtml.push(`<div class="detail-section-title"><i class="bi bi-tags"></i>${__('index.route.metadata')}</div>`);
                detailHtml.push(this.renderStructuredConfig(route.metadata, 'metadata'));
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
            if (route.metadata && Object.keys(route.metadata).length > 0) yarpRoute.Metadata = route.metadata;

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

        // ===== Show Add Modal (JSON Mode) =====
        showAddModal: function() {
            const self = this;
                    
            // Get available clusters
            const clusters = window.DashboardState.get('data.clusters') || [];
            const clusterIds = clusters.map(c => c.clusterId);
                    
            // If no clusters, show warning
            if (clusterIds.length === 0) {
                window.DashboardModals.showWarning(__('index.route.noClusters'));
                return;
            }
        
            // Default route template for new route
            const defaultRoute = {
                "ClusterId": clusterIds[0] || "",
                "Order": 50,
                "Match": {
                    "Path": "/api/service/{**catchAll}"
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
                await this.loadRoutes();
        
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
                const bsModal = new bootstrap.Modal(modalEl);
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
        
            // Add metadata if exists
            if (route.metadata && Object.keys(route.metadata).length > 0) {
                yarpRoute.Metadata = route.metadata;
            }
        
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
                await self.loadRoutes();

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
                __('index.route.deleteConfirm').replace('{id}', routeId) || `确认删除路由 '${routeId}'？此操作不可撤销。`,
                async function() {
                    try {
                        window.DashboardModals.showInfo(__('index.route.deleting'));
                        
                        await window.DashboardApi.endpoints.deleteRouteConfig(routeId);
                        await self.loadRoutes();
                        
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
        }
    };

    // Register module
    if (window.DashboardApp) {
        window.DashboardApp.registerModule('routes', RoutesModule);
    }

    // Expose to window
    window.RoutesModule = RoutesModule;
    
    // Global functions for onclick handlers
    window.showAddRouteModal = function() {
        if (RoutesModule.showAddModal) {
            RoutesModule.showAddModal();
        } else {
            console.warn('[Routes] showAddModal not implemented yet');
        }
    };

})();
