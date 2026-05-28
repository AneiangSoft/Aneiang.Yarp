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
            
            // Render cards view
            this.renderClusterCards(clusters);

            // Render table view
            const tbody = window.DashboardDOM.safe('#cluster-tbody');
            if (tbody) {
                window.DashboardDOM.clear(tbody);
                
                if (clusters.length === 0) {
                    const emptyRow = document.createElement('tr');
                    emptyRow.innerHTML = `
                        <td colspan="7" class="text-center py-5">
                            <div class="empty-state">
                                <i class="bi bi-hdd-rack" style="font-size: 2.5rem; opacity: 0.4; color: #64748b;"></i>
                                <div class="mt-3 text-muted" style="font-size: 14px;">${__('index.cluster.empty')}</div>
                                <div class="mt-2 text-muted small">${__('index.cluster.emptyHelp')}</div>
                            </div>
                        </td>
                    `;
                    tbody.appendChild(emptyRow);
                } else {
                    this.renderClusterRows(clusters, tbody);
                }
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
            const sourceCounts = this._getSourceCounts(allClusters);
            
            container.innerHTML = `
                <div class="card-body py-2 border-bottom">
                    <div class="row g-2 align-items-center">
                        <div class="col">
                            <div class="input-group input-group-sm">
                                <span class="input-group-text bg-light border-end-0">
                                    <i class="bi bi-search text-muted"></i>
                                </span>
                                <input type="text" class="form-control border-start-0" id="cluster-search-input" 
                                       placeholder="${__('index.cluster.search')}" 
                                       autocomplete="off">
                            </div>
                        </div>
                        <div class="col-auto">
                            <select class="form-select form-select-sm" id="cluster-health-select" style="width:100px;">
                                <option value="all">${__('index.cluster.health.all')} (${healthCounts.all})</option>
                                <option value="Healthy">${__('index.cluster.health.healthy')} (${healthCounts.Healthy})</option>
                                <option value="Unknown">${__('index.cluster.health.unknown')} (${healthCounts.Unknown})</option>
                                <option value="Unhealthy">${__('index.cluster.health.unhealthy')} (${healthCounts.Unhealthy})</option>
                            </select>
                        </div>
                        <div class="col-auto">
                            <select class="form-select form-select-sm" id="cluster-source-select" style="width:110px;">
                                <option value="all">${__('index.source.all')} (${sourceCounts.all})</option>
                                <option value="config">${__('index.source.config')} (${sourceCounts.config})</option>
                                <option value="dynamic">${__('index.source.dynamic')} (${sourceCounts.dynamic})</option>
                                <option value="dashboard">${__('index.source.dashboard')} (${sourceCounts.dashboard})</option>
                                <option value="auto-register">${__('index.source.autoRegister')} (${sourceCounts['auto-register']})</option>
                            </select>
                        </div>
                        <div class="col-auto">
                            <span class="text-muted small" id="cluster-search-result"></span>
                        </div>
                        <div class="col-auto">
                            <div class="btn-group">
                                <button class="btn btn-sm btn-outline-secondary" id="cluster-refresh-btn" title="${__('index.btn.refresh')}">
                                    <i class="bi bi-arrow-clockwise"></i>
                                </button>
                                <button class="btn btn-sm btn-outline-danger" id="cluster-clear-btn" title="${__('index.search.clear')}" style="display:none;">
                                    <i class="bi bi-x-circle"></i>
                                </button>
                                <button class="btn btn-sm btn-success" id="cluster-add-btn" title="${__('index.cluster.add')}">
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
            const allClusters = state.get('data.clusters') || [];
            const filteredClusters = state.getFilteredClusters();
            const searchValue = state.get('filters.clusters.search') || ''; 
            const healthValue = state.get('filters.clusters.health') || 'all';
            
            // Update display count in header
            const displayCountEl = document.getElementById('cluster-display-count');
            const totalCountEl = document.getElementById('cluster-total-count');
            if (displayCountEl) displayCountEl.textContent = filteredClusters.length;
            if (totalCountEl) totalCountEl.textContent = allClusters.length;
            
            // Update result count
            const resultEl = document.getElementById('cluster-search-result');
            if (resultEl) {
                if (searchValue || healthValue !== 'all') {
                    resultEl.textContent = `${filteredClusters.length}/${allClusters.length}`;
                    resultEl.style.display = '';
                } else {
                    resultEl.textContent = '';
                }
            }
            
            // Update clear button visibility
            const clearBtn = document.getElementById('cluster-clear-btn');
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
        
        // ===== Get Source Counts =====
        _getSourceCounts: function(clusters) {
            return {
                all: clusters.length,
                'config': clusters.filter(c => (c.source || 'config') === 'config').length,
                'dynamic': clusters.filter(c => c.source === 'dynamic').length,
                'dashboard': clusters.filter(c => c.source === 'dashboard').length,
                'auto-register': clusters.filter(c => c.source === 'auto-register').length
            };
        },
        
        // ===== Bind Filter Events =====
        _bindFilterEvents: function() {
            const self = this;
            
            // Search input - live search on input
            const searchInput = document.getElementById('cluster-search-input');
            if (searchInput) {
                // Debounced live search
                let searchTimeout = null;
                searchInput.addEventListener('input', function(e) {
                    clearTimeout(searchTimeout);
                    searchTimeout = setTimeout(function() {
                        window.DashboardState.set('filters.clusters.search', searchInput.value);
                        self.renderClusters();
                    }, 300);
                });
            }
            
            // Clear button
            const clearBtn = document.getElementById('cluster-clear-btn');
            if (clearBtn) {
                clearBtn.addEventListener('click', function() {
                    const input = document.getElementById('cluster-search-input');
                    if (input) input.value = ''; 
                    window.DashboardState.set('filters.clusters.search', '');
                    window.DashboardState.set('filters.clusters.health', 'all');
                    window.DashboardState.set('filters.clusters.source', 'all');
                    self.renderClusters();
                });
            }
            
            // Refresh button
            const refreshBtn = document.getElementById('cluster-refresh-btn');
            if (refreshBtn) {
                refreshBtn.addEventListener('click', function() {
                    self.loadClusters();
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
            
            // Source select - immediate filter
            const sourceSelect = document.getElementById('cluster-source-select');
            if (sourceSelect) {
                sourceSelect.addEventListener('change', function(e) {
                    window.DashboardState.set('filters.clusters.source', e.target.value);
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




        // ===== Render Cluster Cards =====
        renderClusterCards: function(clusters) {
            var container = document.getElementById('cluster-cards-view');
            if (!container) return;

            if (clusters.length === 0) {
                container.innerHTML = '<div style="text-align:center;padding:48px 0;">' +
                    '<i class="bi bi-hdd-rack" style="font-size:2.5rem;opacity:0.4;color:#64748b;display:block;margin-bottom:12px;"></i>' +
                    '<div style="font-size:14px;color:#64748b;">' + __('index.cluster.empty') + '</div>' +
                    '<div style="font-size:12px;color:#94a3b8;margin-top:6px;">' + __('index.cluster.emptyHelp') + '</div></div>';
                return;
            }

            var html = '<div style="display:grid;grid-template-columns:repeat(auto-fill,minmax(420px,1fr));gap:16px;">';

            clusters.forEach(function(cluster) {
                var destinations = cluster.destinations || [];
                var overallHealth = 'unknown';
                if (cluster.healthyCount > 0) overallHealth = 'healthy';
                else if (cluster.unhealthyCount > 0) overallHealth = 'unhealthy';

                var borderColor = overallHealth === 'healthy' ? '#22c55e' : overallHealth === 'unhealthy' ? '#ef4444' : '#94a3b8';
                var healthIcon = overallHealth === 'healthy' ? 'bi-heart-pulse-fill' : overallHealth === 'unhealthy' ? 'bi-heart-pulse-fill' : 'bi-heart-pulse';
                var healthColor = overallHealth === 'healthy' ? '#22c55e' : overallHealth === 'unhealthy' ? '#ef4444' : '#94a3b8';
                var healthLabel = overallHealth === 'healthy' ? __('index.cluster.health.healthy') : overallHealth === 'unhealthy' ? __('index.cluster.health.unhealthy') : __('index.cluster.health.unknown');

                html += '<div style="border:1px solid var(--border-color);border-left:4px solid ' + borderColor +
                    ';border-radius:12px;background:var(--card-bg);overflow:hidden;transition:box-shadow 0.2s,transform 0.15s;cursor:pointer;"' +
                    ' onmouseover="this.style.boxShadow=\'0 4px 12px rgba(0,0,0,0.08)\';this.style.transform=\'translateY(-1px)\'"' +
                    ' onmouseout="this.style.boxShadow=\'none\';this.style.transform=\'none\'"' +
                    ' onclick="window.DashboardApp.modules.clusters.toggleCluster(\'' + (cluster.clusterId || '').replace(/'/g, "\\'") + '\')"' +
                    ' data-cluster-id="' + (cluster.clusterId || '') + '">';

                // Card header
                html += '<div style="padding:14px 16px;border-bottom:1px solid var(--border-color);display:flex;align-items:center;justify-content:space-between;gap:8px;">';
                html += '<div style="display:flex;align-items:center;gap:10px;min-width:0;flex:1;">';
                html += '<span style="display:inline-flex;align-items:center;justify-content:center;width:34px;height:34px;border-radius:8px;background:linear-gradient(135deg,#3b82f6,#60a5fa);color:#fff;font-size:15px;flex-shrink:0;"><i class="bi bi-hdd-stack"></i></span>';
                html += '<div style="min-width:0;flex:1;">';
                html += '<div style="font-weight:600;font-size:14px;color:var(--text-primary);white-space:nowrap;overflow:hidden;text-overflow:ellipsis;" title="' + (window.DashboardUtils ? DashboardUtils.escapeHtml(cluster.clusterId) : cluster.clusterId) + '">' + (window.DashboardUtils ? DashboardUtils.escapeHtml(cluster.clusterId) : cluster.clusterId) + '</div>';
                html += '<div style="display:flex;align-items:center;gap:6px;margin-top:3px;">';
                html += '<span>' + (window.DashboardUtils ? DashboardUtils.createSourceBadge(cluster.source) : cluster.source || '-') + '</span>';
                html += '<span style="color:#cbd5e1;">|</span>';
                html += '<span style="display:inline-flex;align-items:center;gap:4px;font-size:11px;color:' + healthColor + ';"><i class="bi ' + healthIcon + '"></i>' + healthLabel + '</span>';
                html += '</div></div></div>';

                // Action buttons
                html += '<div style="display:flex;gap:4px;flex-shrink:0;" onclick="event.stopPropagation()">';
                html += '<button style="border:1px solid var(--border-color);background:var(--card-bg);border-radius:6px;padding:4px 8px;font-size:12px;cursor:pointer;color:var(--primary-color);transition:background 0.15s;" onmouseover="this.style.background=\'#eff6ff\'" onmouseout="this.style.background=\'var(--card-bg)\'" onclick="event.stopPropagation();window.DashboardApp.modules.clusters.showEditModal(\'' + (cluster.clusterId || '').replace(/'/g, "\\'") + '\')" title="' + __('index.cluster.edit') + '"><i class="bi bi-pencil"></i></button>';
                html += '<button style="border:1px solid var(--border-color);background:var(--card-bg);border-radius:6px;padding:4px 8px;font-size:12px;cursor:pointer;color:#ef4444;transition:background 0.15s;" onmouseover="this.style.background=\'#fef2f2\'" onmouseout="this.style.background=\'var(--card-bg)\'" onclick="event.stopPropagation();window.DashboardApp.modules.clusters.deleteCluster(\'' + (cluster.clusterId || '').replace(/'/g, "\\'") + '\')" title="' + __('index.cluster.delete') + '"><i class="bi bi-trash"></i></button>';
                html += '</div></div>';

                // Destinations
                html += '<div style="padding:12px 16px;">';
                if (destinations.length === 0) {
                    html += '<div style="text-align:center;padding:16px 0;color:#94a3b8;font-size:13px;"><i class="bi bi-inbox me-1"></i>' + __('index.cluster.noDestinations') + '</div>';
                } else {
                    html += '<div style="display:flex;flex-direction:column;gap:8px;">';
                    destinations.forEach(function(dest) {
                        var ah = dest.activeHealth || 'Unknown';
                        var ph = dest.passiveHealth || 'Unknown';
                        var ahColor = ah === 'Healthy' ? '#22c55e' : ah === 'Unhealthy' ? '#ef4444' : '#94a3b8';
                        var phColor = ph === 'Healthy' ? '#22c55e' : ph === 'Unhealthy' ? '#ef4444' : '#94a3b8';

                        html += '<div style="background:var(--bg-secondary,#f8fafc);border:1px solid var(--border-color);border-radius:8px;padding:10px 12px;">';
                        html += '<div style="display:flex;align-items:center;justify-content:space-between;gap:8px;">';
                        html += '<div style="display:flex;align-items:center;gap:8px;min-width:0;flex:1;">';
                        html += '<i class="bi bi-robot" style="color:#6366f1;font-size:13px;flex-shrink:0;"></i>';
                        html += '<code style="font-size:12px;color:var(--text-secondary);flex-shrink:0;">' + (window.DashboardUtils ? DashboardUtils.escapeHtml(dest.name || '-') : dest.name || '-') + '</code>';
                        html += '<span style="font-size:12px;color:var(--text-secondary);font-family:monospace;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;min-width:0;flex:1;" title="' + (window.DashboardUtils ? DashboardUtils.escapeHtml(dest.address || '') : dest.address || '') + '">' + (window.DashboardUtils ? DashboardUtils.escapeHtml(dest.address || '-') : dest.address || '-') + '</span>';
                        html += '</div>';
                        html += '<div style="display:flex;align-items:center;gap:6px;flex-shrink:0;">';
                        html += '<span style="display:inline-flex;align-items:center;gap:3px;padding:2px 7px;border-radius:4px;font-size:10px;font-weight:500;background:' + (ah === 'Healthy' ? '#f0fdf4' : ah === 'Unhealthy' ? '#fef2f2' : '#f8fafc') + ';color:' + ahColor + ';border:1px solid ' + (ah === 'Healthy' ? '#bbf7d0' : ah === 'Unhealthy' ? '#fecaca' : '#e2e8f0') + ';"><i class="bi bi-activity"></i>' + ah + '</span>';
                        html += '<span style="display:inline-flex;align-items:center;gap:3px;padding:2px 7px;border-radius:4px;font-size:10px;font-weight:500;background:' + (ph === 'Healthy' ? '#f0fdf4' : ph === 'Unhealthy' ? '#fef2f2' : '#f8fafc') + ';color:' + phColor + ';border:1px solid ' + (ph === 'Healthy' ? '#bbf7d0' : ph === 'Unhealthy' ? '#fecaca' : '#e2e8f0') + ';"><i class="bi bi-shield-check"></i>' + ph + '</span>';
                        html += '</div></div></div>';
                    });
                    html += '</div>';
                }

                // Footer
                html += '<div style="display:flex;align-items:center;justify-content:space-between;margin-top:10px;padding-top:10px;border-top:1px solid var(--border-color);">';
                html += '<span style="font-size:11px;color:#64748b;"><i class="bi bi-nodes me-1"></i>' + destinations.length + ' ' + (__('index.cluster.destCount') || '节点') + '</span>';
                html += '<span style="font-size:11px;color:#64748b;"><i class="bi bi-sliders me-1"></i>' + (cluster.loadBalancingPolicy || 'RoundRobin') + '</span>';
                html += '</div>';

                html += '</div></div>';
            });

            html += '</div>';
            container.innerHTML = html;
        },

        // ===== Render Cluster Rows =====
        renderClusterRows: function(clusters, tbody) {
            window.DashboardDOM.clear(tbody);

            var fragment = document.createDocumentFragment();

            clusters.forEach(function(cluster) {
                var isExpanded = (window.DashboardState.get('ui.expandedClusters') || new Set()).has(cluster.clusterId);
                var rows = this.createClusterRows(cluster, isExpanded);
                rows.forEach(function(row) { fragment.appendChild(row); });
            }.bind(this));

            tbody.appendChild(fragment);
        },

        // ===== Create Cluster Rows =====
        createClusterRows: function(cluster, isExpanded) {
            var rows = [];
            var destinations = cluster.destinations || [];

            // Determine overall health status
            var overallHealth = 'Unknown';
            if (cluster.healthyCount > 0) overallHealth = 'Healthy';
            else if (cluster.unhealthyCount > 0) overallHealth = 'Unhealthy';

            // --- Cluster main row ---
            var headerTr = window.DashboardDOM.create('tr', {
                className: 'cluster-row health-' + overallHealth.toLowerCase(),
                attributes: { 'data-cluster-id': cluster.clusterId },
                style: { cursor: 'pointer' }
            });

            // Expand icon
            var tdExpand = window.DashboardDOM.create('td', {
                style: { width: '38px', verticalAlign: 'middle', textAlign: 'center' }
            });
            var expandIcon = window.DashboardDOM.create('i', {
                className: 'bi bi-chevron-right row-expand-icon' + (isExpanded ? ' expanded' : '')
            });
            tdExpand.appendChild(expandIcon);
            headerTr.appendChild(tdExpand);

            // Cluster name
            var tdName = window.DashboardDOM.create('td', {
                style: { overflow: 'hidden' }
            });
            var nameDiv = document.createElement('div');
            nameDiv.style.cssText = 'display:flex;align-items:center;gap:6px;';
            var nameStrong = document.createElement('strong');
            nameStrong.style.cssText = 'font-size:14px;color:var(--text-primary);white-space:nowrap;overflow:hidden;text-overflow:ellipsis;';
            nameStrong.textContent = cluster.clusterId;
            nameStrong.title = cluster.clusterId;
            nameDiv.appendChild(nameStrong);
            var nameCopyBtn = this.createCopyButton(cluster.clusterId);
            nameDiv.appendChild(nameCopyBtn);
            tdName.appendChild(nameDiv);
            headerTr.appendChild(tdName);

            // Source
            var tdSource = window.DashboardDOM.create('td', {
                style: { width: '90px', verticalAlign: 'middle' }
            });
            var sourceSpan = document.createElement('span');
            sourceSpan.innerHTML = this.createSourceBadge(cluster.source);
            tdSource.appendChild(sourceSpan);
            headerTr.appendChild(tdSource);

            // Health
            var tdHealth = window.DashboardDOM.create('td', {
                style: { width: '90px', verticalAlign: 'middle' }
            });
            var healthColors = { 'Healthy': '#22c55e', 'Unhealthy': '#ef4444', 'Unknown': '#94a3b8' };
            var healthLabels = {
                'Healthy': __('index.cluster.health.healthy'),
                'Unhealthy': __('index.cluster.health.unhealthy'),
                'Unknown': __('index.cluster.health.unknown')
            };
            var healthDiv = document.createElement('div');
            healthDiv.style.cssText = 'display:flex;align-items:center;gap:5px;font-size:13px;color:' + (healthColors[overallHealth] || '#94a3b8') + ';';
            var dotSpan = document.createElement('span');
            dotSpan.className = 'health-dot ' + overallHealth.toLowerCase();
            healthDiv.appendChild(dotSpan);
            healthDiv.appendChild(document.createTextNode(healthLabels[overallHealth] || overallHealth));
            tdHealth.appendChild(healthDiv);
            headerTr.appendChild(tdHealth);

            // Dest count
            var tdCount = window.DashboardDOM.create('td', {
                style: { width: '80px', verticalAlign: 'middle', textAlign: 'center' }
            });
            tdCount.textContent = destinations.length;
            headerTr.appendChild(tdCount);

            // Policy
            var tdPolicy = window.DashboardDOM.create('td', {
                style: { width: '110px', verticalAlign: 'middle' }
            });
            var policyBadge = document.createElement('span');
            policyBadge.className = 'badge bg-light text-dark';
            policyBadge.style.cssText = 'font-size:12px;font-weight:500;';
            policyBadge.textContent = cluster.loadBalancingPolicy || 'RoundRobin';
            tdPolicy.appendChild(policyBadge);
            headerTr.appendChild(tdPolicy);

            // Actions
            var tdActions = window.DashboardDOM.create('td', {
                style: { width: '80px', verticalAlign: 'middle', textAlign: 'center' }
            });
            tdActions.appendChild(this.createActionButtons(cluster));
            headerTr.appendChild(tdActions);

            // Click to expand
            headerTr.addEventListener('click', function(e) {
                if (e.target.closest('.copy-btn') || e.target.closest('.btn-group')) return;
                this.toggleCluster(cluster.clusterId);
            }.bind(this));

            rows.push(headerTr);

            // Expanded detail row
            if (isExpanded) {
                var detailTr = this.createClusterDetailRow(cluster);
                rows.push(detailTr);
            }

            return rows;
        },

        // ===== Create Health Badge =====
        createHealthBadge: function(health) {
            const healthMap = {
                'Healthy': { css: 'bg-success', icon: 'bi-check-circle-fill', text: __('index.cluster.health.healthy') },
                'Unhealthy': { css: 'bg-danger', icon: 'bi-x-circle-fill', text: __('index.cluster.health.unhealthy') },
                'Unknown': { css: 'bg-secondary', icon: 'bi-question-circle-fill', text: __('index.cluster.health.unknown') }
            }; 
        
            const config = healthMap[health] || healthMap['Unknown'];
                    
            const badge = window.DashboardDOM.create('span', {
                className: `badge ${config.css}`, 
                style: { fontSize: '11px', display: 'inline-flex', alignItems: 'center', gap: '4px' }
            });
                    
            const icon = window.DashboardDOM.create('i', {
                className: `bi ${config.icon}`
            });
            badge.appendChild(icon);
            badge.appendChild(document.createTextNode(' ' + config.text));
                    
            return badge;
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
                className: 'btn-group btn-group-sm'
            });

            // Edit button
            const editBtn = window.DashboardDOM.create('button', {
                className: 'btn btn-outline-primary',
                attributes: { title: __('index.cluster.edit') },
                events: {
                    click: (e) => {
                        e.stopPropagation();
                        this.showEditModal(cluster.clusterId);
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
                attributes: { title: __('index.cluster.delete') },
                events: {
                    click: (e) => {
                        e.stopPropagation();
                        this.deleteCluster(cluster.clusterId);
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

        // ===== Create Cluster Detail Row =====
        createClusterDetailRow: function(cluster) {
            const tr = window.DashboardDOM.create('tr', {
                className: 'cluster-detail-row'
            });

            const td = window.DashboardDOM.create('td', {
                attributes: { colspan: '7' }
            });

            const detailHtml = [];
            detailHtml.push('<div class="detail-panel">');

            // Quick actions bar
            detailHtml.push('<div class="detail-actions-bar">');
            detailHtml.push(`<div class="detail-actions-left"><span class="detail-actions-label"><i class="bi bi-gear"></i> ${__('index.cluster.title')}</span></div>`);
            detailHtml.push('<div class="detail-actions-right">');
            detailHtml.push(`<button class="btn btn-sm btn-outline-secondary detail-action-btn" onclick="ClustersModule.showEditModal('${window.DashboardUtils.escapeHtml(cluster.clusterId)}')" title="${__('index.cluster.edit')}"><i class="bi bi-pencil"></i> ${__('index.cluster.edit')}</button>`);
            detailHtml.push(`<button class="btn btn-sm btn-outline-primary detail-action-btn" onclick="ClustersModule.copyClusterJson('${window.DashboardUtils.escapeHtml(cluster.clusterId)}')" title="${__('index.copyJson.title')}"><i class="bi bi-clipboard-data"></i> ${__('index.copyJson')}</button>`);
            detailHtml.push('</div>');
            detailHtml.push('</div>');

            // Overview (compact key-value)
            const sourceBadge = this.createSourceBadge(cluster.source);
            detailHtml.push('<div class="detail-section">');
            detailHtml.push(`<div class="detail-section-title"><i class="bi bi-info-circle"></i>${__('index.route.basicInfo')}</div>`);
            detailHtml.push('<div class="detail-structured-config">');
            detailHtml.push(`<div class="detail-kv-row"><span class="detail-kv-key">ClusterId</span><span class="detail-kv-value"><code>${window.DashboardUtils.escapeHtml(cluster.clusterId)}</code></span></div>`);
            detailHtml.push(`<div class="detail-kv-row"><span class="detail-kv-key">Source</span><span class="detail-kv-value">${sourceBadge}</span></div>`);
            detailHtml.push(`<div class="detail-kv-row"><span class="detail-kv-key">${__('index.cluster.loadBalancing')}</span><span class="detail-kv-value"><span class="badge bg-light text-dark">${window.DashboardUtils.escapeHtml(cluster.loadBalancingPolicy || 'RoundRobin')}</span></span></div>`);
            detailHtml.push('</div>');
            detailHtml.push('</div>');

            // Destinations detail (with health info + weight)
            if (cluster.destinations && cluster.destinations.length > 0) {
                detailHtml.push('<div class="detail-section">');
                detailHtml.push(`<div class="detail-section-title"><i class="bi bi-server"></i>${__('index.cluster.destinations')} <span class="badge bg-light text-dark ms-2">${cluster.destinations.length}</span></div>`);

                // Check if cluster has PowerOfTwoChoices policy (weight-aware)
                const hasWeights = (cluster.loadBalancingPolicy || '').toLowerCase().includes('poweroftwo');

                detailHtml.push('<div class="table-responsive">');
                detailHtml.push('<table class="table table-sm detail-table">');
                if (hasWeights) {
                    detailHtml.push(`<thead><tr><th>${__('index.detail.name')}</th><th>${__('index.detail.address')}</th><th>${__('cluster.weight')}</th><th>${__('index.detail.active')}</th><th>${__('index.detail.passive')}</th></tr></thead>`);
                } else {
                    detailHtml.push(`<thead><tr><th>${__('index.detail.name')}</th><th>${__('index.detail.address')}</th><th>${__('index.detail.active')}</th><th>${__('index.detail.passive')}</th></tr></thead>`);
                }
                detailHtml.push('<tbody>');
                (cluster.destinations || []).forEach(dest => {
                    const activeBadge = this.createHealthBadgeInline(dest.activeHealth || 'Unknown');
                    const passiveBadge = this.createHealthBadgeInline(dest.passiveHealth || 'Unknown');
                    const weight = (dest.metadata && dest.metadata.Weight) || '1';
                    detailHtml.push('<tr>');
                    detailHtml.push(`<td><code>${dest.name || '-'}</code></td>`);
                    detailHtml.push(`<td><a href="${dest.address || '#'}" target="_blank" style="text-decoration:none;color:#0ea5e9;">${dest.address || '-'}</a></td>`);
                    if (hasWeights) {
                        detailHtml.push(`<td><span class="badge bg-light text-dark">${weight}</span></td>`);
                    }
                    detailHtml.push(`<td>${activeBadge}</td>`);
                    detailHtml.push(`<td>${passiveBadge}</td>`);
                    detailHtml.push('</tr>');
                });
                detailHtml.push('</tbody></table></div>');

                // Weight help text
                if (hasWeights) {
                    detailHtml.push(`<div class="text-muted mt-1" style="font-size:11px"><i class="bi bi-info-circle me-1"></i>${__('cluster.weightHelp')}</div>`);
                }

                detailHtml.push('</div>');
            }

            // Health Check - structured display
            if (cluster.healthCheck) {
                detailHtml.push('<div class="detail-section">');
                detailHtml.push(`<div class="detail-section-title"><i class="bi bi-heart-pulse"></i>${__('index.cluster.healthCheck')}</div>`);
                detailHtml.push(this.renderStructuredConfig(cluster.healthCheck, 'healthCheck'));
                detailHtml.push('</div>');
            }

            // Session Affinity - structured display
            if (cluster.sessionAffinity) {
                detailHtml.push('<div class="detail-section">');
                detailHtml.push(`<div class="detail-section-title"><i class="bi bi-link-45deg"></i>${__('index.cluster.sessionAffinity')}</div>`);
                detailHtml.push(this.renderStructuredConfig(cluster.sessionAffinity, 'sessionAffinity'));
                detailHtml.push('</div>');
            }

            // HTTP Client - structured display
            if (cluster.httpClient) {
                detailHtml.push('<div class="detail-section">');
                detailHtml.push(`<div class="detail-section-title"><i class="bi bi-globe"></i>${__('index.cluster.httpClient')}</div>`);
                detailHtml.push(this.renderStructuredConfig(cluster.httpClient, 'httpClient'));
                detailHtml.push('</div>');
            }

            // Metadata - structured key-value display
            if (cluster.metadata && Object.keys(cluster.metadata).length > 0) {
                detailHtml.push('<div class="detail-section">');
                detailHtml.push(`<div class="detail-section-title"><i class="bi bi-tags"></i>${__('index.route.metadata')}</div>`);
                detailHtml.push(this.renderStructuredConfig(cluster.metadata, 'metadata'));
                detailHtml.push('</div>');
            }

            detailHtml.push('</div>');
            td.innerHTML = detailHtml.join('');
            tr.appendChild(td);

            return tr;
        },

        // ===== Render Structured Config (instead of raw JSON) =====
        renderStructuredConfig: function(obj, configType) {
            if (!obj || typeof obj !== 'object') return '';

            // Known key display names
            const keyLabels = {
                // Health Check
                'Enabled': 'Enabled', 'Path': 'Path', 'Interval': 'Interval', 'Timeout': 'Timeout',
                'Policy': 'Policy', 'Destination': 'Destination', 'Port': 'Port',
                'Active': 'Active', 'Passive': 'Passive',
                'ConsecutiveFailures': 'ConsecutiveFailures', 'ReactivationPeriod': 'ReactivationPeriod',
                // Session Affinity
                'AffinityKeyName': 'Cookie/Key', 'Cookie': 'Cookie', 'FailurePolicy': 'FailurePolicy',
                'CustomAffinityPolicy': 'CustomPolicy',
                // HTTP Client
                'SslProtocols': 'SSL Protocols', 'DangerousAcceptAnyServerCertificate': 'SkipCertValidation',
                'MaxConnectionsPerServer': 'MaxConnections', 'EnableMultipleHttp2Connections': 'Http2MultiConn',
                'RequestHeaderEncoding': 'HeaderEncoding', 'WebProxy': 'WebProxy',
                // General
                'Name': 'Name', 'Value': 'Value', 'Domain': 'Domain', 'HttpOnly': 'HttpOnly',
                'SameSite': 'SameSite', 'Expiration': 'Expiration', 'MaxAge': 'MaxAge',
                'RequireRemoteCertificate': 'RequireCert',
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
                    const isUrl = typeof value === 'string' && (value.startsWith('http://') || value.startsWith('https://'));
                    const valHtml = isUrl
                        ? `<a href="${value}" target="_blank" class="text-decoration-none"><code>${value}</code></a>`
                        : `<code>${window.DashboardUtils.escapeHtml(String(value))}</code>`;
                    html.push(`<div class="detail-kv-row"${indent}><span class="detail-kv-key">${label}</span><span class="detail-kv-value">${valHtml}</span></div>`);
                } else if (Array.isArray(value)) {
                    const items = value.map(function(v) {
                        if (typeof v === 'object' && v !== null) {
                            return `<div class="detail-kv-nested">${JSON.stringify(v)}</div>`;
                        }
                        return `<code>${window.DashboardUtils.escapeHtml(String(v))}</code>`;
                    }).join(' ');
                    html.push(`<div class="detail-kv-row"${indent}><span class="detail-kv-key">${label}</span><span class="detail-kv-value">${items}</span></div>`);
                } else if (typeof value === 'object') {
                    // Nested object - render with a sub-header
                    html.push(`<div class="detail-kv-row detail-kv-group"${indent}><span class="detail-kv-key detail-kv-group-key"><i class="bi bi-chevron-right"></i> ${label}</span></div>`);
                    Object.keys(value).forEach(function(subKey) {
                        renderKeyValue(subKey, value[subKey], depth + 1);
                    });
                }
            };

            Object.keys(obj).forEach(function(key) {
                renderKeyValue(key, obj[key], 0);
            });

            // Always add a collapsible raw JSON toggle at the bottom
            html.push(`<div class="detail-raw-json-toggle"><details><summary><i class="bi bi-code-slash"></i> ${__('index.viewRawJson')}</summary><pre class="detail-raw-json">${window.DashboardUtils.escapeHtml(JSON.stringify(obj, null, 2))}</pre></details></div>`);

            html.push('</div>');
            return html.join('');
        },

        // ===== Copy Cluster JSON =====
        copyClusterJson: function(clusterId) {
            const clusters = window.DashboardState.get('data.clusters') || [];
            const cluster = clusters.find(function(c) { return c.clusterId === clusterId; });
            if (!cluster) return;

            // Build YARP-format JSON
            const yarpCluster = {
                "Destinations": {},
                "LoadBalancingPolicy": cluster.loadBalancingPolicy || "RoundRobin"
            };
            (cluster.destinations || []).forEach(function(dest) {
                yarpCluster.Destinations[dest.name || 'destination'] = { "Address": dest.address };
            });
            if (cluster.healthCheck) yarpCluster.HealthCheck = cluster.healthCheck;
            if (cluster.httpClient) yarpCluster.HttpClient = cluster.httpClient;
            if (cluster.sessionAffinity) yarpCluster.SessionAffinity = cluster.sessionAffinity;
            if (cluster.metadata && Object.keys(cluster.metadata).length > 0) yarpCluster.Metadata = cluster.metadata;

            const json = JSON.stringify(yarpCluster, null, 2);
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

        // ===== Create Health Badge Inline =====
        createHealthBadgeInline: function(health) {
            const healthMap = {
                'Healthy': { css: 'text-success', icon: 'bi-check-circle-fill' },
                'Unhealthy': { css: 'text-danger', icon: 'bi-x-circle-fill' },
                'Unknown': { css: 'text-secondary', icon: 'bi-question-circle-fill' }
            }; 
            const config = healthMap[health] || healthMap['Unknown'];
            return `<span class="${config.css}" style="display:inline-flex;align-items:center;gap:4px;font-size:13px;">
                <i class="bi ${config.icon}"></i>${health}
            </span>`;
        },


        // ===== Toggle Cluster =====
        toggleCluster: function(clusterId) {
            const state = window.DashboardState;
            const expandedSet = state.get('ui.expandedClusters');
            const current = expandedSet ? expandedSet.has(clusterId) : false;

            // Update state
            if (current) {
                expandedSet.delete(clusterId);
            } else {
                expandedSet.add(clusterId);
            }

            // Find the cluster row
            const headerRow = document.querySelector('.cluster-row[data-cluster-id="' + CSS.escape(clusterId) + '"]');
            if (headerRow) {
                // Update expand icon
                const expandIcon = headerRow.querySelector('.row-expand-icon');
                if (expandIcon) {
                    expandIcon.classList.toggle('expanded', !current);
                }

                // Check if next row is a detail row
                var detailRow = headerRow.nextElementSibling;
                if (detailRow && detailRow.classList.contains('cluster-detail-row')) {
                    detailRow.remove();
                } else if (!current) {
                    var cluster = state.get('data.clusters').find(function(c) { return c.clusterId === clusterId; });
                    if (cluster) {
                        var detailTr = this.createClusterDetailRow(cluster);
                        headerRow.after(detailTr);
                    }
                }
            }
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
                title: __('modal.addCluster'),
                data: defaultCluster,
                schemaType: 'cluster',
                size: 'xl',
                onSave: function(parsedData) {
                    // Validate cluster config
                    if (!parsedData.Destinations || typeof parsedData.Destinations !== 'object') {
                        window.DashboardModals.showError(__('index.cluster.invalidDestinations'));
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
                        window.DashboardModals.showError(__('index.cluster.invalidAddress'));
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
        
                window.DashboardModals.showInfo(__('index.cluster.saving'));
        
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
        
                window.DashboardModals.showSuccess(__('index.cluster.saved'));
                await this.loadClusters();
        
                document.dispatchEvent(new CustomEvent('dashboard:configChanged', {
                    detail: { type: 'cluster', id: clusterId, action: 'save' }
                }));
            } catch (error) {
                console.error('[Clusters] Save failed:', error);
                window.DashboardModals.showError(__('index.cluster.saveFailed') + error.message);
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
                            <div class="modal-content" style="border-radius:16px;border:none;box-shadow:0 25px 50px rgba(0,0,0,0.25);overflow:hidden;">
                                <div class="modal-header" style="background:linear-gradient(135deg,#f8fafc 0%,#e2e8f0 100%);border-bottom:1px solid #e2e8f0;padding:18px 24px;">
                                    <h5 class="modal-title" style="font-weight:600;font-size:16px;display:flex;align-items:center;gap:10px;">
                                        <span style="display:inline-flex;align-items:center;justify-content:center;width:32px;height:32px;border-radius:8px;background:linear-gradient(135deg,#6366f1,#818cf8);color:#fff;font-size:16px;">
                                            <i class="bi bi-tag"></i>
                                        </span>
                                        ${__('modal.clusterId')}
                                    </h5>
                                    <button type="button" class="btn-close" data-bs-dismiss="modal"></button>
                                </div>
                                <div class="modal-body" style="padding:24px;">
                                    <div style="margin-bottom:12px;">
                                        <label class="form-label" style="font-weight:500;font-size:13px;color:#334155;">Cluster ID</label>
                                        <input type="text" class="form-control" id="${modalId}-input" 
                                               placeholder="${__('modal.clusterIdPlaceholder')}"
                                               required
                                               style="border-radius:8px;padding:10px 14px;font-size:14px;border:1.5px solid #e2e8f0;transition:border-color 0.2s,box-shadow 0.2s;"
                                               onfocus="this.style.borderColor='#6366f1';this.style.boxShadow='0 0 0 3px rgba(99,102,241,0.1)'"
                                               onblur="this.style.borderColor='#e2e8f0';this.style.boxShadow='none'">
                                    </div>
                                    <div style="background:#f0f9ff;border:1px solid #bae6fd;border-radius:8px;padding:10px 14px;font-size:12px;color:#0369a1;display:flex;align-items:flex-start;gap:8px;">
                                        <i class="bi bi-info-circle" style="font-size:14px;margin-top:2px;flex-shrink:0;"></i>
                                        <span>${__('modal.clusterIdHelp')}</span>
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

        // ===== Save Cluster =====
        saveCluster: async function(clusterId, config) {
            try {
                window.DashboardModals.showInfo(__('index.cluster.saving'));

                const response = await window.DashboardApi.endpoints.saveCluster(clusterId, config);

                window.DashboardModals.showSuccess(__('index.cluster.saved'));

                // Reload clusters
                await this.loadClusters();

                // Trigger config saved event
                document.dispatchEvent(new CustomEvent('dashboard:configChanged', {
                    detail: { type: 'cluster', id: clusterId, action: 'save' }
                }));
            } catch (error) {
                console.error('[Clusters] Save failed:', error);
                window.DashboardModals.showError(__('index.cluster.saveFailed') + error.message);
            }
        },

        // ===== Show Edit Modal (JSON Mode) =====
        showEditModal: function(clusterId) {
            const self = this;
            
            // Get cluster data
            const clusters = window.DashboardState.get('data.clusters') || [];
            const cluster = clusters.find(c => c.clusterId === clusterId);
            if (!cluster) {
                window.DashboardModals.showError(__('index.cluster.notFound'));
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
                title: __('modal.editCluster'),
                data: yarpCluster,
                schemaType: 'cluster',
                size: 'xl',
                editableId: {
                    label: 'Cluster ID',
                    value: clusterId,
                    original: clusterId,
                    placeholder: __('modal.clusterIdPlaceholder')
                },
                onSave: function(parsedData, newId) {
                    // Validate cluster config
                    if (!parsedData.Destinations || typeof parsedData.Destinations !== 'object') {
                        window.DashboardModals.showError(__('index.cluster.invalidDestinations'));
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
                        window.DashboardModals.showError(__('index.cluster.invalidAddress'));
                        return false;
                    }

                    // Handle rename: only if ID actually changed (case-sensitive comparison)
                    if (newId && newId !== clusterId) {
                        self.renameCluster(clusterId, newId, parsedData);
                    } else {
                        self.saveClusterFromJson(parsedData, clusterId);
                    }
                    return true;
                }
            });
        },

        // ===== Delete Cluster =====
        // ===== Rename Cluster =====
        renameCluster: async function(oldId, newId, clusterConfig) {
            const self = this;
            try {
                window.DashboardModals.showInfo(__('index.cluster.renaming'));

                if (oldId !== newId) {
                    // Use atomic rename API - handles creating new cluster, updating routes, and deleting old cluster in one operation
                    const renameConfig = {
                        newClusterId: newId,
                        destinations: clusterConfig.Destinations,
                        loadBalancingPolicy: clusterConfig.LoadBalancingPolicy || undefined
                    };
                    await window.DashboardApi.endpoints.renameCluster(oldId, renameConfig);
                } else {
                    // Just update the cluster config (no rename)
                    const apiConfig = {
                        clusterId: newId,
                        destinations: clusterConfig.Destinations,
                        loadBalancingPolicy: clusterConfig.LoadBalancingPolicy || undefined,
                        healthCheck: clusterConfig.HealthCheck || undefined,
                        httpClient: clusterConfig.HttpClient || undefined,
                        httpRequest: clusterConfig.HttpRequest || undefined,
                        sessionAffinity: clusterConfig.SessionAffinity || undefined,
                        metadata: clusterConfig.Metadata || undefined
                    };
                    await window.DashboardApi.endpoints.saveCluster(newId, apiConfig);
                }

                window.DashboardModals.showSuccess(__('index.cluster.renamed'));
                await self.loadClusters();

                document.dispatchEvent(new CustomEvent('dashboard:configChanged', {
                    detail: { type: 'cluster', id: newId, oldId: oldId, action: 'rename' }
                }));
            } catch (error) {
                console.error('[Clusters] Rename failed:', error);
                window.DashboardModals.showError(__('index.cluster.renameFailed') + error.message);
            }
        },

        deleteCluster: async function(clusterId) {
            const self = this;
            
            window.DashboardModals.showConfirm(
                __('index.cluster.deleteConfirm').replace('{id}', clusterId) || `确认删除集群 '${clusterId}'？此操作不可撤销。`,
                async function() {
                    try {
                        window.DashboardModals.showInfo(__('index.cluster.deleting'));
                        
                        await window.DashboardApi.endpoints.deleteClusterConfig(clusterId);
                        await self.loadClusters();
                        
                        window.DashboardModals.showSuccess(__('index.cluster.deleted'));

                        // Trigger config deleted event
                        document.dispatchEvent(new CustomEvent('dashboard:configChanged', {
                            detail: { type: 'cluster', id: clusterId, action: 'delete' }
                        }));
                    } catch (error) {
                        console.error('[Clusters] Delete failed:', error);
                        window.DashboardModals.showError(__('index.cluster.deleteFailed') + error.message);
                    }
                },
                null,
                { title: __('modal.deleteCluster'), danger: true }
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
        btn.innerHTML = '<i class="bi bi-arrow-clockwise spin me-1"></i>' + (window.__('index.btn.loading'));
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
