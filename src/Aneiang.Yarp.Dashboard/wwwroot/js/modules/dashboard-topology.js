/**
 * Dashboard Topology Module
 * Visualizes YARP gateway topology using ECharts graph
 */
(function() {
    'use strict';

    const TopologyModule = {
        name: 'topology',
        initialized: false,
        chart: null,
        topologyData: null,
        trafficEnabled: true,
        refreshInterval: null,
        trafficInterval: null,
        currentLayout: 'hierarchical',

        // Node and Edge color schemes
        colors: {
            gateway: { main: '#3b82f6', bg: '#dbeafe' },
            route: { main: '#8b5cf6', bg: '#ede9fe' },
            cluster: { main: '#06b6d4', bg: '#cffafe' },
            destination: { main: '#10b981', bg: '#d1fae5' },
            status: {
                healthy: '#22c55e',
                warning: '#f59e0b',
                error: '#ef4444',
                unknown: '#9ca3af'
            }
        },

        // Icon mapping
        icons: {
            gateway: 'path://M12 2L2 7l10 5 10-5-10-5zM2 17l10 5 10-5M2 12l10 5 10-5',
            route: 'path://M13 7h-8v10h8v-10zM21 17h-8v-4h2v2h4v-4h-4v-2h6v8z',
            cluster: 'path://M20 6h-4V4c0-1.1-.9-2-2-2h-4c-1.1 0-2 .9-2 2v2H4c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2z',
            destination: 'path://M20 18c1.1 0 2-.9 2-2V6c0-1.1-.9-2-2-2H4c-1.1 0-2 .9-2 2v10c0 1.1.9 2 2 2H0v2h24v-2h-4zM4 6h16v10H4V6z'
        },

        // Type mapping (enum number to string)
        nodeTypes: ['gateway', 'route', 'cluster', 'destination'],
        statusTypes: ['healthy', 'warning', 'error', 'unknown'],

        // ===== Helper to convert enum to string =====
        getNodeTypeString: function(type) {
            if (typeof type === 'string') return type.toLowerCase();
            return this.nodeTypes[type] || 'destination';
        },

        getStatusString: function(status) {
            if (typeof status === 'string') return status.toLowerCase();
            return this.statusTypes[status] || 'unknown';
        },

        // ===== Initialization =====
        init: async function() {
            if (this.initialized) return;
            if (typeof echarts === 'undefined') {
                console.error('[Topology] ECharts not loaded');
                return;
            }

            console.log('[Topology] Initializing...');

            try {
                this.initChart();
                this.setupEvents();
                await this.loadTopology();
                this.startAutoRefresh();

                // Apply initial traffic effect after graph is rendered
                setTimeout(() => {
                    this.applyTrafficEffect();
                }, 500);

                this.initialized = true;
                console.log('[Topology] Initialized');
            } catch (error) {
                console.error('[Topology] Init failed:', error);
                this.hideLoading();
            }
        },

        // ===== Chart Initialization =====
        initChart: function() {
            const container = document.getElementById('topology-chart');
            if (!container) {
                console.error('[Topology] Chart container not found');
                return;
            }

            this.chart = echarts.init(container);

            this.chart.on('click', (params) => {
                if (params.dataType === 'node') {
                    this.showNodeDetail(params.data);
                }
            });

            // Handle window resize
            window.addEventListener('resize', () => {
                this.chart && this.chart.resize();
            });
        },

        // ===== Load Data =====
        loadTopology: async function() {
            this.showLoading();

            try {
                this.topologyData = await window.DashboardApi.get('/api/topology');
                this.renderStats(this.topologyData.stats);
                this.renderGraph();

                console.log('[Topology] Data loaded:', this.topologyData);
            } catch (error) {
                console.error('[Topology] Load failed:', error);
                this.showError(error.message);
            } finally {
                this.hideLoading();
            }
        },

        // ===== Render Statistics =====
        renderStats: function(stats) {
            this.setText('stat-routes', stats.routeCount);
            this.setText('stat-clusters', stats.clusterCount);
            this.setText('stat-destinations', stats.destinationCount);
            this.setText('stat-healthy', stats.healthyCount);
            this.setText('stat-unhealthy', stats.unhealthyCount);
            this.setText('stat-unlinked', stats.unlinkedRoutes + stats.unlinkedClusters);
        },

        setText: function(id, value) {
            const el = document.getElementById(id);
            if (el) el.textContent = value ?? '-';
        },

        // ===== Render Graph =====
        renderGraph: function() {
            if (!this.topologyData || !this.chart) return;

            const { nodes, edges } = this.topologyData;
            const option = this.buildChartOption(nodes, edges);

            this.chart.setOption(option, true);
        },

        buildChartOption: function(nodes, edges) {
            const echartsNodes = this.convertNodes(nodes);
            const echartsEdges = this.convertEdges(edges);

            return {
                tooltip: {
                    trigger: 'item',
                    formatter: (params) => this.formatTooltip(params)
                },
                animationDurationUpdate: 800,
                animationEasingUpdate: 'cubicOut',
                series: [{
                    type: 'graph',
                    layout: 'none',
                    symbolSize: (value, params) => this.getSymbolSize(params.data),
                    roam: true,
                    label: {
                        show: true,
                        position: 'bottom',
                        formatter: '{b}',
                        fontSize: 11,
                        color: '#374151'
                    },
                    edgeSymbol: ['circle', 'arrow'],
                    edgeSymbolSize: [4, 10],
                    edgeLabel: {
                        fontSize: 10,
                        show: false
                    },
                    data: echartsNodes,
                    links: echartsEdges,
                    lineStyle: {
                        opacity: 0.7,
                        width: 2,
                        curveness: 0.1,
                        color: 'source'
                    },
                    emphasis: {
                        focus: 'adjacency',
                        lineStyle: {
                            width: 4
                        }
                    },
                    // Apply custom layout positions
                    ...this.getCustomLayout(echartsNodes, echartsEdges)
                }]
            };
        },

        convertNodes: function(nodes) {
            return nodes.map(node => {
                const typeStr = this.getNodeTypeString(node.type);
                const statusStr = this.getStatusString(node.status);
                const colorInfo = this.colors[typeStr] || this.colors.destination;
                const statusColor = this.colors.status[statusStr] || this.colors.status.unknown;

                return {
                    id: node.id,
                    name: node.label,
                    value: this.getNodeValue(node),
                    symbol: this.icons[typeStr],
                    symbolSize: this.getSymbolSizeByType(typeStr),
                    itemStyle: {
                        color: colorInfo.main,
                        borderColor: statusColor,
                        borderWidth: 3,
                        shadowBlur: 10,
                        shadowColor: colorInfo.main + '40'
                    },
                    label: {
                        show: true,
                        position: 'bottom',
                        distance: 5
                    },
                    // Custom data
                    _original: node,
                    _type: typeStr,
                    _status: statusStr
                };
            });
        },

        convertEdges: function(edges) {
            return edges.map(edge => ({
                source: edge.source,
                target: edge.target,
                label: {
                    show: edge.label && edge.source.startsWith('route:'),
                    formatter: edge.label,
                    fontSize: 9,
                    color: '#6b7280'
                },
                lineStyle: {
                    color: this.getEdgeColor(edge),
                    width: this.getEdgeWidth(edge),
                    type: this.getEdgeType(edge)
                },
                _original: edge
            }));
        },

        getSymbolSize: function(data) {
            const sizes = {
                Gateway: 60,
                Route: 40,
                Cluster: 45,
                Destination: 30
            };
            return sizes[data._type] || 35;
        },

        getSymbolSizeByType: function(type) {
            const sizes = {
                gateway: 60,
                route: 40,
                cluster: 45,
                destination: 30
            };
            return sizes[type] || 35;
        },

        getNodeValue: function(node) {
            const typeStr = this.getNodeTypeString(node.type);
            switch (typeStr) {
                case 'gateway':
                    return 100;
                case 'route':
                    return 50;
                case 'cluster':
                    return 60;
                case 'destination':
                    return 40;
                default:
                    return 30;
            }
        },

        getEdgeColor: function(edge) {
            const colors = {
                Request: '#3b82f6',
                Forward: '#8b5cf6',
                Proxy: '#10b981'
            };
            return colors[edge.type] || '#9ca3af';
        },

        getEdgeWidth: function(edge) {
            const widths = {
                Request: 3,
                Forward: 2,
                Proxy: 2
            };
            return widths[edge.type] || 1;
        },

        getEdgeType: function(edge) {
            if (edge.type === 'Proxy') return 'dashed';
            return 'solid';
        },

        getCustomLayout: function(nodes, edges) {
            if (this.currentLayout === 'circular') {
                // Circular layout
                const radius = 200;
                const center = { x: 400, y: 300 };
                const angleStep = (2 * Math.PI) / nodes.length;

                nodes.forEach((node, i) => {
                    const angle = i * angleStep - Math.PI / 2;
                    node.x = center.x + radius * Math.cos(angle);
                    node.y = center.y + radius * Math.sin(angle);
                });
            } else if (this.currentLayout === 'hierarchical') {
                // Hierarchical layout - 4 levels: Gateway -> Route -> Cluster -> Destination
                const levels = {
                    'gateway': 0,
                    'route': 1,
                    'cluster': 2,
                    'destination': 3
                };

                const levelNodes = {};
                nodes.forEach(node => {
                    const level = levels[node._type];
                    if (!levelNodes[level]) levelNodes[level] = [];
                    levelNodes[level].push(node);
                });

                const levelX = [100, 300, 500, 700];
                Object.keys(levelNodes).forEach(level => {
                    const levelNodeList = levelNodes[level];
                    const yStep = 500 / (levelNodeList.length + 1);
                    levelNodeList.forEach((node, i) => {
                        node.x = levelX[level];
                        node.y = 50 + yStep * (i + 1);
                    });
                });
            }

            return {};
        },

        // ===== Tooltip Formatter =====
        formatTooltip: function(params) {
            if (params.dataType === 'node') {
                const node = params.data._original;
                const data = node.data || {};
                const typeStr = this.getNodeTypeString(node.type);
                const statusStr = this.getStatusString(node.status);

                let html = `<div style="font-weight:bold;margin-bottom:5px;">${node.label}</div>`;
                html += `<div style="font-size:12px;color:#666;">类型: ${this.getNodeTypeName(typeStr)}</div>`;
                html += `<div style="font-size:12px;color:#666;">状态: ${this.getStatusName(statusStr)}</div>`;

                // Add specific info based on node type
                if (typeStr === 'route') {
                    if (data.path) html += `<div style="font-size:12px;">路径: ${data.path}</div>`;
                    if (data.methods && data.methods.length) {
                        html += `<div style="font-size:12px;">方法: ${data.methods.join(', ')}</div>`;
                    }
                    if (data.clusterId) html += `<div style="font-size:12px;">集群: ${data.clusterId}</div>`;
                } else if (typeStr === 'cluster') {
                    html += `<div style="font-size:12px;">负载均衡: ${data.loadBalancingPolicy || '默认'}</div>`;
                    html += `<div style="font-size:12px;">健康: ${data.healthyCount}/${data.totalCount}</div>`;
                    if (data.sessionAffinity) html += `<div style="font-size:12px;">会话亲和性: 启用</div>`;
                } else if (typeStr === 'destination') {
                    if (data.health) html += `<div style="font-size:12px;">健康状态: ${data.health}</div>`;
                    if (data.address) html += `<div style="font-size:12px;">地址: ${data.address}</div>`;
                }

                return html;
            } else if (params.dataType === 'edge') {
                const edge = params.data._original;
                return `<div>连接</div><div style="font-size:12px;color:#666;">类型: ${edge.type}</div>`;
            }
            return '';
        },

        getNodeTypeName: function(type) {
            const names = {
                gateway: '网关',
                route: '路由',
                cluster: '集群',
                destination: '目的地'
            };
            return names[type] || type;
        },

        getStatusName: function(status) {
            const names = {
                healthy: '健康',
                warning: '警告',
                error: '错误',
                unknown: '未知'
            };
            return names[status] || status;
        },

        // ===== Detail Panel =====
        showNodeDetail: function(nodeData) {
            const node = nodeData._original;
            const data = node.data || {};
            const typeStr = this.getNodeTypeString(node.type);
            const statusStr = this.getStatusString(node.status);

            document.getElementById('detail-title').textContent = node.label;

            let content = '<table class="table table-sm table-borderless mb-0" style="font-size:13px;">';

            // Common fields
            content += `<tr><td class="text-muted" style="width:100px;">ID</td><td><code>${node.id}</code></td></tr>`;
            content += `<tr><td class="text-muted">类型</td><td>${this.getNodeTypeName(typeStr)}</td></tr>`;
            content += `<tr><td class="text-muted">状态</td><td><span class="badge bg-${this.getStatusClass(statusStr)}">${this.getStatusName(statusStr)}</span></td></tr>`;

            // Type-specific fields
            if (typeStr === 'route') {
                if (data.path) content += `<tr><td class="text-muted">匹配路径</td><td><code>${data.path}</code></td></tr>`;
                if (data.methods && data.methods.length) {
                    content += `<tr><td class="text-muted">HTTP方法</td><td>${data.methods.map(m => `<span class="badge bg-secondary me-1">${m}</span>`).join('')}</td></tr>`;
                }
                if (data.hosts && data.hosts.length) {
                    content += `<tr><td class="text-muted">主机</td><td>${data.hosts.join(', ')}</td></tr>`;
                }
                if (data.clusterId) content += `<tr><td class="text-muted">目标集群</td><td><a href="#" onclick="TopologyModule.goToCluster('${data.clusterId}')">${data.clusterId}</a></td></tr>`;
                if (data.order !== undefined) content += `<tr><td class="text-muted">优先级</td><td>${data.order}</td></tr>`;
                if (data.rateLimiterPolicy) content += `<tr><td class="text-muted">限流策略</td><td>${data.rateLimiterPolicy}</td></tr>`;
                if (data.timeoutPolicy) content += `<tr><td class="text-muted">超时策略</td><td>${data.timeoutPolicy}</td></tr>`;
                if (data.authorizationPolicy) content += `<tr><td class="text-muted">授权策略</td><td>${data.authorizationPolicy}</td></tr>`;
                if (data.transformCount) content += `<tr><td class="text-muted">转换器</td><td>${data.transformCount} 个</td></tr>`;
            } else if (typeStr === 'cluster') {
                content += `<tr><td class="text-muted">负载均衡</td><td>${data.loadBalancingPolicy || '默认'}</td></tr>`;
                content += `<tr><td class="text-muted">健康状态</td><td>健康: ${data.healthyCount} / 异常: ${data.unhealthyCount} / 未知: ${data.unknownCount}</td></tr>`;
                if (data.sessionAffinity) {
                    content += `<tr><td class="text-muted">会话亲和性</td><td><span class="badge bg-success">已启用</span></td></tr>`;
                }
                if (data.healthCheckActive) content += `<tr><td class="text-muted">主动健康检查</td><td><span class="badge bg-success">启用</span></td></tr>`;
                if (data.healthCheckPassive) content += `<tr><td class="text-muted">被动健康检查</td><td><span class="badge bg-success">启用</span></td></tr>`;
            } else if (typeStr === 'destination') {
                if (data.address) content += `<tr><td class="text-muted">地址</td><td><code>${data.address}</code></td></tr>`;
                if (data.host) content += `<tr><td class="text-muted">主机名</td><td>${data.host}</td></tr>`;
                if (data.health) content += `<tr><td class="text-muted">健康</td><td>${data.health}</td></tr>`;
                if (data.activeHealth) content += `<tr><td class="text-muted">主动检查</td><td>${data.activeHealth}</td></tr>`;
                if (data.passiveHealth) content += `<tr><td class="text-muted">被动检查</td><td>${data.passiveHealth}</td></tr>`;
            }

            content += '</table>';

            document.getElementById('detail-content').innerHTML = content;
            document.getElementById('topology-detail').style.display = 'block';
        },

        closeDetail: function() {
            document.getElementById('topology-detail').style.display = 'none';
        },

        getStatusClass: function(status) {
            const classes = {
                healthy: 'success',
                warning: 'warning',
                error: 'danger',
                unknown: 'secondary'
            };
            return classes[status] || 'secondary';
        },

        goToCluster: function(clusterId) {
            const routePrefix = window.__dashboard?.routePrefix || 'apigateway';
            window.location.href = `/${routePrefix}/clusters?highlight=${encodeURIComponent(clusterId)}`;
        },

        // ===== Traffic Animation =====
        // Traffic effect is now handled by ECharts edgeEffect

        toggleTraffic: function() {
            this.trafficEnabled = !this.trafficEnabled;
            const btn = document.getElementById('btn-toggle-traffic');
            if (btn) {
                btn.classList.toggle('active', this.trafficEnabled);
                btn.classList.toggle('btn-primary', this.trafficEnabled);
                btn.classList.toggle('btn-outline-secondary', !this.trafficEnabled);
            }
            // Apply traffic effect immediately
            this.applyTrafficEffect();
        },

        applyTrafficEffect: function() {
            if (!this.chart) return;

            if (this.trafficEnabled) {
                // Enable edge line effect (flow animation)
                this.chart.setOption({
                    series: [{
                        edgeEffect: {
                            show: true,
                            period: 4,
                            trailLength: 0.2,
                            symbol: 'arrow',
                            symbolSize: 8,
                            color: '#3b82f6'
                        }
                    }]
                });
            } else {
                // Disable edge line effect
                this.chart.setOption({
                    series: [{
                        edgeEffect: {
                            show: false
                        }
                    }]
                });
            }
        },

        // ===== Layout Switching =====
        switchLayout: function(mode) {
            this.currentLayout = mode;
            this.renderGraph();
        },

        // ===== Auto Refresh =====
        startAutoRefresh: function() {
            this.stopAutoRefresh();
            this.refreshInterval = setInterval(() => {
                this.loadTopology();
            }, 30000); // Refresh every 30 seconds
        },

        stopAutoRefresh: function() {
            if (this.refreshInterval) {
                clearInterval(this.refreshInterval);
                this.refreshInterval = null;
            }
        },

        // ===== UI Controls =====
        fitView: function() {
            if (this.chart) {
                this.chart.dispatchAction({
                    type: 'restore'
                });
            }
        },

        toggleFullscreen: function() {
            const chartContainer = document.querySelector('.card-panel:has(#topology-chart)');
            if (!chartContainer) return;

            if (!document.fullscreenElement) {
                chartContainer.requestFullscreen().then(() => {
                    // Resize chart after entering fullscreen
                    setTimeout(() => {
                        if (this.chart) {
                            this.chart.resize();
                        }
                    }, 100);
                }).catch(err => {
                    console.error('[Topology] Fullscreen error:', err);
                });
            } else {
                document.exitFullscreen().then(() => {
                    setTimeout(() => {
                        if (this.chart) {
                            this.chart.resize();
                        }
                    }, 100);
                });
            }
        },

        refresh: function() {
            this.loadTopology();
        },

        showLoading: function() {
            const el = document.getElementById('topology-loading');
            if (el) el.style.display = 'block';
        },

        hideLoading: function() {
            const el = document.getElementById('topology-loading');
            if (el) el.style.display = 'none';
        },

        showError: function(message) {
            const chart = document.getElementById('topology-chart');
            if (chart) {
                chart.innerHTML = `<div class="d-flex align-items-center justify-content-center h-100 text-danger"><i class="bi bi-exclamation-triangle me-2"></i>${message}</div>`;
            }
        },

        // ===== Event Handlers =====
        setupEvents: function() {
            // Layout mode radio buttons
            document.querySelectorAll('input[name="layoutMode"]').forEach(radio => {
                radio.addEventListener('change', (e) => {
                    this.switchLayout(e.target.value);
                });
            });

            // Toggle traffic button
            const trafficBtn = document.getElementById('btn-toggle-traffic');
            if (trafficBtn) {
                trafficBtn.addEventListener('click', () => this.toggleTraffic());
                trafficBtn.classList.add('active', 'btn-primary');
            }

            // Fit view button
            const fitBtn = document.getElementById('btn-fit-view');
            if (fitBtn) {
                fitBtn.addEventListener('click', () => this.fitView());
            }

            // Refresh button
            const refreshBtn = document.getElementById('btn-refresh');
            if (refreshBtn) {
                refreshBtn.addEventListener('click', () => this.refresh());
            }

            // Fullscreen button
            const fullscreenBtn = document.getElementById('btn-fullscreen');
            if (fullscreenBtn) {
                fullscreenBtn.addEventListener('click', () => this.toggleFullscreen());
            }
        },

        // ===== Cleanup =====
        destroy: function() {
            this.stopAutoRefresh();
            if (this.chart) {
                this.chart.dispose();
                this.chart = null;
            }
            this.initialized = false;
        }
    };

    // Export to window
    window.TopologyModule = TopologyModule;
})();
