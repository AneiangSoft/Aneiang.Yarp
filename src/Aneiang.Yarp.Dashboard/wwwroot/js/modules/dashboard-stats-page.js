/**
 * Dashboard Stats Page Module
 * Unified stats page with time range selector and Chart.js charts
 */
(function() {
    'use strict';

    const StatsPageModule = {
        name: 'stats-page',
        initialized: false,
        qpsChart: null,
        latencyChart: null,
        errorChart: null,
        statusChart: null,
        currentRange: 60, // minutes
        refreshInterval: null,

        init: function() {
            if (this.initialized) return;
            this.setupEvents();
            this.initialized = true;
            setTimeout(function() { StatsPageModule.loadAll(); }, 0);
            this.startAutoRefresh();
        },

        setupEvents: function() {
            // Time range buttons
            document.querySelectorAll('input[name="timeRange"]').forEach(radio => {
                radio.addEventListener('change', (e) => {
                    this.currentRange = parseInt(e.target.value);
                    this.loadTrafficData();
                });
            });

            // Refresh button
            const refreshBtn = document.getElementById('stats-refresh-btn');
            if (refreshBtn) {
                refreshBtn.addEventListener('click', () => {
                    this.loadAll();
                });
            }
        },

        loadAll: async function() {
            const setEl = (id, val) => {
                const el = document.getElementById(id);
                if (el) el.textContent = val ?? '-';
            };

            try {
                const [stats, traffic] = await Promise.all([
                    DashboardApi.endpoints.getStats().catch(() => null),
                    DashboardApi.endpoints.getTrafficData(this.currentRange).catch(() => null)
                ]);

                if (stats) {
                    setEl('stat-total-requests', (stats.totalRequests || 0).toLocaleString());
                    setEl('stat-success-rate', (stats.successRate || 0) + '%');
                    setEl('stat-avg-latency', (stats.avgLatency || 0) + 'ms');
                    setEl('stat-rpm', stats.requestsPerMin || '0');

                    this.renderLatencyChart(stats);
                    this.renderStatusChart(stats);
                    this.renderTopRoutes(stats.topRoutes || [], stats.totalRequests || 0);
                    this.renderTopClusters(stats.topClusters || [], stats.totalRequests || 0);
                } else {
                    setEl('stat-total-requests', '-');
                    setEl('stat-success-rate', '-');
                    setEl('stat-avg-latency', '-');
                    setEl('stat-rpm', '-');
                }

                if (traffic) {
                    this.renderQpsChart(traffic);
                    this.renderErrorChart(traffic);
                }

                const now = new Date();
                const timeStr = now.toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
                setEl('stats-last-updated', __('stats.updatedAt') + ' ' + timeStr);

            } catch (e) {
                console.error('[StatsPage] Load failed:', e);
            }
        },

        loadTrafficData: async function() {
            try {
                const traffic = await DashboardApi.endpoints.getTrafficData(this.currentRange);
                if (traffic) {
                    this.renderQpsChart(traffic);
                    this.renderErrorChart(traffic);
                }
            } catch (e) {
                console.error('[StatsPage] Traffic data failed:', e);
            }
        },

        startAutoRefresh: function() {
            this.stopAutoRefresh();
            this.refreshInterval = setInterval(() => {
                this.loadAll();
            }, 60000);
        },

        stopAutoRefresh: function() {
            if (this.refreshInterval) {
                clearInterval(this.refreshInterval);
                this.refreshInterval = null;
            }
        },

        renderQpsChart: function(data) {
            const ctx = document.getElementById('qps-chart');
            if (!ctx) return;
            if (this.qpsChart) this.qpsChart.destroy();

            if (!data || !data.labels || data.labels.length === 0) {
                ctx.parentElement.innerHTML = '<div class="text-center text-muted py-5">' + __('stats.noTraffic') + '</div>';
                return;
            }

            this.qpsChart = new Chart(ctx, {
                type: 'line',
                data: {
                    labels: this.formatLabels(data.labels),
                    datasets: [{
                        label: 'QPS',
                        data: data.qps || [],
                        borderColor: '#6366f1',
                        backgroundColor: 'rgba(99, 102, 241, 0.08)',
                        fill: true,
                        tension: 0.4,
                        pointRadius: 0,
                        pointHoverRadius: 5,
                        borderWidth: 2
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    interaction: { mode: 'index', intersect: false },
                    plugins: {
                        legend: { display: false },
                        tooltip: {
                            backgroundColor: 'rgba(0,0,0,0.75)',
                            titleFont: { size: 11 },
                            bodyFont: { size: 11 },
                            padding: 8,
                            callbacks: {
                                label: ctx => ' QPS: ' + (ctx.parsed.y || 0).toFixed(1)
                            }
                        }
                    },
                    scales: {
                        x: {
                            grid: { display: false },
                            ticks: {
                                maxTicksLimit: 8,
                                font: { size: 10 },
                                color: '#9ca3af',
                                maxRotation: 0
                            }
                        },
                        y: {
                            beginAtZero: true,
                            grid: { color: 'rgba(0,0,0,0.05)' },
                            ticks: { font: { size: 10 }, color: '#9ca3af' }
                        }
                    }
                }
            });
        },

        renderLatencyChart: function(data) {
            const ctx = document.getElementById('latency-chart');
            if (!ctx) return;
            if (this.latencyChart) this.latencyChart.destroy();

            const p50 = data.p50 || 0;
            const p90 = data.p90 || 0;
            const p99 = data.p99 || 0;

            if (p50 === 0 && p90 === 0 && p99 === 0) {
                ctx.parentElement.innerHTML = '<div class="text-center text-muted py-5">' + __('stats.noLatency') + '</div>';
                return;
            }

            this.latencyChart = new Chart(ctx, {
                type: 'bar',
                data: {
                    labels: ['P50', 'P90', 'P99'],
                    datasets: [{
                        label: '\u5ef6\u8fdf (ms)',
                        data: [p50, p90, p99],
                        backgroundColor: ['rgba(34,197,94,0.7)', 'rgba(245,158,11,0.7)', 'rgba(239,68,68,0.7)'],
                        borderColor: ['#22c55e', '#f59e0b', '#ef4444'],
                        borderWidth: 1,
                        borderRadius: 6,
                        barThickness: 50
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: {
                        legend: { display: false },
                        tooltip: {
                            backgroundColor: 'rgba(0,0,0,0.75)',
                            callbacks: {
                                label: ctx => ' ' + ctx.parsed.y + ' ms'
                            }
                        }
                    },
                    scales: {
                        x: {
                            grid: { display: false },
                            ticks: { font: { size: 11 }, color: '#6b7280' }
                        },
                        y: {
                            beginAtZero: true,
                            grid: { color: 'rgba(0,0,0,0.05)' },
                            ticks: {
                                font: { size: 10 },
                                color: '#9ca3af',
                                callback: v => v + ' ms'
                            }
                        }
                    }
                }
            });
        },

        renderErrorChart: function(data) {
            const ctx = document.getElementById('error-chart');
            if (!ctx) return;
            if (this.errorChart) this.errorChart.destroy();

            if (!data || !data.labels || data.labels.length === 0) {
                ctx.parentElement.innerHTML = '<div class="text-center text-muted py-5">' + __('stats.noErrors') + '</div>';
                return;
            }

            const qps = data.qps || [];
            const errors = data.errors || [];
            const errorRates = qps.map((q, i) => {
                if (q === 0) return 0;
                return parseFloat(((errors[i] || 0) / q * 100).toFixed(2));
            });

            // Update error rate badge
            const badge = document.getElementById('error-rate-badge');
            if (badge) {
                const avgErr = errorRates.length
                    ? (errorRates.reduce((a, b) => a + b, 0) / errorRates.length).toFixed(2)
                    : 0;
                badge.textContent = avgErr + '%';
                badge.style.display = 'inline-block';
                badge.className = avgErr > 5 ? 'badge bg-danger small' : avgErr > 1 ? 'badge bg-warning small' : 'badge bg-success small';
            }

            this.errorChart = new Chart(ctx, {
                type: 'line',
                data: {
                    labels: this.formatLabels(data.labels),
                    datasets: [{
                        label: '\u9519\u8bef\u7387 (%)',
                        data: errorRates,
                        borderColor: '#ef4444',
                        backgroundColor: 'rgba(239,68,68,0.06)',
                        fill: true,
                        tension: 0.4,
                        pointRadius: 0,
                        borderWidth: 2
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    interaction: { mode: 'index', intersect: false },
                    plugins: {
                        legend: { display: false },
                        tooltip: {
                            backgroundColor: 'rgba(0,0,0,0.75)',
                            callbacks: {
                                label: ctx => ' ' + (ctx.parsed.y || 0).toFixed(2) + '%'
                            }
                        }
                    },
                    scales: {
                        x: {
                            grid: { display: false },
                            ticks: {
                                maxTicksLimit: 8,
                                font: { size: 10 },
                                color: '#9ca3af',
                                maxRotation: 0
                            }
                        },
                        y: {
                            beginAtZero: true,
                            max: 100,
                            grid: { color: 'rgba(0,0,0,0.05)' },
                            ticks: {
                                font: { size: 10 },
                                color: '#9ca3af',
                                callback: v => v + '%'
                            }
                        }
                    }
                }
            });
        },

        renderStatusChart: function(data) {
            const ctx = document.getElementById('status-chart');
            if (!ctx) return;
            if (this.statusChart) this.statusChart.destroy();

            const codes = data.statusCodes || [];
            if (!codes.length) {
                ctx.parentElement.innerHTML = '<div class="text-center text-muted py-5">' + __('stats.noStatusCodes') + '</div>';
                return;
            }

            let count2xx = 0, count3xx = 0, count4xx = 0, count5xx = 0;
            codes.forEach(item => {
                const code = item.code || 0;
                const count = item.count || 0;
                if (code >= 200 && code < 300) count2xx += count;
                else if (code >= 300 && code < 400) count3xx += count;
                else if (code >= 400 && code < 500) count4xx += count;
                else if (code >= 500) count5xx += count;
            });

            const total = count2xx + count3xx + count4xx + count5xx;
            if (total === 0) {
                ctx.parentElement.innerHTML = '<div class="text-center text-muted py-5">' + __('stats.noStatusCodes') + '</div>';
                return;
            }

            this.statusChart = new Chart(ctx, {
                type: 'doughnut',
                data: {
                    labels: ['2xx \u6210\u529f', '3xx \u91cd\u5b9a\u5411', '4xx \u5ba2\u6237\u7aef\u9519\u8bef', '5xx \u670d\u52a1\u7aef\u9519\u8bef'],
                    datasets: [{
                        data: [count2xx, count3xx, count4xx, count5xx],
                        backgroundColor: ['#22c55e', '#3b82f6', '#f59e0b', '#ef4444'],
                        borderWidth: 0,
                        hoverOffset: 6
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    cutout: '65%',
                    plugins: {
                        legend: {
                            position: 'bottom',
                            labels: {
                                boxWidth: 10,
                                padding: 10,
                                font: { size: 11 },
                                color: '#6b7280',
                                generateLabels: (chart) => {
                                    const d = chart.data;
                                    return d.labels.map((label, i) => ({
                                        text: label + ' ' + d.datasets[0].data[i].toLocaleString(),
                                        fillStyle: d.datasets[0].backgroundColor[i],
                                        hidden: false,
                                        index: i
                                    }));
                                }
                            }
                        },
                        tooltip: {
                            backgroundColor: 'rgba(0,0,0,0.75)',
                            callbacks: {
                                label: ctx => {
                                    const val = ctx.parsed || 0;
                                    const pct = total > 0 ? ((val / total) * 100).toFixed(1) : 0;
                                    return ' ' + val.toLocaleString() + ' (' + pct + '%)';
                                }
                            }
                        }
                    }
                }
            });
        },

        renderTopRoutes: function(routes, total) {
            const container = document.getElementById('top-routes-list');
            const countBadge = document.getElementById('top-routes-count');
            if (!container) return;
            if (countBadge) countBadge.textContent = routes.length + ' ' + __('stats.routesUnit');

            if (!routes.length) {
                container.innerHTML = '<div class="text-center text-muted py-4 small">' + __('stats.noRoutes') + '</div>';
                return;
            }

            const maxCount = routes[0].count || 1;
            container.innerHTML = routes.slice(0, 10).map((r, i) => {
                const name = (r.name || r.route || 'unknown');
                const pct = total > 0 ? ((r.count / total) * 100).toFixed(1) : 0;
                const barPct = maxCount > 0 ? ((r.count / maxCount) * 100).toFixed(0) : 0;
                const rankClass = i === 0 ? 'top-1' : i === 1 ? 'top-2' : i === 2 ? 'top-3' : '';
                return `<div class="stats-list-row">
                    <div class="stats-list-rank ${rankClass}">${i + 1}</div>
                    <div class="stats-list-name" title="${this.escHtml(name)}">${this.escHtml(name)}</div>
                    <div class="stats-list-bar-wrap"><div class="stats-list-bar" style="width:${barPct}%"></div></div>
                    <div class="stats-list-value">${r.count.toLocaleString()}<br><span style="font-size:9px;color:#d1d5db">${pct}%</span></div>
                </div>`;
            }).join('');
        },

        renderTopClusters: function(clusters, total) {
            const container = document.getElementById('top-clusters-list');
            const countBadge = document.getElementById('top-clusters-count');
            if (!container) return;
            if (countBadge) countBadge.textContent = clusters.length + ' ' + __('stats.clustersUnit');

            if (!clusters.length) {
                container.innerHTML = '<div class="text-center text-muted py-4 small">' + __('stats.noClusters') + '</div>';
                return;
            }

            const maxCount = clusters[0].count || 1;
            container.innerHTML = clusters.slice(0, 10).map((c, i) => {
                const name = (c.name || c.cluster || 'unknown');
                const pct = total > 0 ? ((c.count / total) * 100).toFixed(1) : 0;
                const barPct = maxCount > 0 ? ((c.count / maxCount) * 100).toFixed(0) : 0;
                const rankClass = i === 0 ? 'top-1' : i === 1 ? 'top-2' : i === 2 ? 'top-3' : '';
                return `<div class="stats-list-row">
                    <div class="stats-list-rank ${rankClass}">${i + 1}</div>
                    <div class="stats-list-name" title="${this.escHtml(name)}">${this.escHtml(name)}</div>
                    <div class="stats-list-bar-wrap"><div class="stats-list-bar" style="width:${barPct}%;background:#06b6d4"></div></div>
                    <div class="stats-list-value">${c.count.toLocaleString()}<br><span style="font-size:9px;color:#d1d5db">${pct}%</span></div>
                </div>`;
            }).join('');
        },

        escHtml: function(str) {
            const div = document.createElement('div');
            div.textContent = str;
            return div.innerHTML;
        },

        formatLabels: function(labels) {
            if (!labels || !labels.length) return [];
            if (typeof labels[0] === 'string' && labels[0].includes('T')) {
                return labels.map(l => {
                    try {
                        const d = new Date(l);
                        return d.toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit', hour12: false });
                    } catch { return l; }
                });
            }
            if (typeof labels[0] === 'number') {
                const now = new Date();
                return labels.map(m => {
                    const d = new Date(now.getTime() - m * 60000);
                    return d.toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit', hour12: false });
                }).reverse();
            }
            return labels;
        },

        destroy: function() {
            this.stopAutoRefresh();
            [this.qpsChart, this.latencyChart, this.errorChart, this.statusChart].forEach(c => c && c.destroy());
        }
    };

    window.StatsPageModule = StatsPageModule;
})();
