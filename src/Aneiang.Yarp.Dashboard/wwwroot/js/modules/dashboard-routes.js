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
                    <td colspan="6" class="text-center py-5 text-muted">
                        <i class="bi bi-signpost-split" style="font-size: 2rem; opacity: 0.5;"></i>
                        <div class="mt-2">${__('index.route.empty') || '暂无路由数据'}</div>
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
        
            container.innerHTML = `
                <div class="card-body py-2 border-bottom">
                    <div class="row g-2 align-items-center">
                        <div class="col">
                            <div class="input-group input-group-sm">
                                <span class="input-group-text"><i class="bi bi-search"></i></span>
                                <input type="text" class="form-control" id="route-search-input" 
                                       placeholder="${__('index.route.search') || '搜索路由ID、集群、路径...'}..." 
                                       autocomplete="off">
                                <button class="btn btn-primary" type="button" id="route-search-btn">
                                    <i class="bi bi-arrow-right"></i>
                                </button>
                            </div>
                        </div>
                        <div class="col-auto">
                            <span class="text-muted small" id="route-search-result"></span>
                        </div>
                        <div class="col-auto">
                            <select class="form-select form-select-sm" id="route-method-select" style="width:auto;">
                                <option value="all">${__('index.route.method.all') || '全部'} (${methodCounts.all})</option>
                                <option value="GET">GET (${methodCounts.GET})</option>
                                <option value="POST">POST (${methodCounts.POST})</option>
                                <option value="PUT">PUT (${methodCounts.PUT})</option>
                                <option value="DELETE">DELETE (${methodCounts.DELETE})</option>
                            </select>
                        </div>
                        <div class="col-auto">
                            <button class="btn btn-sm btn-outline-secondary" type="button" id="route-search-clear" title="${__('index.search.clear') || '清除搜索'}" style="display:none;">\n                                <i class="bi bi-x-circle me-1"></i>${__('index.search.clear') || '清除'}\n                            </button>
                        </div>
                        <div class="col-auto">
                            <button class="btn btn-sm btn-success" id="route-add-btn">
                                <i class="bi bi-plus-circle me-1"></i>${__('index.route.add') || '新增'}
                            </button>
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
                    
            // Update result count
            const resultEl = document.getElementById('route-search-result');
            if (resultEl) {
                if (searchValue || methodValue !== 'all') {
                    resultEl.textContent = `${filteredRoutes.length}/${allRoutes.length}`;
                    resultEl.style.display = '';
                } else {
                    resultEl.textContent = `${allRoutes.length} ${__('index.route.total') || '个路由'}`;
                }
            }
                    
            // Update clear button visibility
            const clearBtn = document.getElementById('route-search-clear');
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
                        
        // ===== Bind Filter Events =====
        _bindFilterEvents: function() {
            const self = this;
                            
            // Search button click
            const searchBtn = document.getElementById('route-search-btn');
            if (searchBtn) {
                searchBtn.addEventListener('click', function() {
                    self._doSearch();
                });
            }
                    
            // Search input - Enter key triggers search
            const searchInput = document.getElementById('route-search-input');
            if (searchInput) {
                searchInput.addEventListener('keypress', function(e) {
                    if (e.key === 'Enter') {
                        e.preventDefault();
                        self._doSearch();
                    }
                });
            }
                            
            // Clear button
            const clearBtn = document.getElementById('route-search-clear');
            if (clearBtn) {
                clearBtn.addEventListener('click', function() {
                    const input = document.getElementById('route-search-input');
                    if (input) input.value = ''; 
                    window.DashboardState.set('filters.routes.search', '');
                    window.DashboardState.set('filters.routes.method', 'all');
                    self.renderRoutes();
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
                    
            // Add button
            const addBtn = document.getElementById('route-add-btn');
            if (addBtn) {
                addBtn.addEventListener('click', function() {
                    self.showAddModal();
                });
            }
        },
                
        // ===== Do Search =====
        _doSearch: function() {
            const searchInput = document.getElementById('route-search-input');
            if (searchInput) {
                window.DashboardState.set('filters.routes.search', searchInput.value);
                this.renderRoutes();
            }
        },
                
        // ===== Legacy methods (removed) =====
        initFilterHandlers: function() { /* deprecated */ },
        restoreFilterValues: function() { /* deprecated */ },

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
                const isExpanded = window.DashboardState.get(`ui.expandedRoutes.${route.routeId}`) || false;
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
                style: { cursor: 'pointer' }
            });

            // Order
            const tdOrder = window.DashboardDOM.create('td', {
                style: { textAlign: 'center' }
            });
            tdOrder.appendChild(this.createOrderBadge(route.order));
            tr.appendChild(tdOrder);

            // Route ID
            const tdId = window.DashboardDOM.create('td', {
                style: { fontWeight: '500' }
            });
            
            const expandIcon = window.DashboardDOM.create('span', {
                className: 'route-expand-icon',
                textContent: isExpanded ? '▼' : '▶',
                style: {
                    display: 'inline-block',
                    width: '16px',
                    marginRight: '4px'
                }
            });
            
            tdId.appendChild(expandIcon);
            tdId.appendChild(document.createTextNode(route.routeId));
            tr.appendChild(tdId);

            // Click to expand
            tr.addEventListener('click', () => this.toggleRoute(route.routeId));

            // Path
            const tdPath = window.DashboardDOM.create('td', {});
            const code = window.DashboardDOM.create('code', {
                textContent: route.match?.path || route.path || '-'
            });
            tdPath.appendChild(code);
            tr.appendChild(tdPath);

            // Cluster
            const tdCluster = window.DashboardDOM.create('td', {});
            if (route.clusterId) {
                const badge = window.DashboardDOM.create('span', {
                    className: 'badge bg-primary',
                    textContent: route.clusterId,
                    style: { fontSize: '12px' }
                });
                tdCluster.appendChild(badge);
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
            const methods = route.match?.methods || route.methods;
            if (methods && methods.length > 0) {
                methods.forEach(method => {
                    const methodBadge = window.DashboardDOM.create('span', {
                        className: `badge ${this.getMethodBadgeClass(method)} me-1`,
                        textContent: method,
                        style: { fontSize: '11px' }
                    });
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
            const className = (order !== null && order !== undefined && order < 100) ? 'badge bg-warning text-dark' : 'badge bg-light text-dark';
            
            return window.DashboardDOM.create('span', {
                className: className,
                textContent: displayOrder
            });
        },

        // ===== Get Method Badge Class =====
        getMethodBadgeClass: function(method) {
            const methodMap = {
                'GET': 'bg-success',
                'POST': 'bg-primary',
                'PUT': 'bg-info',
                'DELETE': 'bg-danger',
                'PATCH': 'bg-warning text-dark'
            };
            return methodMap[method] || 'bg-secondary';
        },

        // ===== Create Action Buttons =====
        createActionButtons: function(route) {
            const container = window.DashboardDOM.create('div', {
                className: 'd-flex gap-1'
            });

            // Edit button
            const editBtn = window.DashboardDOM.create('button', {
                className: 'btn btn-sm btn-outline-primary',
                textContent: __('index.route.edit'),
                events: {
                    click: (e) => {
                        e.stopPropagation();
                        this.showEditModal(route.routeId);
                    }
                }
            });

            // Delete button (only if editable)
            if (route.isEditable) {
                const deleteBtn = window.DashboardDOM.create('button', {
                    className: 'btn btn-sm btn-outline-danger',
                    textContent: __('index.route.delete'),
                    events: {
                        click: (e) => {
                            e.stopPropagation();
                            this.deleteRoute(route.routeId);
                        }
                    }
                });
                container.appendChild(deleteBtn);
            }

            container.appendChild(editBtn);

            return container;
        },

        // ===== Create Route Detail Row =====
        createRouteDetailRow: function(route) {
            const tr = window.DashboardDOM.create('tr', {
                className: 'route-detail-row'
            });

            const td = window.DashboardDOM.create('td', {
                attributes: { colspan: '6' },
                style: {
                    padding: '0'
                }
            });

            const detailDiv = window.DashboardDOM.create('div', {
                style: {
                    padding: '14px 20px',
                    background: '#f8fafc',
                    borderBottom: '2px solid #e2e8f0',
                    fontSize: '13px',
                    lineHeight: '1.8'
                }
            });

            const detailHtml = [];

            // Metadata row
            detailHtml.push('<div style="display:flex;flex-wrap:wrap;gap:8px 24px;margin-bottom:6px;">');
            detailHtml.push(`<span><strong>RouteId:</strong> ${window.DashboardUtils.escapeHtml(route.routeId)}</span>`);
            detailHtml.push(`<span><strong>ClusterId:</strong> <code>${window.DashboardUtils.escapeHtml(route.clusterId || '')}</code></span>`);
            detailHtml.push(`<span><strong>Order:</strong> ${route.order ?? '-'}</span>`);
            detailHtml.push(`<span><strong>Source:</strong> <span class="badge ${route.source === 'dynamic' ? 'bg-success' : 'bg-secondary'}">${route.source || 'static'}</span></span>`);
            detailHtml.push('</div>');

            // Match configuration
            const match = route.match || {};
            detailHtml.push(`<div style="margin-top:6px;margin-bottom:4px;"><strong>${__('index.route.match')}</strong></div>`);
            detailHtml.push('<div class="table-responsive">');
            detailHtml.push('<table class="table table-sm table-bordered" style="font-size:12px;">');
            detailHtml.push('<thead><tr><th style="width:150px;">Property</th><th>Value</th></tr></thead>');
            detailHtml.push('<tbody>');
            
            if (match.path) {
                detailHtml.push(`<tr><td>Path</td><td><code>${window.DashboardUtils.escapeHtml(match.path)}</code></td></tr>`);
            }
            if (match.hosts && match.hosts.length > 0) {
                detailHtml.push(`<tr><td>Hosts</td><td>${match.hosts.join(', ')}</td></tr>`);
            }
            if (match.methods && match.methods.length > 0) {
                detailHtml.push(`<tr><td>Methods</td><td>${match.methods.join(', ')}</td></tr>`);
            }
            if (match.headers && Object.keys(match.headers).length > 0) {
                detailHtml.push(`<tr><td>Headers</td><td>${this.renderJsonBlock(match.headers, 'Headers')}</td></tr>`);
            }
            if (match.queryParameters && Object.keys(match.queryParameters).length > 0) {
                detailHtml.push(`<tr><td>Query Params</td><td>${this.renderJsonBlock(match.queryParameters, 'Query Parameters')}</td></tr>`);
            }
            
            detailHtml.push('</tbody></table></div>');

            // Transforms
            if (route.transforms && route.transforms.length > 0) {
                detailHtml.push(`<div style="margin-top:8px;margin-bottom:4px;"><strong>${__('index.route.transforms')}</strong></div>`);
                detailHtml.push('<div class="table-responsive">');
                detailHtml.push('<table class="table table-sm table-bordered" style="font-size:12px;">');
                detailHtml.push('<thead><tr><th style="width:50px;">#</th><th>Transform</th></tr></thead>');
                detailHtml.push('<tbody>');
                
                route.transforms.forEach((transform, index) => {
                    detailHtml.push(`<tr><td>${index + 1}</td><td>${this.renderJsonBlock(transform, `Transform ${index + 1}`)}</td></tr>`);
                });
                
                detailHtml.push('</tbody></table></div>');
            }

            // Authorization
            if (route.authorizationPolicy) {
                detailHtml.push(`<div style="margin-top:8px;"><strong>Authorization:</strong> <code>${window.DashboardUtils.escapeHtml(route.authorizationPolicy)}</code></div>`);
            }

            // CORS
            if (route.corsPolicy) {
                detailHtml.push(`<div style="margin-top:4px;"><strong>CORS Policy:</strong> <code>${window.DashboardUtils.escapeHtml(route.corsPolicy)}</code></div>`);
            }

            // Timeout
            if (route.timeout || route.timeoutPolicy) {
                detailHtml.push(`<div style="margin-top:4px;"><strong>Timeout:</strong>`);
                if (route.timeout) detailHtml.push(` ${route.timeout}`);
                if (route.timeoutPolicy) detailHtml.push(` (${route.timeoutPolicy})`);
                detailHtml.push(`</div>`);
            }

            // Metadata
            if (route.metadata && Object.keys(route.metadata).length > 0) {
                detailHtml.push(`<div style="margin-top:8px;"><strong>${__('index.route.metadata')}</strong></div>`);
                detailHtml.push(this.renderJsonBlock(route.metadata, 'Metadata'));
            }

            detailDiv.innerHTML = detailHtml.join('');
            td.appendChild(detailDiv);
            tr.appendChild(td);

            return tr;
        },

        // ===== Render JSON Block =====
        renderJsonBlock: function(obj, title) {
            const json = JSON.stringify(obj, null, 2);
            return `<details style="margin:4px 0 0;">
                <summary style="cursor:pointer;color:#0ea5e9;font-weight:500;">${title}</summary>
                <pre style="background:#f1f5f9;border:1px solid #cbd5e1;border-radius:4px;padding:8px;margin:4px 0 0;overflow-x:auto;white-space:pre-wrap;word-break:break-all;font-size:12px;color:#334155;line-height:1.6;">${window.DashboardUtils.escapeHtml(json)}</pre>
            </details>`;
        },

        // ===== Toggle Route =====
        toggleRoute: function(routeId) {
            const state = window.DashboardState;
            const current = state.get(`ui.expandedRoutes.${routeId}`) || false;
            state.set(`ui.expandedRoutes.${routeId}`, !current);
            this.renderRoutes();
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
                window.DashboardModals.showWarning(__('index.route.noClusters') || '请先添加集群');
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
                title: __('modal.addRoute') || '添加路由 (JSON模式)',
                data: defaultRoute,
                schemaType: 'route',
                size: 'xl',
                onSave: function(parsedData) {
                    // Validate route config
                    if (!parsedData.ClusterId || !parsedData.ClusterId.trim()) {
                        window.DashboardModals.showError(__('index.route.invalidCluster') || 'ClusterId 必须指定有效的集群');
                        return false;
                    }
                    if (!parsedData.Match || (!parsedData.Match.Path && !parsedData.Match.Hosts)) {
                        window.DashboardModals.showError(__('index.route.invalidMatch') || 'Match 配置无效，必须指定 Path 或 Hosts');
                        return false;
                    }
                    // Check cluster exists
                    if (clusterIds.indexOf(parsedData.ClusterId) === -1) {
                        window.DashboardModals.showWarning(__('index.route.clusterNotFound') || '指定的集群不存在: ' + parsedData.ClusterId);
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
        
                window.DashboardModals.showInfo(__('index.route.saving') || '正在保存路由...');
        
                // Convert to API format (YARP format is already correct)
                const response = await window.DashboardApi.endpoints.saveRoute(routeId, routeConfig);
        
                window.DashboardModals.showSuccess(__('index.route.saved') || '路由保存成功');
                await this.loadRoutes();
        
                document.dispatchEvent(new CustomEvent('dashboard:configChanged', {
                    detail: { type: 'route', id: routeId, action: 'save' }
                }));
            } catch (error) {
                console.error('[Routes] Save failed:', error);
                window.DashboardModals.showError(__('index.route.saveFailed') || '路由保存失败: ' + error.message);
            }
        },
        
        // ===== Prompt Route ID =====
        promptRouteId: function() {
            return new Promise(function(resolve) {
                const modalId = 'route-id-prompt-' + Date.now();
                const modalHtml = `
                    <div class="modal fade" id="${modalId}" tabindex="-1">
                        <div class="modal-dialog modal-dialog-centered">
                            <div class="modal-content">
                                <div class="modal-header">
                                    <h5 class="modal-title"><i class="bi bi-tag me-2"></i>${__('modal.routeId') || '输入路由ID'}</h5>
                                    <button type="button" class="btn-close" data-bs-dismiss="modal"></button>
                                </div>
                                <div class="modal-body">
                                    <input type="text" class="form-control" id="${modalId}-input" 
                                           placeholder="${__('modal.routeIdPlaceholder') || '例如: my-service-route'}"
                                           required>
                                    <small class="text-muted mt-2">${__('modal.routeIdHelp') || '路由ID用于标识此路由规则，建议使用服务名+Route作为ID'}</small>
                                </div>
                                <div class="modal-footer">
                                    <button type="button" class="btn btn-secondary" data-bs-dismiss="modal">${__('modal.cancelBtn') || '取消'}</button>
                                    <button type="button" class="btn btn-primary" id="${modalId}-confirm">${__('modal.confirmBtn') || '确认'}</button>
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
                window.DashboardModals.showError(__('index.route.notFound') || '路由不存在');
                return;
            }
        
            // Check if editable
            if (route.source === 'config') {
                window.DashboardModals.showWarning(__('index.route.notEditable') || '静态配置的路由无法通过Dashboard编辑');
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
                title: __('modal.editRoute') || '编辑路由 (JSON模式) - ' + routeId,
                data: yarpRoute,
                schemaType: 'route',
                size: 'xl',
                onSave: function(parsedData) {
                    // Validate route config
                    if (!parsedData.ClusterId || !parsedData.ClusterId.trim()) {
                        window.DashboardModals.showError(__('index.route.invalidCluster') || 'ClusterId 必须指定有效的集群');
                        return false;
                    }
                    if (!parsedData.Match || (!parsedData.Match.Path && !parsedData.Match.Hosts)) {
                        window.DashboardModals.showError(__('index.route.invalidMatch') || 'Match 配置无效，必须指定 Path 或 Hosts');
                        return false;
                    }
        
                    // Save route directly with existing ID
                    self.saveRouteFromJson(parsedData, routeId);
                    return true;
                }
            });
        },

        // ===== Delete Route =====
        deleteRoute: async function(routeId) {
            const self = this;
            
            window.DashboardModals.showConfirm(
                __('index.route.deleteConfirm').replace('{id}', routeId) || `确认删除路由 '${routeId}'？此操作不可撤销。`,
                async function() {
                    try {
                        window.DashboardModals.showInfo(__('index.route.deleting') || '正在删除路由...');
                        
                        await window.DashboardApi.endpoints.deleteRoute(routeId);
                        await self.loadRoutes();
                        
                        window.DashboardModals.showSuccess(__('index.route.deleted') || '路由删除成功');

                        // Trigger config deleted event
                        document.dispatchEvent(new CustomEvent('dashboard:configChanged', {
                            detail: { type: 'route', id: routeId, action: 'delete' }
                        }));
                    } catch (error) {
                        console.error('[Routes] Delete failed:', error);
                        window.DashboardModals.showError(__('index.route.deleteFailed') || '路由删除失败: ' + error.message);
                    }
                },
                null,
                { title: __('modal.deleteRoute') || '删除路由', danger: true }
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
