/**
 * Feature Catalog - Displays YARP gateway features in a card grid.
 */
(function() {
    'use strict';

    window.DashboardFeatures = {
        containerId: 'features-grid',

        init: function(containerId) {
            this.containerId = containerId || 'features-grid';
            this.load();
        },

        load: function() {
            var self = this;
            var prefix = window.__routePrefix || 'apigateway';

            fetch('/' + prefix + '/api/features')
                .then(function(res) { return res.json(); })
                .then(function(json) {
                    if (json.success !== false && json.data) {
                        self._render(json.data);
                    } else {
                        self._renderError(json.message);
                    }
                })
                .catch(function(err) { self._renderError(err.message); });
        },

        _render: function(features) {
            var container = document.getElementById(this.containerId);
            if (!container) return;

            if (!features || features.length === 0) {
                container.innerHTML = '<div class="col-12 text-center text-muted py-4">No features available.</div>';
                return;
            }

            var html = '';
            features.forEach(function(f) {
                var keyPointsHtml = (f.keyPoints || []).map(function(kp) {
                    return '<span class="badge bg-light text-dark me-1 mb-1">' + self._esc(kp) + '</span>';
                }).join('');

                var exampleHtml = f.exampleConfig
                    ? '<button class="btn btn-sm btn-outline-info" onclick="DashboardFeatures._toggleExample(\'' + f.id + '\')"><i class="bi bi-code-slash"></i> 示例</button>'
                    : '';

                var configBtn = f.configPageUrl
                    ? '<a href="' + f.configPageUrl + '" class="btn btn-sm btn-primary"><i class="bi bi-gear"></i> 配置</a>'
                    : '';

                var docBtn = f.docUrl
                    ? '<a href="' + f.docUrl + '" target="_blank" class="btn btn-sm btn-outline-secondary"><i class="bi bi-book"></i></a>'
                    : '';

                var aiBtn = '<button class="btn btn-sm btn-outline-info" onclick="DashboardFeatures._askAI(\'' + f.id + '\', \'' + self._esc(f.name).replace(/'/g, "\\'") + '\')"><i class="bi bi-robot"></i> 问问 AI</button>';

                var pluginBadge = f.isPlugin
                    ? '<span class="badge bg-warning text-dark">插件</span>'
                    : '';

                html += '<div class="col-lg-4 col-md-6">' +
                    '<div class="card h-100">' +
                    '<div class="card-body">' +
                    '<div class="d-flex align-items-center gap-2 mb-2">' +
                    '<div class="feature-icon" style="width:40px;height:40px;border-radius:8px;background:#eef2ff;display:flex;align-items:center;justify-content:center;">' +
                    '<i class="bi ' + f.icon + '" style="font-size:20px;color:#6366f1;"></i></div>' +
                    '<div><h6 class="mb-0">' + self._esc(f.name) + '</h6>' +
                    '<small class="text-muted">' + self._esc(f.category) + '</small></div>' +
                    pluginBadge +
                    '</div>' +
                    '<p class="small text-muted mb-2">' + self._esc(f.summary) + '</p>' +
                    '<div class="mb-2">' + keyPointsHtml + '</div>' +
                    '<div class="d-flex gap-2 mt-3 flex-wrap">' + exampleHtml + configBtn + aiBtn + docBtn + '</div>' +
                    '<div class="mt-2" id="example-' + f.id + '" style="display:none;">' +
                    '<pre class="small bg-dark text-light p-2 rounded mt-2" style="max-height:200px;overflow:auto;"><code>' + self._esc(f.exampleConfig || '') + '</code></pre>' +
                    '</div>' +
                    '</div>' +
                    '</div>' +
                    '</div>';
            });

            container.innerHTML = html;
        },

        _toggleExample: function(id) {
            var el = document.getElementById('example-' + id);
            if (el) el.style.display = el.style.display === 'none' ? 'block' : 'none';
        },

        _askAI: function(featureId, featureName) {
            // Open AI chat panel and pre-fill a question about this feature
            if (window.AIChat) {
                window.AIChat.toggle();
                // Pre-fill the input with a question about this feature
                setTimeout(function() {
                    var input = document.querySelector('.ai-chat-input textarea, .ai-input textarea, #ai-chat-input');
                    if (input) {
                        var question = '请详细介绍"' + featureName + '"功能的配置方法和最佳实践';
                        if (input.tagName === 'TEXTAREA' || input.tagName === 'INPUT') {
                            input.value = question;
                            input.dispatchEvent(new Event('input'));
                            input.focus();
                        }
                    } else if (window.AIChat.sendQuickAction) {
                        window.AIChat.sendQuickAction('请详细介绍"' + featureName + '"功能的配置方法和最佳实践');
                    }
                }, 300);
            } else {
                window.location.href = '/apigateway/ai-settings';
            }
        },

        _renderError: function(msg) {
            var container = document.getElementById(this.containerId);
            if (!container) return;
            container.innerHTML = '<div class="col-12 text-center text-danger py-4"><i class="bi bi-exclamation-triangle"></i> ' + this._esc(msg || 'Error') + '</div>';
        },

        _esc: function(s) {
            if (!s) return '';
            var d = document.createElement('div');
            d.textContent = s;
            return d.innerHTML;
        }
    };

    var self = window.DashboardFeatures;
})();
