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
        currentLayout: 'hierarchical',
        _resizeHandler: null,

        colors: {
            gateway: { bg: '#dbeafe', main: '#3b82f6', text: '#1d4ed8' },
            route:   { bg: '#ede9fe', main: '#8b5cf6', text: '#5b21b6' },
            cluster: { bg: '#cffafe', main: '#06b6d4', text: '#0e7490' },
            destination: { bg: '#d1fae5', main: '#10b981', text: '#065f46' },
            edge: {
                Request: '#3b82f6',
                Forward: '#8b5cf6',
                Proxy: '#10b981'
            },
            status: {
                healthy: '#22c55e',
                warning: '#f59e0b',
                error:   '#ef4444',
                unknown: '#9ca3af'
            }
        },

        icons: {},

        nodeTypes: ['gateway', 'route', 'cluster', 'destination'],
        statusTypes: ['healthy', 'warning', 'error', 'unknown'],

        t: function(key) {
            if (window.DashboardI18n && window.DashboardI18n.t) {
                return window.DashboardI18n.t(key);
            }
            const fallbacks = {
                'topology.detail.type': '类型',
                'topology.detail.status': '状态',
                'topology.detail.cluster': '集群',
                'topology.detail.healthStatus': '健康状态',
                'topology.detail.connection': '连接',
                'topology.legend.gateway': '网关',
                'topology.legend.route': '路由',
                'topology.legend.cluster': '集群',
                'topology.legend.destination': '目的地',
                'topology.legend.healthy': '健康',
                'topology.legend.warning': '警告',
                'topology.legend.error': '错误',
                'topology.detail.matchPath': '匹配路径',
                'topology.detail.httpMethods': 'HTTP方法',
                'topology.detail.hosts': '主机',
                'topology.detail.targetCluster': '目标集群',
                'topology.detail.priority': '优先级',
                'topology.detail.rateLimitPolicy': '限流策略',
                'topology.detail.timeoutPolicy': '超时策略',
                'topology.detail.authorizationPolicy': '授权策略',
                'topology.detail.transforms': '转换器',
                'topology.detail.loadBalancing': '负载均衡',
                'topology.detail.sessionAffinity': '会话亲和性',
                'topology.detail.activeHealthCheck': '主动健康检查',
                'topology.detail.passiveHealthCheck': '被动健康检查',
                'topology.detail.address': '地址',
                'topology.detail.host': '主机名',
                'topology.detail.activeHealth': '主动检查',
                'topology.detail.passiveHealth': '被动检查',
                'topology.detail.enabled': '已启用',
                'topology.detail.disabled': '未启用'
            };
            return fallbacks[key] || key;
        },

        getNodeTypeString: function(type) {
            if (typeof type === 'string') return type.toLowerCase();
            return this.nodeTypes[type] || 'destination';
        },

        getStatusString: function(status) {
            if (typeof status === 'string') return status.toLowerCase();
            return this.statusTypes[status] || 'unknown';
        },

        init: async function() {
            if (this.initialized) return;
            if (typeof echarts === 'undefined') {
                console.error('[Topology] ECharts not loaded');
                return;
            }

            console.log('[Topology] Initializing...');

            this.initChart();
            this.setupEvents();
            await this.loadTopology();
            this.startAutoRefresh();

            this.initialized = true;
            console.log('[Topology] Initialized');
        },

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
                } else if (params.dataType === 'edge') {
                    this.showEdgeDetail(params.data);
                }
            });

            this._resizeHandler = () => {
                if (this.chart) this.chart.resize();
            };
            window.addEventListener('resize', this._resizeHandler);
        },

        loadTopology: async function() {
            this.showLoading();

            try {
                this.topologyData = await window.DashboardApi.get('/api/topology');
                this.renderStats(this.topologyData.stats);
                this.renderGraph();
                console.log('[Topology] Data loaded:', this.topologyData);
            } catch (error) {
                console.error('[Topology] Load failed:', error);
                this.showError(error.message || '加载拓扑数据失败');
            } finally {
                this.hideLoading();
            }
        },

        renderStats: function(stats) {
            const el = (id, val) => {
                const e = document.getElementById(id);
                if (e) e.textContent = val ?? '-';
            };
            el('stat-routes', stats.routeCount);
            el('stat-clusters', stats.clusterCount);
            el('stat-destinations', stats.destinationCount);
            el('stat-healthy', stats.healthyCount);
            el('stat-unhealthy', stats.unhealthyCount);
            el('stat-unlinked', (stats.unlinkedRoutes || 0) + (stats.unlinkedClusters || 0));
        },

        renderGraph: function() {
            if (!this.topologyData || !this.chart) return;

            const { nodes, edges } = this.topologyData;
            this.applyLayout(edges);
            const option = this.buildChartOption(nodes, edges);
            this.chart.setOption(option, true);
        },

        // ===== Layout Algorithm =====
        // 4-column layout: Gateway | Routes | Clusters | Destinations
        // Each column distributes its nodes evenly with guaranteed minimum spacing
        // so icons never overlap.
        applyLayout: function(edges) {
            if (!this.topologyData) return;
            const nodes = this.topologyData.nodes;

            const routeToCluster = {};
            const clusterToDests = {};

            edges.forEach(e => {
                const [srcType] = e.source.split(':');
                const [tgtType] = e.target.split(':');
                if (srcType === 'route' && tgtType === 'cluster') routeToCluster[e.source] = e.target;
                if (srcType === 'cluster' && tgtType === 'destination') {
                    if (!clusterToDests[e.source]) clusterToDests[e.source] = [];
                    clusterToDests[e.source].push(e.target);
                }
            });

            const clusters = nodes.filter(n => this.getNodeTypeString(n.type) === 'cluster');
            const routes   = nodes.filter(n => this.getNodeTypeString(n.type) === 'route');
            const dests    = nodes.filter(n => this.getNodeTypeString(n.type) === 'destination');
            const gateway   = nodes.find(n => this.getNodeTypeString(n.type) === 'gateway');

            if (this.currentLayout === 'hierarchical') {
                // Guaranteed minimum vertical step per column type
                // (must exceed symbolSize / 2 + padding so icons don't touch)
                const STEP = 65;

                // Canvas height: accommodate the most populous column
                const maxCol = Math.max(routes.length, clusters.length, dests.length);
                const H = Math.max(500, maxCol * STEP + 100);

                // Column X positions
                const GW_X = 70, RT_X = 280, CL_X = 520, DS_X = 760;

                // Gateway: centre-left, vertically centred
                if (gateway) { gateway._x = GW_X; gateway._y = H / 2; }

                // Routes column — one row per route, evenly spaced
                const rtSpacing = H / (routes.length + 1);
                routes.forEach((r, i) => {
                    r._x = RT_X;
                    r._y = 30 + rtSpacing * (i + 1);
                });

                // Clusters column — one row per cluster, evenly spaced
                const clSpacing = H / (clusters.length + 1);
                clusters.forEach((c, i) => {
                    c._x = CL_X;
                    c._y = 30 + clSpacing * (i + 1);
                });

                // Destinations column — fill top-down evenly, regardless of cluster affiliation
                const dsSpacing = H / (dests.length + 1);
                dests.forEach((d, i) => {
                    d._x = DS_X;
                    d._y = 30 + dsSpacing * (i + 1);
                });

                // Orphan nodes (should not happen but guard anyway)
                nodes.forEach(n => {
                    if (n._x === undefined) { n._x = DS_X; n._y = 30 + Math.random() * (H - 60); }
                });

            } else {
                // Circular layout
                const groups = [gateway ? [gateway] : [], routes, clusters, dests];
                const cx = 420, cy = 350;
                const maxR = Math.max(120, groups.flat().length * 12 + 80);

                groups.forEach((group, gi) => {
                    if (!group.length) return;
                    const angleStart = (gi / groups.length) * 2 * Math.PI - Math.PI / 2;
                    const angleEnd = ((gi + 1) / groups.length) * 2 * Math.PI - Math.PI / 2;
                    const r = maxR - gi * 70;
                    group.forEach((node, ni) => {
                        const angle = angleStart + (angleEnd - angleStart) * (ni / Math.max(group.length - 1, 1));
                        node._x = cx + r * Math.cos(angle);
                        node._y = cy + r * Math.sin(angle);
                    });
                });
            }
        },

        buildChartOption: function(nodes, edges) {
            const echartsNodes = this.convertNodes(nodes);
            const echartsEdges = this.convertEdges(edges);

            return {
                animation: true,
                animationDuration: 500,
                animationEasingUpdate: 'cubicOut',
                tooltip: {
                    trigger: 'item',
                    formatter: (params) => this.formatTooltip(params)
                },
                xAxis: { show: false, min: -80, max: 860 },
                yAxis: { show: false, min: -60, max: 700 },
                series: [{
                    type: 'graph',
                    layout: 'none',
                    symbol: 'circle',
                    symbolSize: 5,
                    roam: true,
                    draggable: false,
                    edgeSymbol: this.trafficEnabled ? ['circle', 'arrow'] : ['none', 'none'],
                    edgeSymbolSize: this.trafficEnabled ? [5, 14] : [0, 0],
                    data: echartsNodes,
                    links: echartsEdges,
                    lineStyle: {
                        opacity: this.trafficEnabled ? 0.75 : 0.3,
                        width: 1.5,
                        curveness: 0.06,
                        color: 'source'
                    },
                    emphasis: {
                        focus: 'adjacency',
                        lineStyle: { width: 3, opacity: 1 }
                    },
                    select: { disabled: true }
                }]
            };
        },

        convertEdges: function(edges) {
            return edges.map(edge => ({
                source: edge.source,
                target: edge.target,
                lineStyle: {
                    color: this.getEdgeColor(edge),
                    width: this.getEdgeWidth(edge)
                },
                _original: edge
            }));
        },

        getEdgeColor: function(edge) {
            return this.colors.edge[edge.type] || '#9ca3af';
        },

        getEdgeWidth: function(edge) {
            const widths = { Request: 3, Forward: 2, Proxy: 2 };
            return widths[edge.type] || 1;
        },

        toggleTraffic: function() {
            this.trafficEnabled = !this.trafficEnabled;
            const btn = document.getElementById('btn-toggle-traffic');
            if (btn) {
                btn.classList.toggle('active', this.trafficEnabled);
                btn.classList.toggle('btn-primary', this.trafficEnabled);
                btn.classList.toggle('btn-outline-secondary', !this.trafficEnabled);
            }

            if (!this.chart) return;

            this.chart.setOption({
                series: [{
                    edgeSymbol: this.trafficEnabled ? ['circle', 'arrow'] : ['none', 'none'],
                    edgeSymbolSize: this.trafficEnabled ? [5, 12] : [0, 0],
                    lineStyle: {
                        opacity: this.trafficEnabled ? 0.8 : 0.35
                    }
                }]
            });
        },

        // Convert nodes to ECharts data items using SVG path symbols
        convertNodes: function(nodes) {
            return nodes.map(node => {
                const typeStr = this.getNodeTypeString(node.type);
                const statusStr = this.getStatusString(node.status);
                const ci = this.colors[typeStr] || this.colors.destination;
                const statusColor = this.colors.status[statusStr] || this.colors.status.unknown;

                return {
                    id: node.id,
                    name: node.label,
                    x: node._x || 0,
                    y: node._y || 0,
                    symbol: 'circle',
                    symbolSize: 26,
                    itemStyle: {
                        color: ci.main,
                        borderColor: statusColor,
                        borderWidth: 2,
                        shadowBlur: 8,
                        shadowColor: ci.main + '50'
                    },
                    label: {
                        show: true,
                        position: 'bottom',
                        distance: 4,
                        fontSize: 10,
                        fontWeight: '500',
                        fontFamily: 'system-ui, sans-serif',
                        color: '#6b7280'
                    },
                    emphasis: {
                        itemStyle: {
                            shadowBlur: 16,
                            shadowColor: ci.main + '90'
                        }
                    },
                    _original: node,
                    _type: typeStr,
                    _status: statusStr
                };
            });
        },

        getSymbolSizeByType: function(type) {
            return 26;
        },

        switchLayout: function(mode) {
            this.currentLayout = mode;
            this.renderGraph();
        },

        convertEdges: function(edges) {
            return edges.map(edge => ({
                source: edge.source,
                target: edge.target,
                lineStyle: {
                    color: this.getEdgeColor(edge),
                    width: this.getEdgeWidth(edge)
                },
                _original: edge
            }));
        },

        startAutoRefresh: function() {
            this.stopAutoRefresh();
            this.refreshInterval = setInterval(() => this.loadTopology(), 30000);
        },

        stopAutoRefresh: function() {
            if (this.refreshInterval) { clearInterval(this.refreshInterval); this.refreshInterval = null; }
        },

        fitView: function() {
            if (this.chart) this.chart.dispatchAction({ type: 'restore' });
        },

        toggleFullscreen: function() {
            const panel = document.querySelector('.card-panel:has(#topology-chart)');
            if (!panel) return;
            if (!document.fullscreenElement) {
                panel.requestFullscreen().then(() => setTimeout(() => this.chart && this.chart.resize(), 100)).catch(() => {});
            } else {
                document.exitFullscreen().then(() => setTimeout(() => this.chart && this.chart.resize(), 100)).catch(() => {});
            }
        },

        refresh: function() { this.loadTopology(); },

        showLoading: function() {
            const el = document.getElementById('topology-loading');
            if (el) el.style.display = 'flex';
        },

        hideLoading: function() {
            const el = document.getElementById('topology-loading');
            if (el) el.style.display = 'none';
        },

        showError: function(message) {
            const chart = document.getElementById('topology-chart');
            if (chart) {
                chart.innerHTML = `<div class="d-flex flex-column align-items-center justify-content-center h-100 text-muted">
                    <i class="bi bi-exclamation-triangle text-warning fs-1 mb-2"></i><div>${message}</div>
                </div>`;
            }
        },

        // ===== Tooltip =====
        formatTooltip: function(params) {
            if (params.dataType === 'node') {
                const node = params.data._original;
                const data = node.data || {};
                const typeStr = this.getNodeTypeString(node.type);
                const statusStr = this.getStatusString(node.status);

                let html = `<div style="font-weight:600;margin-bottom:4px;">${node.label}</div>`;
                html += `<div style="font-size:12px;color:#6b7280;">${this.t('topology.detail.type')}: ${this.getNodeTypeName(typeStr)}</div>`;
                html += `<div style="font-size:12px;color:#6b7280;">${this.t('topology.detail.status')}: ${this.getStatusName(statusStr)}</div>`;

                if (typeStr === 'route') {
                    if (data.path) html += `<div style="font-size:11px;margin-top:2px;"><code>${data.path}</code></div>`;
                    if (data.clusterId) html += `<div style="font-size:11px;">${this.t('topology.detail.targetCluster')}: ${data.clusterId}</div>`;
                } else if (typeStr === 'cluster') {
                    html += `<div style="font-size:11px;">${this.t('topology.detail.healthStatus')}: ${data.healthyCount || 0}/${data.totalCount || 0}</div>`;
                } else if (typeStr === 'destination') {
                    if (data.address) html += `<div style="font-size:11px;"><code>${data.address}</code></div>`;
                    if (data.health) html += `<div style="font-size:11px;">${this.t('topology.detail.healthStatus')}: ${data.health}</div>`;
                }
                return html;

            } else if (params.dataType === 'edge') {
                const edge = params.data._original || {};
                const colorMap = { Request: '请求', Forward: '转发', Proxy: '代理' };
                return `<div><b>${this.t('topology.detail.connection')}</b></div>
                    <div style="font-size:11px;color:#6b7280;">${this.t('topology.detail.type')}: ${colorMap[edge.type] || '-'}</div>`;
            }
            return '';
        },

        // ===== Detail Panel =====
        showNodeDetail: function(nodeData) {
            const node = nodeData._original;
            const data = node.data || {};
            const typeStr = this.getNodeTypeString(node.type);
            const statusStr = this.getStatusString(node.status);

            document.getElementById('detail-title').textContent = node.label;

            let content = '<table class="table table-sm table-borderless mb-0" style="font-size:13px;">';
            content += `<tr><td class="text-muted" style="width:110px;">ID</td><td><code style="font-size:11px;">${node.id}</code></td></tr>`;
            content += `<tr><td class="text-muted">${this.t('topology.detail.type')}</td><td>${this.getNodeTypeName(typeStr)}</td></tr>`;
            content += `<tr><td class="text-muted">${this.t('topology.detail.status')}</td><td><span class="badge bg-${this.getStatusClass(statusStr)}">${this.getStatusName(statusStr)}</span></td></tr>`;

            if (typeStr === 'route') {
                if (data.path) content += `<tr><td class="text-muted">${this.t('topology.detail.matchPath')}</td><td><code>${data.path}</code></td></tr>`;
                if (data.methods && data.methods.length) content += `<tr><td class="text-muted">${this.t('topology.detail.httpMethods')}</td><td>${data.methods.map(m => `<span class="badge bg-secondary me-1">${m}</span>`).join('')}</td></tr>`;
                if (data.hosts && data.hosts.length) content += `<tr><td class="text-muted">${this.t('topology.detail.hosts')}</td><td>${data.hosts.join(', ')}</td></tr>`;
                if (data.clusterId) content += `<tr><td class="text-muted">${this.t('topology.detail.targetCluster')}</td><td><a href="javascript:void(0)" onclick="TopologyModule.goToCluster('${data.clusterId}')">${data.clusterId}</a></td></tr>`;
                if (data.order !== undefined) content += `<tr><td class="text-muted">${this.t('topology.detail.priority')}</td><td>${data.order}</td></tr>`;
                if (data.rateLimiterPolicy) content += `<tr><td class="text-muted">${this.t('topology.detail.rateLimitPolicy')}</td><td>${data.rateLimiterPolicy}</td></tr>`;
                if (data.timeoutPolicy) content += `<tr><td class="text-muted">${this.t('topology.detail.timeoutPolicy')}</td><td>${data.timeoutPolicy}</td></tr>`;
                if (data.authorizationPolicy) content += `<tr><td class="text-muted">${this.t('topology.detail.authorizationPolicy')}</td><td>${data.authorizationPolicy}</td></tr>`;
                if (data.transformCount) content += `<tr><td class="text-muted">${this.t('topology.detail.transforms')}</td><td>${data.transformCount}</td></tr>`;
            } else if (typeStr === 'cluster') {
                content += `<tr><td class="text-muted">${this.t('topology.detail.loadBalancing')}</td><td>${data.loadBalancingPolicy || '默认'}</td></tr>`;
                content += `<tr><td class="text-muted">${this.t('topology.detail.healthStatus')}</td><td>
                    <span class="text-success">${data.healthyCount || 0}</span> /
                    <span class="text-warning">${data.unknownCount || 0}</span> /
                    <span class="text-danger">${data.unhealthyCount || 0}</span>
                </td></tr>`;
                if (data.sessionAffinity) content += `<tr><td class="text-muted">${this.t('topology.detail.sessionAffinity')}</td><td><span class="badge bg-success">${this.t('topology.detail.enabled')}</span></td></tr>`;
                if (data.healthCheckActive) content += `<tr><td class="text-muted">${this.t('topology.detail.activeHealthCheck')}</td><td><span class="badge bg-success">${this.t('topology.detail.enabled')}</span></td></tr>`;
                if (data.healthCheckPassive) content += `<tr><td class="text-muted">${this.t('topology.detail.passiveHealthCheck')}</td><td><span class="badge bg-success">${this.t('topology.detail.enabled')}</span></td></tr>`;
                content += `<tr><td class="text-muted" colspan="2"><a href="javascript:void(0)" onclick="TopologyModule.goToCluster('${node.id}')" class="small">查看集群详情 &rarr;</a></td></tr>`;
            } else if (typeStr === 'destination') {
                if (data.address) content += `<tr><td class="text-muted">${this.t('topology.detail.address')}</td><td><code>${data.address}</code></td></tr>`;
                if (data.host) content += `<tr><td class="text-muted">${this.t('topology.detail.host')}</td><td>${data.host}</td></tr>`;
                if (data.health) content += `<tr><td class="text-muted">${this.t('topology.detail.healthStatus')}</td><td>${data.health}</td></tr>`;
                if (data.activeHealth) content += `<tr><td class="text-muted">${this.t('topology.detail.activeHealth')}</td><td>${data.activeHealth}</td></tr>`;
                if (data.passiveHealth) content += `<tr><td class="text-muted">${this.t('topology.detail.passiveHealth')}</td><td>${data.passiveHealth}</td></tr>`;
            }

            content += '</table>';
            document.getElementById('detail-content').innerHTML = content;
            document.getElementById('topology-detail').style.display = 'block';
        },

        showEdgeDetail: function(edgeData) {
            const edge = edgeData._original || {};
            const colorMap = { Request: '请求', Forward: '转发', Proxy: '代理' };
            document.getElementById('detail-title').textContent = this.t('topology.detail.connection');
            const content = `<table class="table table-sm table-borderless mb-0" style="font-size:13px;">
                <tr><td class="text-muted" style="width:110px;">${this.t('topology.detail.type')}</td><td>${colorMap[edge.type] || '-'}</td></tr>
                <tr><td class="text-muted">Source</td><td><code>${edge.source || '-'}</code></td></tr>
                <tr><td class="text-muted">Target</td><td><code>${edge.target || '-'}</code></td></tr>
            </table>`;
            document.getElementById('detail-content').innerHTML = content;
            document.getElementById('topology-detail').style.display = 'block';
        },

        closeDetail: function() {
            document.getElementById('topology-detail').style.display = 'none';
        },

        getNodeTypeName: function(type) {
            const keys = { gateway: 'topology.legend.gateway', route: 'topology.legend.route', cluster: 'topology.legend.cluster', destination: 'topology.legend.destination' };
            return this.t(keys[type]) || type;
        },

        getStatusName: function(status) {
            const keys = { healthy: 'topology.legend.healthy', warning: 'topology.legend.warning', error: 'topology.legend.error', unknown: 'topology.legend.error' };
            return this.t(keys[status]) || status;
        },

        getStatusClass: function(status) {
            return { healthy: 'success', warning: 'warning', error: 'danger', unknown: 'secondary' }[status] || 'secondary';
        },

        goToCluster: function(clusterId) {
            const routePrefix = window.__dashboard?.routePrefix || 'apigateway';
            window.location.href = `/${routePrefix}/clusters?highlight=${encodeURIComponent(clusterId)}`;
        },

        setupEvents: function() {
            document.querySelectorAll('input[name="layoutMode"]').forEach(radio => {
                radio.addEventListener('change', (e) => this.switchLayout(e.target.value));
            });

            const trafficBtn = document.getElementById('btn-toggle-traffic');
            if (trafficBtn) {
                trafficBtn.addEventListener('click', () => this.toggleTraffic());
                trafficBtn.classList.add('active', 'btn-primary');
            }

            const fitBtn = document.getElementById('btn-fit-view');
            if (fitBtn) fitBtn.addEventListener('click', () => this.fitView());

            const refreshBtn = document.getElementById('btn-refresh');
            if (refreshBtn) refreshBtn.addEventListener('click', () => this.refresh());

            const fullscreenBtn = document.getElementById('btn-fullscreen');
            if (fullscreenBtn) fullscreenBtn.addEventListener('click', () => this.toggleFullscreen());
        },

        destroy: function() {
            this.stopAutoRefresh();
            if (this._resizeHandler) { window.removeEventListener('resize', this._resizeHandler); this._resizeHandler = null; }
            if (this.chart) { this.chart.dispose(); this.chart = null; }
            this.initialized = false;
        }
    };

    window.TopologyModule = TopologyModule;
})();
