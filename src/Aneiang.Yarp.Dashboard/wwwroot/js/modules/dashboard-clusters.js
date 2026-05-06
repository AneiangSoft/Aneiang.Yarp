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
            const allClusters = state.get('data.clusters') || [];
            const clusters = state.getFilteredClusters();
            
            console.log('[Clusters] Rendering:', {
                total: allClusters.length,
                filtered: clusters.length,
                filters: state.get('filters.clusters')
            });
            
            // Render filter toolbar (only once, then update counts)
            this.renderFilterToolbar();
            
            const tbody = window.DashboardDOM.safe('#cluster-tbody');
            if (!tbody) {
                console.error('[Clusters] tbody not found, cannot render');
                return;
            }

            // Always clear tbody content, not the parent table
            window.DashboardDOM.clear(tbody);
            
            if (clusters.length === 0) {
                // Show empty state INSIDE tbody, not replacing it
                const emptyRow = document.createElement('tr');
                emptyRow.innerHTML = `
                    <td colspan="5" class="text-center py-5 text-muted">
                        <i class="bi bi-hdd-rack" style="font-size: 2rem; opacity: 0.5;"></i>
                        <div class="mt-2">${__('index.cluster.empty') || '暂无集群数据'}</div>
                    </td>
                `; 
                tbody.appendChild(emptyRow);
            } else {
                this.renderClusterRows(clusters, tbody);
            }

            // Update refresh time
            this.updateRefreshTime();
        },

        // ===== Render Filter Toolbar =====
        renderFilterToolbar: function() {
            const container = window.DashboardDOM.safe('#cluster-filter-container');
            if (!container) return;
            
            // Check if toolbar already exists - only update counts, don't recreate
            const existingToolbar = document.getElementById('cluster-search-input');
            if (existingToolbar) {
                this._updateFilterCounts();
                return;
            }
            
            // First time - create toolbar
            const state = window.DashboardState;
            const allClusters = state.get('data.clusters') || [];
            const healthCounts = this._getHealthCounts(allClusters);
            
            container.innerHTML = `
                <div class="card-body py-2 border-bottom">
                    <div class="row g-2 align-items-center">
                        <div class="col">
                            <div class="input-group input-group-sm">
                                <span class="input-group-text"><i class="bi bi-search"></i></span>
                                <input type="text" class="form-control" id="cluster-search-input" 
                                       placeholder="${__('index.cluster.search') || '搜索集群ID、地址...'}..." 
                                       autocomplete="off">
                                <button class="btn btn-primary" type="button" id="cluster-search-btn">
                                    <i class="bi bi-arrow-right"></i>
                                </button>
                            </div>
                        </div>
                        <div class="col-auto">
                            <span class="text-muted small" id="cluster-search-result"></span>
                        </div>
                        <div class="col-auto">
                            <select class="form-select form-select-sm" id="cluster-health-select" style="width:auto;">
                                <option value="all">${__('index.cluster.health.all') || '全部'} (${healthCounts.all})</option>
                                <option value="Healthy">${__('index.cluster.health.healthy') || '健康'} (${healthCounts.Healthy})</option>
                                <option value="Unknown">${__('index.cluster.health.unknown') || '未知'} (${healthCounts.Unknown})</option>
                                <option value="Unhealthy">${__('index.cluster.health.unhealthy') || '不健康'} (${healthCounts.Unhealthy})</option>
                            </select>
                        </div>
                        <div class="col-auto">
                            <button class="btn btn-sm btn-outline-secondary" type="button" id="cluster-search-clear" title="${__('index.search.clear') || '清除搜索'}" style="display:none;">\n                                <i class="bi bi-x-circle me-1"></i>${__('index.search.clear') || '清除'}\n                            </button>
                        </div>
                        <div class="col-auto">
                            <button class="btn btn-sm btn-success" id="cluster-add-btn">
                                <i class="bi bi-plus-circle me-1"></i>${__('index.cluster.add') || '新增'}
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
            const allClusters = state.get('data.clusters') || [];
            const filteredClusters = state.getFilteredClusters();
            const searchValue = state.get('filters.clusters.search') || ''; 
            const healthValue = state.get('filters.clusters.health') || 'all';
            
            // Update result count
            const resultEl = document.getElementById('cluster-search-result');
            if (resultEl) {
                if (searchValue || healthValue !== 'all') {
                    resultEl.textContent = `${filteredClusters.length}/${allClusters.length}`;
                    resultEl.style.display = '';
                } else {
                    resultEl.textContent = `${allClusters.length} ${__('index.cluster.total') || '个集群'}`;
                }
            }
            
            // Update clear button visibility
            const clearBtn = document.getElementById('cluster-search-clear');
            if (clearBtn) {
                clearBtn.style.display = searchValue ? '' : 'none';
            }
            
            // Update health select value (don't recreate)
            const healthSelect = document.getElementById('cluster-health-select');
            if (healthSelect && healthSelect.value !== healthValue) {
                healthSelect.value = healthValue;
            }
        },
        
        // ===== Get Health Counts =====
        _getHealthCounts: function(clusters) {
            return {
                all: clusters.length,
                Healthy: clusters.filter(c => c.healthyCount > 0).length,
                Unknown: clusters.filter(c => c.unknownCount > 0).length,
                Unhealthy: clusters.filter(c => c.unhealthyCount > 0).length
            }; 
        },
        
        // ===== Bind Filter Events =====
        _bindFilterEvents: function() {
            const self = this;
            
            // Search button click
            const searchBtn = document.getElementById('cluster-search-btn');
            if (searchBtn) {
                searchBtn.addEventListener('click', function() {
                    self._doSearch();
                });
            }
            
            // Search input - Enter key triggers search
            const searchInput = document.getElementById('cluster-search-input');
            if (searchInput) {
                searchInput.addEventListener('keypress', function(e) {
                    if (e.key === 'Enter') {
                        e.preventDefault();
                        self._doSearch();
                    }
                });
            }
            
            // Clear button
            const clearBtn = document.getElementById('cluster-search-clear');
            if (clearBtn) {
                clearBtn.addEventListener('click', function() {
                    const input = document.getElementById('cluster-search-input');
                    if (input) input.value = ''; 
                    window.DashboardState.set('filters.clusters.search', '');
                    window.DashboardState.set('filters.clusters.health', 'all');
                    self.renderClusters();
                });
            }
            
            // Health select - immediate filter
            const healthSelect = document.getElementById('cluster-health-select');
            if (healthSelect) {
                healthSelect.addEventListener('change', function(e) {
                    window.DashboardState.set('filters.clusters.health', e.target.value);
                    self.renderClusters();
                });
            }
            
            // Add button
            const addBtn = document.getElementById('cluster-add-btn');
            if (addBtn) {
                addBtn.addEventListener('click', function() {
                    self.showAddModal();
                });
            }
        },
        
        // ===== Do Search =====
        _doSearch: function() {
            const searchInput = document.getElementById('cluster-search-input');
            if (searchInput) {
                window.DashboardState.set('filters.clusters.search', searchInput.value);
                this.renderClusters();
            }
        },
        
        // ===== Legacy methods (removed) =====
        initFilterHandlers: function() { /* deprecated */ },
        restoreFilterValues: function() { /* deprecated */ },

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

        // ===== Show Add Modal (JSON Mode) =====
        showAddModal: function() {
            const self = this;
                    
            // Default cluster template for new cluster
            const defaultCluster = {
                "Destinations": {
                    "destination1": {
                        "Address": "http://localhost:5000"
                    }
                },
                "LoadBalancingPolicy": "RoundRobin"
            }; 
        
            window.DashboardModals.showJsonModal({
                title: __('modal.addCluster') || '添加集群 (JSON模式)',
                data: defaultCluster,
                schemaType: 'cluster',
                size: 'xl',
                onSave: function(parsedData) {
                    // Validate cluster config
                    if (!parsedData.Destinations || typeof parsedData.Destinations !== 'object') {
                        window.DashboardModals.showError(__('index.cluster.invalidDestinations') || 'Destinations 配置无效');
                        return false;
                    }
        
                    // Check for valid addresses
                    let hasValidAddress = false;
                    for (const destName in parsedData.Destinations) {
                        const dest = parsedData.Destinations[destName];
                        if (dest && dest.Address && (dest.Address.startsWith('http://') || dest.Address.startsWith('https://'))) {
                            hasValidAddress = true;
                            break;
                        }
                    }
                    if (!hasValidAddress) {
                        window.DashboardModals.showError(__('index.cluster.invalidAddress') || '目标地址必须以 http:// 或 https:// 开头');
                        return false;
                    }
        
                    // Save cluster
                    self.saveClusterFromJson(parsedData);
                    return true;
                }
            });
        },
        
        // ===== Save Cluster from JSON =====
        saveClusterFromJson: async function(clusterConfig, clusterId) {
            try {
                // Generate clusterId from user input or from existing
                if (!clusterId) {
                    // Ask for cluster ID via simple prompt
                    const id = await this.promptClusterId();
                    if (!id) return;
                    clusterId = id;
                }
        
                window.DashboardModals.showInfo(__('index.cluster.saving') || '正在保存集群...');
        
                // Convert to API format
                const apiConfig = {
                    clusterId: clusterId,
                    destinations: clusterConfig.Destinations,
                    loadBalancingPolicy: clusterConfig.LoadBalancingPolicy || undefined,
                    healthCheck: clusterConfig.HealthCheck || undefined,
                    httpClient: clusterConfig.HttpClient || undefined,
                    httpRequest: clusterConfig.HttpRequest || undefined,
                    sessionAffinity: clusterConfig.SessionAffinity || undefined,
                    metadata: clusterConfig.Metadata || undefined
                }; 
        
                const response = await window.DashboardApi.endpoints.saveCluster(clusterId, apiConfig);
        
                window.DashboardModals.showSuccess(__('index.cluster.saved') || '集群保存成功');
                await this.loadClusters();
        
                document.dispatchEvent(new CustomEvent('dashboard:configChanged', {
                    detail: { type: 'cluster', id: clusterId, action: 'save' }
                }));
            } catch (error) {
                console.error('[Clusters] Save failed:', error);
                window.DashboardModals.showError(__('index.cluster.saveFailed') || '集群保存失败: ' + error.message);
            }
        },
        
        // ===== Prompt Cluster ID =====
        promptClusterId: function() {
            return new Promise(function(resolve) {
                // Use a simple Bootstrap modal for ID input
                const modalId = 'cluster-id-prompt-' + Date.now();
                const modalHtml = `
                    <div class="modal fade" id="${modalId}" tabindex="-1">
                        <div class="modal-dialog modal-dialog-centered">
                            <div class="modal-content">
                                <div class="modal-header">
                                    <h5 class="modal-title"><i class="bi bi-tag me-2"></i>${__('modal.clusterId') || '输入集群ID'}</h5>
                                    <button type="button" class="btn-close" data-bs-dismiss="modal"></button>
                                </div>
                                <div class="modal-body">
                                    <input type="text" class="form-control" id="${modalId}-input" 
                                           placeholder="${__('modal.clusterIdPlaceholder') || '例如: my-service-cluster'}"
                                           required>
                                    <small class="text-muted mt-2">${__('modal.clusterIdHelp') || '集群ID用于标识和引用此集群，建议使用服务名作为ID'}</small>
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

        // ===== Save Cluster =====
        saveCluster: async function(clusterId, config) {
            try {
                window.DashboardModals.showInfo(__('index.cluster.saving') || '正在保存集群...');

                const response = await window.DashboardApi.endpoints.saveCluster(clusterId, config);

                window.DashboardModals.showSuccess(__('index.cluster.saved') || '集群保存成功');

                // Reload clusters
                await this.loadClusters();

                // Trigger config saved event
                document.dispatchEvent(new CustomEvent('dashboard:configChanged', {
                    detail: { type: 'cluster', id: clusterId, action: 'save' }
                }));
            } catch (error) {
                console.error('[Clusters] Save failed:', error);
                window.DashboardModals.showError(__('index.cluster.saveFailed') || '集群保存失败: ' + error.message);
            }
        },

        // ===== Show Edit Modal (JSON Mode) =====
        showEditModal: function(clusterId) {
            const self = this;
            
            // Get cluster data
            const clusters = window.DashboardState.get('data.clusters') || [];
            const cluster = clusters.find(c => c.clusterId === clusterId);
            if (!cluster) {
                window.DashboardModals.showError(__('index.cluster.notFound') || '集群不存在');
                return;
            }

            // Check if editable
            if (cluster.source === 'config') {
                window.DashboardModals.showWarning(__('index.cluster.notEditable') || '静态配置的集群无法通过Dashboard编辑');
                return;
            }

            // Build YARP format cluster config for JSON editor
            const yarpCluster = {
                "Destinations": {},
                "LoadBalancingPolicy": cluster.loadBalancingPolicy || "RoundRobin"
            }; 

            // Convert destinations from API format to YARP format
            if (cluster.destinations && Array.isArray(cluster.destinations)) {
                cluster.destinations.forEach(function(dest) {
                    yarpCluster.Destinations[dest.name || 'destination'] = {
                        "Address": dest.address
                    }; 
                });
            } else if (cluster.destinations && typeof cluster.destinations === 'object') {
                // Already in object format
                for (const destName in cluster.destinations) {
                    const dest = cluster.destinations[destName];
                    yarpCluster.Destinations[destName] = {
                        "Address": typeof dest === 'string' ? dest : (dest.Address || dest.address)
                    }; 
                }
            }

            // Add health check if exists
            if (cluster.healthCheck) {
                yarpCluster.HealthCheck = cluster.healthCheck;
            }

            // Add HTTP client config if exists
            if (cluster.httpClient) {
                yarpCluster.HttpClient = cluster.httpClient;
            }

            window.DashboardModals.showJsonModal({
                title: __('modal.editCluster') || '编辑集群 (JSON模式) - ' + clusterId,
                data: yarpCluster,
                schemaType: 'cluster',
                size: 'xl',
                onSave: function(parsedData) {
                    // Validate cluster config
                    if (!parsedData.Destinations || typeof parsedData.Destinations !== 'object') {
                        window.DashboardModals.showError(__('index.cluster.invalidDestinations') || 'Destinations 配置无效');
                        return false;
                    }

                    // Check for valid addresses
                    let hasValidAddress = false;
                    for (const destName in parsedData.Destinations) {
                        const dest = parsedData.Destinations[destName];
                        if (dest && dest.Address && (dest.Address.startsWith('http://') || dest.Address.startsWith('https://'))) {
                            hasValidAddress = true;
                            break;
                        }
                    }
                    if (!hasValidAddress) {
                        window.DashboardModals.showError(__('index.cluster.invalidAddress') || '目标地址必须以 http:// 或 https:// 开头');
                        return false;
                    }

                    // Save cluster directly with existing ID
                    self.saveClusterFromJson(parsedData, clusterId);
                    return true;
                }
            });
        },

        // ===== Delete Cluster =====
        deleteCluster: async function(clusterId) {
            const self = this;
            
            window.DashboardModals.showConfirm(
                __('index.cluster.deleteConfirm').replace('{id}', clusterId) || `确认删除集群 '${clusterId}'？此操作不可撤销。`,
                async function() {
                    try {
                        window.DashboardModals.showInfo(__('index.cluster.deleting') || '正在删除集群...');
                        
                        await window.DashboardApi.endpoints.deleteCluster(clusterId);
                        await self.loadClusters();
                        
                        window.DashboardModals.showSuccess(__('index.cluster.deleted') || '集群删除成功');

                        // Trigger config deleted event
                        document.dispatchEvent(new CustomEvent('dashboard:configChanged', {
                            detail: { type: 'cluster', id: clusterId, action: 'delete' }
                        }));
                    } catch (error) {
                        console.error('[Clusters] Delete failed:', error);
                        window.DashboardModals.showError(__('index.cluster.deleteFailed') || '集群删除失败: ' + error.message);
                    }
                },
                null,
                { title: __('modal.deleteCluster') || '删除集群', danger: true }
            );
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
    
    // Global functions for onclick handlers
    window.manualRefresh = async function(btn) {
        const originalText = btn.innerHTML;
        btn.innerHTML = '<i class="bi bi-arrow-clockwise spin me-1"></i>' + (window.__('index.btn.loading') || 'Loading...');
        btn.disabled = true;
        try {
            await ClustersModule.loadClusters();
        } finally {
            btn.innerHTML = originalText;
            btn.disabled = false;
        }
    };
    
    window.loadClusters = function() {
        ClustersModule.loadClusters();
    };
    
    window.showAddClusterModal = function() {
        if (ClustersModule.showAddModal) {
            ClustersModule.showAddModal();
        } else {
            console.warn('[Clusters] showAddModal not implemented yet');
        }
    };

})();
