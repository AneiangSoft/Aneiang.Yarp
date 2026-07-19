/**
 * Transform Guide - Displays YARP Transform documentation.
 */
(function() {
    'use strict';

    window.DashboardTransformGuide = {
        containerId: 'transform-guide',

        init: function(containerId) {
            this.containerId = containerId || 'transform-guide';
            this.load();
        },

        load: function() {
            var self = this;
            var prefix = window.__routePrefix || 'apigateway';

            fetch('/' + prefix + '/api/config/knowledge/transforms')
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

        _render: function(topic) {
            var container = document.getElementById(this.containerId);
            if (!container) return;

            // Parse key points into categorized transform cards
            var categories = {
                '路径转换': ['PathRemovePrefix', 'PathSet', 'PathPrefix', 'PathPattern'],
                '请求头转换': ['RequestHeader', 'RequestHeaderRouteValue', 'RequestHeadersAllowed', 'RequestHeaderRemove', 'RequestHeadersCopy', 'RequestHeaderOriginalHost'],
                '响应头转换': ['ResponseHeader', 'ResponseHeaderRemove', 'ResponseHeadersCopy', 'ResponseHeadersAllowed', 'ResponseTrailer', 'ResponseTrailerRemove', 'ResponseTrailersCopy', 'ResponseTrailersAllowed'],
                '转发头': ['X-Forwarded', 'Forwarded', 'ClientCert'],
                '查询参数': ['QueryValueParameter', 'QueryRemoveParameter', 'QueryRouteParameter'],
                'HTTP 方法': ['HttpMethodChange']
            };

            var html = '<div class="col-12">' +
                '<div class="alert alert-info">' +
                '<i class="bi bi-info-circle"></i> ' + this._esc(topic.summary) +
                '</div>' +
                '</div>';

            // Render categories
            Object.keys(categories).forEach(function(cat) {
                var transforms = categories[cat];
                html += '<div class="col-12"><h6 class="mt-3 mb-2"><i class="bi bi-folder2-open text-primary"></i> ' + cat + '</h6></div>';

                transforms.forEach(function(name) {
                    var desc = self._getTransformDesc(name);
                    var example = self._getTransformExample(name);
                    var ba = self._getBeforeAfter(name);
                    var pairings = self._getPairingSuggestions(name);
                    var baHtml = ba ? '<div class="small mt-1">' +
                        '<span class="text-danger">Before: </span><code>' + self._esc(ba.before) + '</code><br>' +
                        '<span class="text-success">After: </span><code>' + self._esc(ba.after) + '</code>' +
                        '</div>' : '';
                    var pairingHtml = pairings.length > 0
                        ? '<div class="small text-muted mt-1">🔗 常见搭配: ' +
                          pairings.map(function(p) { return '<span class="badge bg-light text-dark">' + p + '</span>'; }).join(' ') +
                          '</div>'
                        : '';
                    html += '<div class="col-lg-6 col-md-12">' +
                        '<div class="card mb-2">' +
                        '<div class="card-body p-3">' +
                        '<div class="d-flex justify-content-between align-items-center">' +
                        '<code class="fw-semibold text-primary">' + name + '</code>' +
                        '<div class="btn-group btn-group-sm">' +
                        '<button class="btn btn-outline-info py-0" onclick="DashboardTransformGuide._toggleCode(\'' + name + '\')"><i class="bi bi-code-slash"></i></button>' +
                        '<button class="btn btn-outline-success py-0" onclick="DashboardTransformGuide._applyToRoute(\'' + name + '\', ' + encodeURIComponent(JSON.stringify(example)) + ')"><i class="bi bi-plus-circle"></i> 应用</button>' +
                        '</div>' +
                        '</div>' +
                        '<p class="small text-muted mt-1 mb-0">' + desc + '</p>' +
                        baHtml +
                        pairingHtml +
                        '<div id="code-' + name + '" style="display:none;" class="mt-2">' +
                        '<pre class="small bg-dark text-light p-2 rounded mb-0"><code>' + example + '</code></pre>' +
                        '</div>' +
                        '</div>' +
                        '</div>' +
                        '</div>';
                });
            });

            // Render best practices
            if (topic.bestPractices && topic.bestPractices.length > 0) {
                html += '<div class="col-12 mt-3">' +
                    '<div class="card border-success">' +
                    '<div class="card-header bg-success text-white"><i class="bi bi-lightbulb"></i> 最佳实践</div>' +
                    '<div class="card-body"><ul class="mb-0">';
                topic.bestPractices.forEach(function(bp) {
                    html += '<li class="small">' + self._esc(bp) + '</li>';
                });
                html += '</ul></div></div></div>';
            }

            // Render common mistakes
            if (topic.commonMistakes && topic.commonMistakes.length > 0) {
                html += '<div class="col-12 mt-2">' +
                    '<div class="card border-warning">' +
                    '<div class="card-header bg-warning text-dark"><i class="bi bi-exclamation-triangle"></i> 常见错误</div>' +
                    '<div class="card-body"><ul class="mb-0">';
                topic.commonMistakes.forEach(function(m) {
                    html += '<li class="small">' + self._esc(m) + '</li>';
                });
                html += '</ul></div></div></div>';
            }

            // Doc link
            if (topic.docUrl) {
                html += '<div class="col-12 mt-2 text-center">' +
                    '<a href="' + topic.docUrl + '" target="_blank" class="btn btn-outline-primary"><i class="bi bi-book"></i> 官方文档</a>' +
                    '</div>';
            }

            container.innerHTML = html;
        },

        _getTransformDesc: function(name) {
            var descs = {
                'PathRemovePrefix': '移除请求路径的前缀部分',
                'PathSet': '将请求路径设置为指定值',
                'PathPrefix': '在请求路径前添加前缀',
                'PathPattern': '使用模板模式替换路径（推荐）',
                'RequestHeader': '添加、修改或移除请求头',
                'RequestHeaderRouteValue': '从路由值获取并设置请求头',
                'RequestHeadersAllowed': '指定允许转发的请求头列表',
                'RequestHeaderRemove': '移除指定的请求头',
                'RequestHeadersCopy': '控制是否将请求头复制到出站请求',
                'RequestHeaderOriginalHost': '控制是否保留原始 Host 头',
                'ResponseHeader': '添加或修改响应头',
                'ResponseHeaderRemove': '移除指定的响应头',
                'ResponseHeadersCopy': '控制是否将响应头复制回客户端',
                'ResponseHeadersAllowed': '指定允许转发的响应头列表',
                'ResponseTrailer': '添加 HTTP Trailer',
                'ResponseTrailerRemove': '移除指定的响应 Trailer',
                'ResponseTrailersCopy': '控制是否将响应 Trailer 复制回客户端',
                'ResponseTrailersAllowed': '指定允许的 Trailer 列表',
                'X-Forwarded': '设置标准 X-Forwarded-* 头（For/Proto/Host/Prefix）',
                'Forwarded': '设置 RFC 7239 Forwarded 头',
                'ClientCert': '将客户端证书转发为请求头',
                'QueryValueParameter': '添加查询参数',
                'QueryRemoveParameter': '移除查询参数',
                'QueryRouteParameter': '从路由值设置查询参数',
                'HttpMethodChange': '修改请求的 HTTP 方法'
            };
            return descs[name] || 'YARP Transform';
        },

        _getPairingSuggestions: function(name) {
            var suggestions = {
                'PathPattern': ['X-Forwarded', 'RequestHeader'],
                'PathRemovePrefix': ['PathPrefix', 'X-Forwarded'],
                'PathSet': ['X-Forwarded'],
                'PathPrefix': ['PathRemovePrefix'],
                'RequestHeader': ['RequestHeadersAllowed', 'X-Forwarded'],
                'RequestHeaderRouteValue': ['PathPattern'],
                'RequestHeadersAllowed': ['RequestHeader', 'X-Forwarded'],
                'RequestHeaderRemove': ['RequestHeadersAllowed'],
                'RequestHeadersCopy': ['RequestHeadersAllowed'],
                'RequestHeaderOriginalHost': ['X-Forwarded'],
                'ResponseHeader': ['ResponseHeadersCopy', 'ResponseHeadersAllowed'],
                'ResponseHeaderRemove': ['ResponseHeadersAllowed'],
                'ResponseHeadersCopy': ['ResponseHeader'],
                'ResponseHeadersAllowed': ['ResponseHeader'],
                'ResponseTrailer': ['ResponseTrailersCopy'],
                'ResponseTrailerRemove': ['ResponseTrailersAllowed'],
                'ResponseTrailersCopy': ['ResponseTrailer'],
                'ResponseTrailersAllowed': ['ResponseTrailer'],
                'X-Forwarded': ['PathPattern', 'RequestHeader'],
                'Forwarded': ['X-Forwarded'],
                'ClientCert': ['RequestHeader'],
                'QueryValueParameter': ['PathPattern'],
                'QueryRemoveParameter': ['QueryValueParameter'],
                'QueryRouteParameter': ['PathPattern'],
                'HttpMethodChange': ['PathPattern']
            };
            return suggestions[name] || [];
        },

        _getBeforeAfter: function(name) {
            var data = {
                'PathRemovePrefix': { before: 'GET /api/orders/123', after: 'GET /123' },
                'PathSet': { before: 'GET /api/orders/123', after: 'GET /new-path' },
                'PathPrefix': { before: 'GET /orders/123', after: 'GET /api/orders/123' },
                'PathPattern': { before: 'GET /api/orders/123/items', after: 'GET /orders/123/items' },
                'RequestHeader': { before: 'X-Custom: (none)', after: 'X-Custom: value' },
                'RequestHeaderRouteValue': { before: 'X-Id: (none)', after: 'X-Id: order-123' },
                'RequestHeadersAllowed': { before: '所有请求头转发', after: '仅 Authorization, Content-Type 转发' },
                'RequestHeaderRemove': { before: 'X-Internal: secret', after: 'X-Internal: (removed)' },
                'RequestHeadersCopy': { before: '所有请求头复制到出站', after: '不复制请求头' },
                'RequestHeaderOriginalHost': { before: 'Host: backend.local', after: 'Host: client.example.com' },
                'ResponseHeader': { before: 'X-Custom: (none)', after: 'X-Custom: value' },
                'ResponseHeaderRemove': { before: 'Server: nginx', after: 'Server: (removed)' },
                'ResponseHeadersCopy': { before: '所有响应头复制回客户端', after: '不复制响应头' },
                'ResponseHeadersAllowed': { before: '所有响应头转发', after: '仅 Content-Type, X-Custom 转发' },
                'ResponseTrailer': { before: 'X-Trace: (none)', after: 'X-Trace: value' },
                'ResponseTrailerRemove': { before: 'X-Debug: data', after: 'X-Debug: (removed)' },
                'ResponseTrailersCopy': { before: '所有 Trailer 复制回客户端', after: '不复制 Trailer' },
                'ResponseTrailersAllowed': { before: '所有 Trailer 转发', after: '仅 X-Trace 转发' },
                'X-Forwarded': { before: '后端无法获取客户端 IP', after: 'X-Forwarded-For: 1.2.3.4' },
                'Forwarded': { before: '后端无法获取客户端信息', after: 'Forwarded: for=1.2.3.4;proto=https' },
                'ClientCert': { before: '后端无法获取客户端证书', after: 'X-Client-Cert: (base64 cert)' },
                'QueryValueParameter': { before: '? (no version)', after: '?version=1' },
                'QueryRemoveParameter': { before: '?debug=1', after: '? (debug removed)' },
                'QueryRouteParameter': { before: '? (no userId)', after: '?userId=order-123' },
                'HttpMethodChange': { before: 'PUT /api/resource', after: 'POST /api/resource' }
            };
            return data[name] || null;
        },

        _getTransformExample: function(name) {
            var examples = {
                'PathRemovePrefix': '{ "PathRemovePrefix": "/api" }',
                'PathSet': '{ "PathSet": "/new-path" }',
                'PathPrefix': '{ "PathPrefix": "/api" }',
                'PathPattern': '{ "PathPattern": "/api/{**remainder}" }',
                'RequestHeader': '{ "RequestHeader": "X-Custom", "Set": "value" }',
                'RequestHeaderRouteValue': '{ "RequestHeaderRouteValue": "X-Id", "Set": "routeId" }',
                'RequestHeadersAllowed': '{ "RequestHeadersAllowed": ["Authorization", "Content-Type"] }',
                'RequestHeaderRemove': '{ "RequestHeaderRemove": "X-Internal" }',
                'ResponseHeader': '{ "ResponseHeader": "X-Custom", "Set": "value" }',
                'ResponseHeaderRemove': '{ "ResponseHeaderRemove": "Server" }',
                'ResponseTrailer': '{ "ResponseTrailer": "X-Trace", "Set": "value" }',
                'ResponseTrailersAllowed': '{ "ResponseTrailersAllowed": ["X-Trace"] }',
                'X-Forwarded': '{ "X-Forwarded": "Set", "For": "Set", "Proto": "Set", "Host": "Set" }',
                'Forwarded': '{ "Forwarded": "for,by,proto,host", "Action": "Set" }',
                'ClientCert': '{ "ClientCert": "X-Client-Cert" }',
                'QueryValueParameter': '{ "QueryValueParameter": "version", "Set": "1" }',
                'QueryRemoveParameter': '{ "QueryRemoveParameter": "debug" }',
                'QueryRouteParameter': '{ "QueryRouteParameter": "userId", "Set": "userId" }',
                'HttpMethodChange': '{ "HttpMethodChange": "PUT", "Set": "POST" }'
            };
            return this._esc(examples[name] || '{}');
        },

        _toggleCode: function(name) {
            var el = document.getElementById('code-' + name);
            if (el) el.style.display = el.style.display === 'none' ? 'block' : 'none';
        },

        _applyToRoute: function(transformName, encodedExample) {
            var self = this;
            var prefix = window.__routePrefix || 'apigateway';
            var example = JSON.parse(decodeURIComponent(encodedExample));

            // Fetch routes
            fetch('/' + prefix + '/api/routes')
                .then(function(res) { return res.json(); })
                .then(function(json) {
                    var routes = json.data || json || [];
                    if (!routes || routes.length === 0) {
                        if (window.DashboardModals) window.DashboardModals.showError('没有可用的路由，请先创建路由');
                        return;
                    }

                    // Show route selector using a simple modal
                    var routeOptions = routes.map(function(r) {
                        var id = r.routeId || r.RouteId;
                        return '<option value="' + id + '">' + id + '</option>';
                    }).join('');

                    // Create a temporary modal for route selection
                    var modalId = 'transform-apply-modal-' + Date.now();
                    var modal = document.createElement('div');
                    modal.className = 'modal fade';
                    modal.id = modalId;
                    modal.innerHTML = '<div class="modal-dialog"><div class="modal-content">' +
                        '<div class="modal-header"><h5 class="modal-title">应用 Transform 到路由</h5>' +
                        '<button type="button" class="btn-close" data-bs-dismiss="modal"></button></div>' +
                        '<div class="modal-body">' +
                        '<p class="small">选择要添加 <code>' + transformName + '</code> Transform 的路由：</p>' +
                        '<select class="form-select form-select-sm mb-2" id="' + modalId + '-select">' + routeOptions + '</select>' +
                        '<p class="small text-muted">Transform 配置：</p>' +
                        '<pre class="small bg-dark text-light p-2 rounded"><code>' + self._esc(JSON.stringify(example, null, 2)) + '</code></pre>' +
                        '</div>' +
                        '<div class="modal-footer">' +
                        '<button class="btn btn-secondary" data-bs-dismiss="modal">取消</button>' +
                        '<button class="btn btn-success" id="' + modalId + '-confirm">添加 Transform</button>' +
                        '</div></div></div>';

                    document.body.appendChild(modal);
                    var bsModal = new bootstrap.Modal(modal);
                    bsModal.show();
                    modal.addEventListener('hidden.bs.modal', function() { modal.remove(); });

                    document.getElementById(modalId + '-confirm').onclick = function() {
                        var routeId = document.getElementById(modalId + '-select').value;
                        if (!routeId) return;

                        // Fetch the full route config
                        fetch('/' + prefix + '/api/routes/' + encodeURIComponent(routeId))
                            .then(function(r) { return r.json(); })
                            .then(function(routeJson) {
                                var routeData = routeJson.data || routeJson;
                                // Ensure Transforms array exists
                                if (!routeData.transforms) routeData.transforms = [];
                                if (!routeData.Transforms) routeData.Transforms = [];

                                // Add the transform
                                routeData.transforms.push(example);
                                routeData.Transforms.push(example);

                                // Save back
                                return fetch('/' + prefix + '/api/routes/' + encodeURIComponent(routeId), {
                                    method: 'PUT',
                                    headers: { 'Content-Type': 'application/json' },
                                    body: JSON.stringify(routeData)
                                });
                            })
                            .then(function(res) { return res.json(); })
                            .then(function(result) {
                                bsModal.hide();
                                if (window.DashboardModals) {
                                    window.DashboardModals.showSuccess('Transform "' + transformName + '" 已添加到路由 "' + routeId + '"');
                                }
                            })
                            .catch(function(err) {
                                if (window.DashboardModals) {
                                    window.DashboardModals.showError('添加 Transform 失败: ' + err.message);
                                }
                            });
                    };
                })
                .catch(function(err) {
                    if (window.DashboardModals) window.DashboardModals.showError('获取路由失败: ' + err.message);
                });
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

    var self = window.DashboardTransformGuide;
})();
