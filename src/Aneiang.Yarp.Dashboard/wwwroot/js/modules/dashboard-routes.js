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
            
            // Render filter toolbar
            this.renderFilterToolbar();
            
            const tbody = window.DashboardDOM.safe('#route-tbody');
            if (!tbody) return;

            if (routes.length === 0) {
                window.DashboardDOM.showEmpty(
                    tbody.parentElement,
                    __('index.route.empty'),
                    'bi bi-signpost-split'
                );
            } else {
                this.renderRouteRows(routes, tbody);
            }

            // Update refresh time
            this.updateRefreshTime();
        },

        // ===== Render Filter Toolbar =====
        renderFilterToolbar: function() {
            const container = window.DashboardDOM.safe('#route-filter-container');
            if (!container || container.dataset.initialized) return;

            container.innerHTML = `
                <div class="card-body py-2 border-bottom">
                    <div class="row g-2 align-items-center">
                        <div class="col">
                            <input type="text" class="form-control form-control-sm" id="route-search-input" 
                                   placeholder="${__('index.route.search')}...">
                        </div>
                        <div class="col-auto">
                            <select class="form-select form-select-sm" id="route-source-select" style="width:auto;">
                                <option value="all">${__('index.route.source.all')}</option>
                                <option value="static">${__('index.route.source.static')}</option>
                                <option value="dynamic">${__('index.route.source.dynamic')}</option>
                            </select>
                        </div>
                        <div class="col-auto">
                            <select class="form-select form-select-sm" id="route-method-select" style="width:auto;">
                                <option value="all">${__('index.route.method.all')}</option>
                                <option value="GET">GET</option>
                                <option value="POST">POST</option>
                                <option value="PUT">PUT</option>
                                <option value="DELETE">DELETE</option>
                            </select>
                        </div>
                        <div class="col-auto">
                            <button class="btn btn-sm btn-success" onclick="RoutesModule.showAddModal()">
                                <i class="bi bi-plus-circle me-1"></i>${__('index.route.add')}
                            </button>
                        </div>
                    </div>
                </div>
            `;

            container.dataset.initialized = 'true';
            this.initFilterHandlers();
        },

        // ===== Initialize Filter Handlers =====
        initFilterHandlers: function() {
            // Search input (debounced)
            const searchInput = window.DashboardDOM.safe('#route-search-input');
            if (searchInput) {
                searchInput.addEventListener('input', window.DashboardUtils.debounce((e) => {
                    window.DashboardState.set('filters.routes.search', e.target.value);
                    this.renderRoutes();
                }, 300));
            }

            // Source select
            const sourceSelect = window.DashboardDOM.safe('#route-source-select');
            if (sourceSelect) {
                sourceSelect.addEventListener('change', (e) => {
                    window.DashboardState.set('filters.routes.source', e.target.value);
                    this.renderRoutes();
                });
            }

            // Method select
            const methodSelect = window.DashboardDOM.safe('#route-method-select');
            if (methodSelect) {
                methodSelect.addEventListener('change', (e) => {
                    window.DashboardState.set('filters.routes.method', e.target.value);
                    this.renderRoutes();
                });
            }

            // Restore filter values from state
            this.restoreFilterValues();
        },

        // ===== Restore Filter Values =====
        restoreFilterValues: function() {
            const state = window.DashboardState;
            
            const searchInput = window.DashboardDOM.safe('#route-search-input');
            if (searchInput) {
                searchInput.value = state.get('filters.routes.search') || '';
            }

            const sourceSelect = window.DashboardDOM.safe('#route-source-select');
            if (sourceSelect) {
                sourceSelect.value = state.get('filters.routes.source') || 'all';
            }

            const methodSelect = window.DashboardDOM.safe('#route-method-select');
            if (methodSelect) {
                methodSelect.value = state.get('filters.routes.method') || 'all';
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

        // ===== Show Add Modal =====
        showAddModal: function() {
            // TODO: Implement add route modal
            alert('Add route modal - to be implemented');
        },

        // ===== Show Edit Modal =====
        showEditModal: function(routeId) {
            // TODO: Implement edit route modal
            alert(`Edit route modal for ${routeId} - to be implemented`);
        },

        // ===== Delete Route =====
        deleteRoute: async function(routeId) {
            if (!confirm(__('index.route.deleteConfirm').replace('{id}', routeId))) return;

            try {
                await window.DashboardApi.endpoints.deleteRoute(routeId);
                await this.loadRoutes();
                
                if (window.DashboardModals) {
                    window.DashboardModals.showSuccess(__('index.route.deleted'));
                }
            } catch (error) {
                console.error('[Routes] Delete failed:', error);
                if (window.DashboardModals) {
                    window.DashboardModals.showError(__('index.route.deleteFailed'));
                }
            }
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

})();
