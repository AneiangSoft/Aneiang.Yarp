/**
 * Traffic Monitor module for the dashboard.
 * Fetches aggregated traffic statistics from /api/traffic/stats and renders charts.
 */
(function () {
    'use strict';

    var TrafficModule = {
        _charts: {},
        _refreshTimer: null,
        _minutes: 60,

        init: function () {
            var self = this;
            if (window.DashboardApi) DashboardApi.init();

            // Time range buttons
            document.querySelectorAll('input[name="timeRange"]').forEach(function (radio) {
                radio.addEventListener('change', function () {
                    self._minutes = parseInt(this.value, 10);
                    self.loadData();
                });
            });

            // Refresh button
            var refreshBtn = document.getElementById('stats-refresh-btn');
            if (refreshBtn) {
                refreshBtn.addEventListener('click', function () { self.loadData(); });
            }

            // Auto-refresh every 30s
            self.loadData();
            self._refreshTimer = setInterval(function () { self.loadData(); }, 30000);
        },

        loadData: function () {
            var self = this;
            var prefix = (window.__dashboard && window.__dashboard.routePrefix) || 'apigateway';
            var url = '/' + prefix + '/api/traffic/stats?minutes=' + this._minutes;

            fetch(url)
                .then(function (r) { return r.json(); })
                .then(function (res) {
                    if (res.code === 200 && res.data) {
                        self.renderData(res.data);
                    }
                })
                .catch(function (err) {
                    console.error('[Traffic] Failed to load stats:', err);
                });
        },

        renderData: function (data) {
            // Summary cards
            this._setText('stat-total-requests', data.totalRequests || 0);
            this._setText('stat-success-rate', (data.successRate || 0) + '%');
            this._setText('stat-avg-latency', (data.avgLatency || 0) + ' ms');
            this._setText('stat-rpm', data.rpm || 0);

            // Last updated
            var updated = document.getElementById('stats-last-updated');
            if (updated) updated.textContent = new Date().toLocaleTimeString();

            // QPS chart
            this._renderLineChart('qps-chart', data.buckets || [], 'Requests');

            // Status code pie
            this._renderPieChart('status-chart', data.statusCodes || {});

            // Latency chart
            this._renderLatencyChart('latency-chart', data.percentiles || {});

            // Error rate chart
            this._renderErrorChart('error-chart', data.buckets || []);

            // Top routes
            this._renderTopList('top-routes-list', 'top-routes-count', data.topRoutes || []);

            // Top clusters
            this._renderTopList('top-clusters-list', 'top-clusters-count', data.topClusters || []);
        },

        _setText: function (id, value) {
            var el = document.getElementById(id);
            if (el) el.textContent = value;
        },

        _getChart: function (id) {
            var canvas = document.getElementById(id);
            if (!canvas) return null;
            if (this._charts[id]) return this._charts[id];
            var ctx = canvas.getContext('2d');
            this._charts[id] = new Chart(ctx, {
                type: 'line',
                data: { labels: [], datasets: [] },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: { legend: { display: false } },
                    scales: {
                        x: { grid: { display: false }, ticks: { font: { size: 10 } } },
                        y: { beginAtZero: true, grid: { color: 'rgba(0,0,0,0.05)' }, ticks: { font: { size: 10 } } }
                    }
                }
            });
            return this._charts[id];
        },

        _renderLineChart: function (id, buckets, label) {
            var chart = this._getChart(id);
            if (!chart) return;
            chart.data.labels = buckets.map(function (b) { return b.time; });
            chart.data.datasets = [{
                label: label,
                data: buckets.map(function (b) { return b.requests; }),
                borderColor: '#6366f1',
                backgroundColor: 'rgba(99,102,241,0.1)',
                fill: true,
                tension: 0.3,
                pointRadius: 2
            }];
            chart.update('none');
        },

        _renderPieChart: function (id, statusCodes) {
            var canvas = document.getElementById(id);
            if (!canvas) return;
            if (this._charts[id]) { this._charts[id].destroy(); delete this._charts[id]; }
            var ctx = canvas.getContext('2d');
            var labels = Object.keys(statusCodes);
            var values = labels.map(function (k) { return statusCodes[k]; });
            var colors = { '2xx': '#22c55e', '3xx': '#3b82f6', '4xx': '#f59e0b', '5xx': '#ef4444' };
            this._charts[id] = new Chart(ctx, {
                type: 'doughnut',
                data: {
                    labels: labels,
                    datasets: [{
                        data: values,
                        backgroundColor: labels.map(function (k) { return colors[k] || '#9ca3af'; })
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: { legend: { position: 'bottom', labels: { font: { size: 10 } } } }
                }
            });
        },

        _renderLatencyChart: function (id, percentiles) {
            var chart = this._getChart(id);
            if (!chart) return;
            chart.data.labels = ['P50', 'P90', 'P99'];
            chart.data.datasets = [{
                label: 'Latency (ms)',
                data: [percentiles.p50 || 0, percentiles.p90 || 0, percentiles.p99 || 0],
                backgroundColor: ['#22c55e', '#f59e0b', '#ef4444'],
                barThickness: 40
            }];
            chart.config.options.scales.y.beginAtZero = true;
            chart.update('none');
        },

        _renderErrorChart: function (id, buckets) {
            var chart = this._getChart(id);
            if (!chart) return;
            chart.data.labels = buckets.map(function (b) { return b.time; });
            chart.data.datasets = [{
                label: 'Errors',
                data: buckets.map(function (b) {
                    return b.requests > 0 ? Math.round(b.errors / b.requests * 1000) / 10 : 0;
                }),
                borderColor: '#ef4444',
                backgroundColor: 'rgba(239,68,68,0.1)',
                fill: true,
                tension: 0.3,
                pointRadius: 2
            }];
            chart.update('none');

            var badge = document.getElementById('error-rate-badge');
            if (badge) {
                var lastError = buckets.length > 0 ? buckets[buckets.length - 1] : null;
                var rate = lastError && lastError.requests > 0 ? Math.round(lastError.errors / lastError.requests * 1000) / 10 : 0;
                badge.textContent = rate + '%';
                badge.style.display = rate > 0 ? '' : 'none';
            }
        },

        _renderTopList: function (listId, countId, items) {
            var list = document.getElementById(listId);
            if (!list) return;
            var countEl = document.getElementById(countId);
            if (countEl) countEl.textContent = items.length;

            if (items.length === 0) {
                list.innerHTML = '<div class="text-muted text-center py-3 small">' + (window.__ ? __('common.noData') : 'No data') + '</div>';
                return;
            }

            var max = items[0].count || 1;
            var escapeHtml = this._escapeHtml;
            list.innerHTML = items.map(function (item, i) {
                var rank = i + 1;
                var rankClass = rank <= 3 ? 'top-' + rank : '';
                var barWidth = Math.round(item.count / max * 100);
                return '<div class="stats-list-row">' +
                    '<div class="stats-list-rank ' + rankClass + '">' + rank + '</div>' +
                    '<div class="stats-list-name">' + escapeHtml(item.name) + '</div>' +
                    '<div class="stats-list-bar-wrap"><div class="stats-list-bar" style="width:' + barWidth + '%"></div></div>' +
                    '<div class="stats-list-value">' + item.count + ' · ' + Math.round(item.avgLatency) + 'ms</div>' +
                    '</div>';
            }).join('');
        },

        _escapeHtml: function (str) {
            if (!str) return '';
            return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
        }
    };

    window.TrafficModule = TrafficModule;
})();
