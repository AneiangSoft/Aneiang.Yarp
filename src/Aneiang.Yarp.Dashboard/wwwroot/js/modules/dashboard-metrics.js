/**
 * Dashboard Metrics Module - Prometheus metrics viewer with parsed display
 */
(function() {
    'use strict';

    var MetricsModule = {
        name: 'metrics',
        initialized: false,
        rawText: '',
        parsedMetrics: null,

        init: async function() {
            if (this.initialized) return;
            this.setupEvents();
            this.initialized = true;
        },

        loadMetrics: async function() {
            try {
                var container = window.DashboardDOM.safe('#metrics-content');
                if (!container) return;

                window.DashboardDOM.showLoading(container, __('metrics.loading'));

                var response = await window.DashboardApi.endpoints.getMetrics();
                this.rawText = await response.text();
                this.parsedMetrics = this.parsePrometheusText(this.rawText);
                this.renderMetrics(container);
            } catch (error) {
                console.error('[Metrics] Load failed:', error);
                var container = window.DashboardDOM.safe('#metrics-content');
                if (container) {
                    if (error.message && error.message.indexOf('404') >= 0) {
                        window.DashboardDOM.showError(container, __('metrics.notEnabled'));
                    } else {
                        window.DashboardDOM.showError(container, __('metrics.loadFailed'));
                    }
                }
            }
        },

        parsePrometheusText: function(text) {
            var metrics = [];
            var currentMetric = null;
            var lines = text.split('\n');

            lines.forEach(function(line) {
                if (line.startsWith('# HELP ')) {
                    var helpParts = line.substring(7).split(' ');
                    var name = helpParts[0];
                    var help = helpParts.slice(1).join(' ');
                    if (!currentMetric || currentMetric.name !== name) {
                        currentMetric = { name: name, help: help, type: '', samples: [] };
                        metrics.push(currentMetric);
                    } else {
                        currentMetric.help = help;
                    }
                } else if (line.startsWith('# TYPE ')) {
                    var typeParts = line.substring(7).split(' ');
                    var typeName = typeParts[0];
                    var type = typeParts[1];
                    if (currentMetric && currentMetric.name === typeName) {
                        currentMetric.type = type;
                    } else {
                        currentMetric = { name: typeName, help: '', type: type, samples: [] };
                        metrics.push(currentMetric);
                    }
                } else if (line.trim() && !line.startsWith('#')) {
                    if (!currentMetric) {
                        currentMetric = { name: 'unknown', help: '', type: '', samples: [] };
                        metrics.push(currentMetric);
                    }
                    currentMetric.samples.push(line.trim());
                }
            });

            return metrics;
        },

        renderMetrics: function(container) {
            window.DashboardDOM.clear(container);

            if (!this.parsedMetrics || this.parsedMetrics.length === 0) {
                container.innerHTML =
                    '<div class="text-center py-5">' +
                        '<i class="bi bi-graph-up text-muted" style="font-size:48px;"></i>' +
                        '<p class="text-muted mt-3">' + __('metrics.noData') + '</p>' +
                    '</div>';
                return;
            }

            var html = '';

            // Summary cards
            var totalSamples = 0;
            this.parsedMetrics.forEach(function(m) { totalSamples += m.samples.length; });

            html += '<div class="row g-3 mb-4">';
            html += '<div class="col-md-4"><div class="stat-mini-card"><div class="stat-mini-value">' + this.parsedMetrics.length + '</div><div class="stat-mini-label">' + __('metrics.metricCount') + '</div></div></div>';
            html += '<div class="col-md-4"><div class="stat-mini-card"><div class="stat-mini-value">' + totalSamples + '</div><div class="stat-mini-label">' + __('metrics.sampleCount') + '</div></div></div>';
            html += '<div class="col-md-4"><div class="stat-mini-card"><div class="stat-mini-value">' + (this.rawText.length / 1024).toFixed(1) + 'KB</div><div class="stat-mini-label">' + __('metrics.payloadSize') + '</div></div></div>';
            html += '</div>';

            // Parsed metric cards
            this.parsedMetrics.forEach(function(metric) {
                var typeBadge = metric.type
                    ? '<span class="badge bg-info me-2" style="font-size:10px">' + metric.type + '</span>'
                    : '';

                html += '<div class="card-panel mb-3">';
                html += '<div class="card-header">';
                html += '<span>' + typeBadge + '<code style="font-size:13px;color:#4338ca">' + window.DashboardUtils.escapeHtml(metric.name) + '</code>';
                if (metric.help) {
                    html += '<small class="text-muted ms-2">— ' + window.DashboardUtils.escapeHtml(metric.help) + '</small>';
                }
                html += '</span>';
                html += '<span class="badge bg-secondary" style="font-size:10px">' + metric.samples.length + ' ' + __('metrics.samples') + '</span>';
                html += '</div>';

                if (metric.samples.length > 0) {
                    html += '<div class="card-body" style="max-height:200px;overflow-y:auto;padding:12px 16px;">';
                    html += '<pre style="margin:0;font-size:12px;line-height:1.6;color:#334155;white-space:pre-wrap;word-break:break-all;">';
                    metric.samples.forEach(function(s) {
                        html += window.DashboardUtils.escapeHtml(s) + '\n';
                    });
                    html += '</pre></div>';
                }

                html += '</div>';
            });

            // Raw text toggle
            html += '<details class="mt-3">';
            html += '<summary style="cursor:pointer;color:#94a3b8;font-size:12px;display:inline-flex;align-items:center;gap:4px;">' +
                     '<i class="bi bi-code-slash"></i> ' + __('metrics.viewRaw') + '</summary>';
            html += '<div class="detail-raw-json" style="background:#1e293b;color:#e2e8f0;padding:12px 16px;border-radius:8px;margin-top:6px;font-size:12px;line-height:1.6;max-height:400px;overflow:auto;white-space:pre-wrap;word-break:break-all;">' +
                     window.DashboardUtils.escapeHtml(this.rawText) + '</div>';
            html += '</details>';

            container.innerHTML = html;
        },

        setupEvents: function() {
            document.addEventListener('dashboard:shortcut:refresh', function() { MetricsModule.loadMetrics(); });
            document.addEventListener('dashboard:localeChange', function() { MetricsModule.loadMetrics(); });
        }
    };

    if (window.DashboardApp) {
        window.DashboardApp.registerModule('metrics', MetricsModule);
    }
    window.MetricsModule = MetricsModule;
})();
