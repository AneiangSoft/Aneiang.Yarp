/**
 * Dashboard Clusters Module - Cluster management and monitoring
 */
(function() {
    'use strict';

    const ClustersModule = {
        name: 'clusters',
        initialized: false,

        // ===== Initialization =====
        init: async function() {
            if (this.initialized) return;
            
            console.log('[Clusters] Initializing...');
            
            try {
                // Load initial data
                await this.loadClusters();
                
                // Setup event listeners
                this.setupEvents();
                
                this.initialized = true;
                console.log('[Clusters] Initialized');
            } catch (error) {
                console.error('[Clusters] Init failed:', error);
                throw error;
            }
        },

        // ===== Load Clusters =====
        loadClusters: async function() {
            try {
                const container = window.DashboardDOM.safe('#cluster-tbody');
                if (!container) return;

                window.DashboardDOM.showLoading(container, __('index.cluster.loading'));

                const clusters = await window.DashboardApi.endpoints.getClusters();
                
                // Update state
                window.DashboardState.set('data.clusters', clusters || []);

                // Render clusters
                this.renderClusters();

            } catch (error) {
                console.error('[Clusters] Load failed:', error);
                const container = window.DashboardDOM.safe('#cluster-tbody');
                if (container) {
                    window.DashboardDOM.showError(container, __('index.cluster.loadFailed'));
                }
            }
        },

        // ===== Render Clusters =====
        renderClusters: function() {
            const state = window.DashboardState;
            const clusters = state.getFilteredClusters();
            
            // Render filter toolbar
            this.renderFilterToolbar();
            
            const tbody = window.DashboardDOM.safe('#cluster-tbody');
            if (!tbody) return;

            if (clusters.length === 0) {
                window.DashboardDOM.showEmpty(
                    tbody.parentElement,
                    __('index.cluster.empty'),
                    'bi bi-hdd-rack'
                );
            } else {
                this.renderClusterRows(clusters, tbody);
            }

            // Update refresh time
            this.updateRefreshTime();
        },

        // ===== Render Filter Toolbar =====
        renderFilterToolbar: function() {
            const container = window.DashboardDOM.safe('#cluster-filter-container');
            if (!container || container.dataset.initialized) return;

            container.innerHTML = `
                <div class="card-body py-2 border-bottom">
                    <div class="row g-2 align-items-center">
                        <div class="col">
                            <input type="text" class="form-control form-control-sm" id="cluster-search-input" 
                                   placeholder="${__('index.cluster.search')}...">
                        </div>
                        <div class="col-auto">
                            <select class="form-select form-select-sm" id="cluster-health-select" style="width:auto;">
                                <option value="all">${__('index.cluster.health.all')}</option>
                                <option value="Healthy">${__('index.cluster.health.healthy')}</option>
                                <option value="Unknown">${__('index.cluster.health.unknown')}</option>
                                <option value="Unhealthy">${__('index.cluster.health.unhealthy')}</option>
                            </select>
                        </div>
                        <div class="col-auto">
                            <select class="form-select form-select-sm" id="cluster-source-select" style="width:auto;">
                                <option value="all">${__('index.cluster.source.all')}</option>
                                <option value="static">${__('index.cluster.source.static')}</option>
                                <option value="dynamic">${__('index.cluster.source.dynamic')}</option>
                            </select>
                        </div>
                        <div class="col-auto">
                            <button class="btn btn-sm btn-success" onclick="ClustersModule.showAddModal()">
                                <i class="bi bi-plus-circle me-1"></i>${__('index.cluster.add')}
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
            const searchInput = window.DashboardDOM.safe('#cluster-search-input');
            if (searchInput) {
                searchInput.addEventListener('input', window.DashboardUtils.debounce((e) => {
                    window.DashboardState.set('filters.clusters.search', e.target.value);
                    this.renderClusters();
                }, 300));
            }

            // Health select
            const healthSelect = window.DashboardDOM.safe('#cluster-health-select');
            if (healthSelect) {
                healthSelect.addEventListener('change', (e) => {
                    window.DashboardState.set('filters.clusters.health', e.target.value);
                    this.renderClusters();
                });
            }

            // Source select
            const sourceSelect = window.DashboardDOM.safe('#cluster-source-select');
            if (sourceSelect) {
                sourceSelect.addEventListener('change', (e) => {
                    window.DashboardState.set('filters.clusters.source', e.target.value);
                    this.renderClusters();
                });
            }

            // Restore filter values from state
            this.restoreFilterValues();
        },

        // ===== Restore Filter Values =====
        restoreFilterValues: function() {
            const state = window.DashboardState;
            
            const searchInput = window.DashboardDOM.safe('#cluster-search-input');
            if (searchInput) {
                searchInput.value = state.get('filters.clusters.search') || '';
            }

            const healthSelect = window.DashboardDOM.safe('#cluster-health-select');
            if (healthSelect) {
                healthSelect.value = state.get('filters.clusters.health') || 'all';
            }

            const sourceSelect = window.DashboardDOM.safe('#cluster-source-select');
            if (sourceSelect) {
                sourceSelect.value = state.get('filters.clusters.source') || 'all';
            }
        },

        // ===== Render Cluster Rows =====
        renderClusterRows: function(clusters, tbody) {
            window.DashboardDOM.clear(tbody);

            const fragment = document.createDocumentFragment();

            clusters.forEach(cluster => {
                const isExpanded = window.DashboardState.get(`ui.expandedClusters.${cluster.clusterId}`) || false;
                const rows = this.createClusterRows(cluster, isExpanded);
                rows.forEach(row => fragment.appendChild(row));
            });

            tbody.appendChild(fragment);
        },

        // ===== Create Cluster Rows =====
        createClusterRows: function(cluster, isExpanded) {
            const rows = [];
            const destinations = cluster.destinations || [];
            const rowspan = destinations.length || 1;

            destinations.forEach((dest, index) => {
                const tr = window.DashboardDOM.create('tr', {
                    className: 'cluster-row',
                    style: { cursor: 'pointer' }
                });

                // First column (cluster info) - only on first row
                if (index === 0) {
                    const tdCluster = window.DashboardDOM.create('td', {
                        attributes: { rowspan: rowspan },
                        style: {
                            fontWeight: '600',
                            verticalAlign: 'middle'
                        }
                    });

                    // Expand icon
                    const expandIcon = window.DashboardDOM.create('span', {
                        className: 'cluster-expand-icon',
                        textContent: isExpanded ? '▼' : '▶',
                        style: {
                            display: 'inline-block',
                            width: '16px',
                            marginRight: '4px'
                        }
                    });

                    tdCluster.appendChild(expandIcon);
                    tdCluster.appendChild(document.createTextNode(cluster.clusterId));

                    // Click to expand
                    tr.addEventListener('click', () => this.toggleCluster(cluster.clusterId));

                    tr.appendChild(tdCluster);
                }

                // Destination name
                const tdName = window.DashboardDOM.create('td', {});
                const code = window.DashboardDOM.create('code', {
                    textContent: dest.name || '-'
                });
                tdName.appendChild(code);
                tr.appendChild(tdName);

                // Destination address
                const tdAddress = window.DashboardDOM.create('td', {});
                const link = window.DashboardDOM.create('a', {
                    textContent: dest.address || '-',
                    attributes: {
                        href: dest.address || '#',
                        target: '_blank'
                    },
                    style: { textDecoration: 'none' }
                });
                tdAddress.appendChild(link);
                tr.appendChild(tdAddress);

                // Active health
                const tdActive = window.DashboardDOM.create('td', {});
                tdActive.appendChild(this.createHealthBadge(dest.activeHealth || 'Unknown'));
                tr.appendChild(tdActive);

                // Passive health
                const tdPassive = window.DashboardDOM.create('td', {});
                tdPassive.appendChild(this.createHealthBadge(dest.passiveHealth || 'Unknown'));
                tr.appendChild(tdPassive);

                // Load balancing policy (only on first row)
                if (index === 0) {
                    const tdPolicy = window.DashboardDOM.create('td', {
                        attributes: { rowspan: rowspan },
                        style: { verticalAlign: 'middle' }
                    });
                    tdPolicy.appendChild(this.createPolicyBadge(cluster.loadBalancingPolicy));
                    tr.appendChild(tdPolicy);

                    // Actions (only on first row)
                    const tdActions = window.DashboardDOM.create('td', {
                        attributes: { rowspan: rowspan },
                        style: { verticalAlign: 'middle' }
                    });
                    tdActions.appendChild(this.createActionButtons(cluster));
                    tr.appendChild(tdActions);
                }

                rows.push(tr);
            });

            // Expanded detail row
            if (isExpanded && destinations.length > 0) {
                const detailTr = this.createClusterDetailRow(cluster);
                rows.push(detailTr);
            }

            return rows;
        },

        // ===== Create Health Badge =====
        createHealthBadge: function(health) {
            const healthMap = {
                'Healthy': { class: 'text-success', icon: '✓' },
                'Unhealthy': { class: 'text-danger', icon: '✗' },
                'Unknown': { class: 'text-warning', icon: '?' }
            };

            const config = healthMap[health] || healthMap['Unknown'];
            
            return window.DashboardDOM.create('span', {
                className: config.class,
                textContent: `${config.icon} ${health}`
            });
        },

        // ===== Create Policy Badge =====
        createPolicyBadge: function(policy) {
            return window.DashboardDOM.create('span', {
                className: 'badge bg-light text-dark',
                textContent: policy || 'RoundRobin'
            });
        },

        // ===== Create Action Buttons =====
        createActionButtons: function(cluster) {
            const container = window.DashboardDOM.create('div', {
                className: 'd-flex gap-1'
            });

            // Edit button
            const editBtn = window.DashboardDOM.create('button', {
                className: 'btn btn-sm btn-outline-primary',
                textContent: __('index.cluster.edit'),
                events: {
                    click: (e) => {
                        e.stopPropagation();
                        this.showEditModal(cluster.clusterId);
                    }
                }
            });

            // Delete button (only if editable)
            if (cluster.isEditable) {
                const deleteBtn = window.DashboardDOM.create('button', {
                    className: 'btn btn-sm btn-outline-danger',
                    textContent: __('index.cluster.delete'),
                    events: {
                        click: (e) => {
                            e.stopPropagation();
                            this.deleteCluster(cluster.clusterId);
                        }
                    }
                });
                container.appendChild(deleteBtn);
            }

            container.appendChild(editBtn);

            return container;
        },

        // ===== Create Cluster Detail Row =====
        createClusterDetailRow: function(cluster) {
            const tr = window.DashboardDOM.create('tr', {
                className: 'cluster-detail-row'
            });

            const td = window.DashboardDOM.create('td', {
                attributes: { colspan: '7' },
                style: {
                    padding: '16px',
                    background: '#f8fafc'
                }
            });

            const detailHtml = [];

            // Destinations detail
            detailHtml.push(`<h6 class="mb-2">${__('index.cluster.destinations')}</h6>`);
            detailHtml.push('<div class="table-responsive">');
            detailHtml.push('<table class="table table-sm table-bordered">');
            detailHtml.push('<thead><tr><th>Name</th><th>Address</th><th>Active Health</th><th>Passive Health</th></tr></thead>');
            detailHtml.push('<tbody>');

            (cluster.destinations || []).forEach(dest => {
                detailHtml.push('<tr>');
                detailHtml.push(`<td><code>${dest.name || '-'}</code></td>`);
                detailHtml.push(`<td>${dest.address || '-'}</td>`);
                detailHtml.push(`<td>${dest.activeHealth || 'Unknown'}</td>`);
                detailHtml.push(`<td>${dest.passiveHealth || 'Unknown'}</td>`);
                detailHtml.push('</tr>');
            });

            detailHtml.push('</tbody></table></div>');

            // Health check config
            if (cluster.healthCheck) {
                detailHtml.push(`<h6 class="mt-3 mb-2">${__('index.cluster.healthCheck')}</h6>`);
                detailHtml.push(this.renderJsonBlock(cluster.healthCheck, 'Health Check Config'));
            }

            // Session affinity
            if (cluster.sessionAffinity) {
                detailHtml.push(`<h6 class="mt-3 mb-2">${__('index.cluster.sessionAffinity')}</h6>`);
                detailHtml.push(this.renderJsonBlock(cluster.sessionAffinity, 'Session Affinity Config'));
            }

            // HTTP client
            if (cluster.httpClient) {
                detailHtml.push(`<h6 class="mt-3 mb-2">${__('index.cluster.httpClient')}</h6>`);
                detailHtml.push(this.renderJsonBlock(cluster.httpClient, 'HTTP Client Config'));
            }

            td.innerHTML = detailHtml.join('');
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

        // ===== Toggle Cluster =====
        toggleCluster: function(clusterId) {
            const state = window.DashboardState;
            const current = state.get(`ui.expandedClusters.${clusterId}`) || false;
            state.set(`ui.expandedClusters.${clusterId}`, !current);
            this.renderClusters();
        },

        // ===== Update Refresh Time =====
        updateRefreshTime: function() {
            const timeEl = window.DashboardDOM.safe('#cluster-refresh-time');
            if (timeEl) {
                timeEl.textContent = __('index.cluster.updated') + window.DashboardI18n.formatTime(new Date());
            }
        },

        // ===== Show Add Modal =====
        showAddModal: function() {
            // TODO: Implement add cluster modal
            alert('Add cluster modal - to be implemented');
        },

        // ===== Show Edit Modal =====
        showEditModal: function(clusterId) {
            // TODO: Implement edit cluster modal
            alert(`Edit cluster modal for ${clusterId} - to be implemented`);
        },

        // ===== Delete Cluster =====
        deleteCluster: async function(clusterId) {
            if (!confirm(__('index.cluster.deleteConfirm').replace('{id}', clusterId))) return;

            try {
                await window.DashboardApi.endpoints.deleteCluster(clusterId);
                await this.loadClusters();
                
                if (window.DashboardModals) {
                    window.DashboardModals.showSuccess(__('index.cluster.deleted'));
                }
            } catch (error) {
                console.error('[Clusters] Delete failed:', error);
                if (window.DashboardModals) {
                    window.DashboardModals.showError(__('index.cluster.deleteFailed'));
                }
            }
        },

        // ===== Setup Events =====
        setupEvents: function() {
            // Refresh shortcut
            document.addEventListener('dashboard:shortcut:refresh', async () => {
                await this.loadClusters();
            });

            // Locale change
            document.addEventListener('dashboard:localeChange', () => {
                this.renderClusters();
            });
        }
    };

    // Register module
    if (window.DashboardApp) {
        window.DashboardApp.registerModule('clusters', ClustersModule);
    }

    // Expose to window
    window.ClustersModule = ClustersModule;

})();
