/**
 * Template Library - Displays and applies configuration templates.
 */
(function() {
    'use strict';

    window.DashboardTemplates = {
        containerId: 'templates-grid',
        templates: [],
        currentTemplate: null,

        init: function(containerId) {
            this.containerId = containerId || 'templates-grid';
            this.load();
        },

        load: function() {
            var self = this;
            var prefix = window.__routePrefix || 'apigateway';

            fetch('/' + prefix + '/api/config/templates')
                .then(function(res) { return res.json(); })
                .then(function(json) {
                    if (json.success !== false && json.data) {
                        self.templates = json.data;
                        self._render(json.data);
                    } else {
                        self._renderError(json.message);
                    }
                })
                .catch(function(err) { self._renderError(err.message); });
        },

        // ═══════════════════════════════════════════
        // MY TEMPLATES (user-defined, stored in localStorage)
        // ═══════════════════════════════════════════

        _userTemplatesKey: 'aneiang_user_templates',

        getUserTemplates: function() {
            try {
                var raw = localStorage.getItem(this._userTemplatesKey);
                return raw ? JSON.parse(raw) : [];
            } catch (e) { return []; }
        },

        saveUserTemplate: function(name, description, config) {
            var templates = this.getUserTemplates();
            var template = {
                id: 'user-' + Date.now(),
                name: name,
                description: description || '用户自定义模板',
                category: '我的模板',
                difficulty: 'custom',
                features: [],
                config: config,
                steps: [],
                variables: [],
                isUserTemplate: true,
                createdAt: new Date().toISOString()
            };
            templates.unshift(template);
            try {
                localStorage.setItem(this._userTemplatesKey, JSON.stringify(templates));
            } catch (e) {
                if (window.DashboardModals) window.DashboardModals.showError('保存失败：存储空间不足');
                return false;
            }
            return true;
        },

        deleteUserTemplate: function(id) {
            var templates = this.getUserTemplates().filter(function(t) { return t.id !== id; });
            localStorage.setItem(this._userTemplatesKey, JSON.stringify(templates));
            this.load(); // Reload to update display
        },

        _saveCurrentAsTemplate: function() {
            var self = this;
            var prefix = window.__routePrefix || 'apigateway';

            // Fetch current config
            Promise.all([
                fetch('/' + prefix + '/api/routes').then(function(r) { return r.json(); }),
                fetch('/' + prefix + '/api/clusters').then(function(r) { return r.json(); })
            ]).then(function(results) {
                var routes = results[0].data || results[0] || [];
                var clusters = results[1].data || results[1] || [];

                // Build config in YARP format
                var config = { Routes: {}, Clusters: {} };
                routes.forEach(function(r) {
                    var id = r.routeId || r.RouteId;
                    if (id) {
                        config.Routes[id] = {
                            ClusterId: r.clusterId || r.ClusterId,
                            Order: r.order || r.Order || 100,
                            Match: r.match || r.Match || {}
                        };
                    }
                });
                clusters.forEach(function(c) {
                    var id = c.clusterId || c.ClusterId;
                    if (id) {
                        config.Clusters[id] = {
                            LoadBalancingPolicy: c.loadBalancingPolicy || c.LoadBalancingPolicy || 'PowerOfTwoChoices',
                            Destinations: c.destinations || c.Destinations || {}
                        };
                    }
                });

                // Show save dialog
                if (window.DashboardModals) {
                    var body = '<div class="mb-3">' +
                        '<label class="form-label">模板名称</label>' +
                        '<input type="text" class="form-control" id="user-tpl-name" placeholder="例如: 我的微服务配置">' +
                        '</div>' +
                        '<div class="mb-3">' +
                        '<label class="form-label">描述（可选）</label>' +
                        '<textarea class="form-control" id="user-tpl-desc" rows="2" placeholder="模板描述..."></textarea>' +
                        '</div>' +
                        '<details><summary class="small text-muted">预览配置 JSON</summary>' +
                        '<pre class="small bg-dark text-light p-2 rounded" style="max-height:200px;overflow:auto;"><code>' +
                        self._esc(JSON.stringify(config, null, 2)) + '</code></pre></details>';

                    var modal = window.DashboardModals.showConfirm({
                        title: '保存为我的模板',
                        body: body,
                        confirmText: '保存',
                        onConfirm: function() {
                            var name = document.getElementById('user-tpl-name').value;
                            var desc = document.getElementById('user-tpl-desc').value;
                            if (!name) {
                                alert('请输入模板名称');
                                return false;
                            }
                            if (self.saveUserTemplate(name, desc, config)) {
                                if (window.DashboardModals) window.DashboardModals.showSuccess('模板已保存');
                                self.load();
                                return true;
                            }
                            return false;
                        }
                    });
                }
            }).catch(function(err) {
                if (window.DashboardModals) window.DashboardModals.showError('获取当前配置失败: ' + err.message);
            });
        },

        _render: function(templates) {
            var container = document.getElementById(this.containerId);
            if (!container) return;

            if (!templates || templates.length === 0) {
                container.innerHTML = '<div class="col-12 text-center text-muted py-4">No templates available.</div>';
                return;
            }

            var difficultyColors = { beginner: 'success', intermediate: 'warning', advanced: 'danger', custom: 'info' };
            var self = this;

            // Header with "Save as Template" button
            var html = '<div class="col-12 mb-3 d-flex justify-content-between align-items-center">' +
                '<h6 class="mb-0">全局模板（' + templates.length + '）</h6>' +
                '<button class="btn btn-sm btn-outline-success" onclick="DashboardTemplates._saveCurrentAsTemplate()"><i class="bi bi-bookmark-plus"></i> 保存当前配置为模板</button>' +
                '</div>';

            // Global templates
            templates.forEach(function(t) {
                var diffColor = difficultyColors[t.difficulty] || 'secondary';
                var featuresHtml = (t.features || []).map(function(f) {
                    return '<span class="badge bg-light text-dark me-1">' + self._esc(f) + '</span>';
                }).join('');

                html += '<div class="col-lg-4 col-md-6">' +
                    '<div class="card h-100">' +
                    '<div class="card-body">' +
                    '<div class="d-flex justify-content-between align-items-start mb-2">' +
                    '<h6 class="mb-0">' + self._esc(t.name) + '</h6>' +
                    '<span class="badge bg-' + diffColor + '">' + self._esc(t.difficulty) + '</span>' +
                    '</div>' +
                    '<p class="small text-muted mb-2">' + self._esc(t.description) + '</p>' +
                    '<div class="mb-2"><span class="small text-muted me-1">分类:</span>' + self._esc(t.category) + '</div>' +
                    '<div class="mb-3">' + featuresHtml + '</div>' +
                    '<div class="d-flex gap-2">' +
                    '<button class="btn btn-sm btn-primary" onclick="DashboardTemplates._showApply(\'' + t.id + '\')"><i class="bi bi-download"></i> 应用模板</button>' +
                    '<button class="btn btn-sm btn-outline-info" onclick="DashboardTemplates._showDetails(\'' + t.id + '\')"><i class="bi bi-info-circle"></i> 详情</button>' +
                    '</div>' +
                    '</div>' +
                    '</div>' +
                    '</div>';
            });

            // User templates section
            var userTemplates = this.getUserTemplates();
            if (userTemplates.length > 0) {
                html += '<div class="col-12 mt-3 mb-2"><h6 class="mb-0">我的模板（' + userTemplates.length + '）</h6></div>';
                userTemplates.forEach(function(t) {
                    html += '<div class="col-lg-4 col-md-6">' +
                        '<div class="card h-100 border-info">' +
                        '<div class="card-body">' +
                        '<div class="d-flex justify-content-between align-items-start mb-2">' +
                        '<h6 class="mb-0">' + self._esc(t.name) + ' <i class="bi bi-person-fill small text-info"></i></h6>' +
                        '<div class="btn-group btn-group-sm">' +
                        '<button class="btn btn-outline-primary" onclick="DashboardTemplates._showApply(\'' + t.id + '\')"><i class="bi bi-download"></i></button>' +
                        '<button class="btn btn-outline-danger" onclick="DashboardTemplates.deleteUserTemplate(\'' + t.id + '\')"><i class="bi bi-trash"></i></button>' +
                        '</div>' +
                        '</div>' +
                        '<p class="small text-muted mb-2">' + self._esc(t.description) + '</p>' +
                        '<small class="text-muted">创建于: ' + new Date(t.createdAt).toLocaleDateString() + '</small>' +
                        '</div>' +
                        '</div>' +
                        '</div>';
                });
            }

            container.innerHTML = html;
        },

        _showApply: function(templateId) {
            var self = this;

            // Check user templates first (localStorage)
            var userTemplate = this.getUserTemplates().find(function(t) { return t.id === templateId; });
            if (userTemplate) {
                this.currentTemplate = userTemplate;
                this._currentRoutes = [];
                this._currentClusters = [];
                this._renderApplyModal();
                return;
            }

            // Check global templates
            var template = this.templates.find(function(t) { return t.id === templateId; });
            if (!template) return;

            this.currentTemplate = template;

            // Fetch full template config AND current routes/clusters for diff
            var prefix = window.__routePrefix || 'apigateway';

            Promise.all([
                fetch('/' + prefix + '/api/config/templates/' + templateId).then(function(r) { return r.json(); }),
                fetch('/' + prefix + '/api/routes').then(function(r) { return r.json(); }).catch(function() { return { success: false }; }),
                fetch('/' + prefix + '/api/clusters').then(function(r) { return r.json(); }).catch(function() { return { success: false }; })
            ]).then(function(results) {
                if (results[0].success !== false && results[0].data) {
                    self.currentTemplate = results[0].data;
                    self._currentRoutes = (results[1].data || results[1] || []);
                    self._currentClusters = (results[2].data || results[2] || []);
                    self._renderApplyModal();
                }
            }).catch(function() {
                self._currentRoutes = [];
                self._currentClusters = [];
                self._renderApplyModal();
            });
        },

        _renderApplyModal: function() {
            var self = this;
            var template = this.currentTemplate;
            if (!template) return;

            var body = document.getElementById('template-modal-body');
            var title = document.getElementById('template-modal-title');
            title.textContent = '应用模板: ' + template.name;

            // Build Dry-Run diff preview
            var previewHtml = '<div class="alert alert-info small"><i class="bi bi-info-circle"></i> 配置变更预览（Dry-Run）：</div>';

            var currentRouteIds = (self._currentRoutes || []).map(function(r) {
                return r.routeId || r.RouteId || r.RouteName || '';
            }).filter(function(id) { return id; });

            var currentClusterIds = (self._currentClusters || []).map(function(c) {
                return c.clusterId || c.ClusterId || '';
            }).filter(function(id) { return id; });

            if (template.config && template.config.Routes) {
                var routeNames = Object.keys(template.config.Routes);
                var newRoutes = [];
                var modRoutes = [];

                routeNames.forEach(function(name) {
                    if (currentRouteIds.indexOf(name) >= 0) {
                        modRoutes.push(name);
                    } else {
                        newRoutes.push(name);
                    }
                });

                previewHtml += '<div class="mb-2"><strong>路由变更:</strong></div>';

                if (newRoutes.length > 0) {
                    previewHtml += '<div class="mb-1"><span class="text-success fw-bold">+ 新增 (' + newRoutes.length + '):</span><ul class="small mb-1">';
                    newRoutes.forEach(function(name) {
                        var route = template.config.Routes[name];
                        var path = route.Match && route.Match.Path ? route.Match.Path : '/';
                        var cluster = route.ClusterId || '?';
                        previewHtml += '<li class="text-success"><code>+' + self._esc(name) + '</code> -> <code>' + self._esc(cluster) + '</code> (' + self._esc(path) + ')</li>';
                    });
                    previewHtml += '</ul></div>';
                }

                if (modRoutes.length > 0) {
                    previewHtml += '<div class="mb-1"><span class="text-warning fw-bold">~ 修改 (' + modRoutes.length + '):</span><ul class="small mb-1">';
                    modRoutes.forEach(function(name) {
                        var route = template.config.Routes[name];
                        var path = route.Match && route.Match.Path ? route.Match.Path : '/';
                        previewHtml += '<li class="text-warning"><code>~' + self._esc(name) + '</code> (将覆盖现有配置) (' + self._esc(path) + ')</li>';
                    });
                    previewHtml += '</ul></div>';
                }
            }

            if (template.config && template.config.Clusters) {
                var clusterNames = Object.keys(template.config.Clusters);
                var newClusters = [];
                var modClusters = [];

                clusterNames.forEach(function(name) {
                    if (currentClusterIds.indexOf(name) >= 0) {
                        modClusters.push(name);
                    } else {
                        newClusters.push(name);
                    }
                });

                previewHtml += '<div class="mb-2 mt-2"><strong>集群变更:</strong></div>';

                if (newClusters.length > 0) {
                    previewHtml += '<div class="mb-1"><span class="text-success fw-bold">+ 新增 (' + newClusters.length + '):</span><ul class="small mb-1">';
                    newClusters.forEach(function(name) {
                        var cluster = template.config.Clusters[name];
                        var destCount = cluster.Destinations ? Object.keys(cluster.Destinations).length : 0;
                        var lb = cluster.LoadBalancingPolicy || '默认';
                        previewHtml += '<li class="text-success"><code>+' + self._esc(name) + '</code> (' + destCount + ' 个后端, ' + self._esc(lb) + ')</li>';
                    });
                    previewHtml += '</ul></div>';
                }

                if (modClusters.length > 0) {
                    previewHtml += '<div class="mb-1"><span class="text-warning fw-bold">~ 修改 (' + modClusters.length + '):</span><ul class="small mb-1">';
                    modClusters.forEach(function(name) {
                        var cluster = template.config.Clusters[name];
                        var destCount = cluster.Destinations ? Object.keys(cluster.Destinations).length : 0;
                        previewHtml += '<li class="text-warning"><code>~' + self._esc(name) + '</code> (将覆盖现有配置) (' + destCount + ' 个后端)</li>';
                    });
                    previewHtml += '</ul></div>';
                }
            }

            // Variable inputs
            var varHtml = '';
            if (template.variables && template.variables.length > 0) {
                varHtml += '<hr><h6 class="mt-3">请填写变量：</h6>';
                template.variables.forEach(function(v) {
                    varHtml += '<div class="mb-2">' +
                        '<label class="form-label small">' + self._esc(v.label) +
                        (v.required ? ' <span class="text-danger">*</span>' : '') + '</label>' +
                        '<input type="text" class="form-control form-control-sm" id="var-' + v.key + '" value="' + self._esc(v.defaultValue) + '" placeholder="' + self._esc(v.description) + '">' +
                        '<small class="text-muted">' + self._esc(v.description) + '</small>' +
                        '</div>';
                });
            } else {
                varHtml += '<hr><p class="text-info small mt-3">此模板无需填写变量，可直接应用。</p>';
            }

            // Steps
            var stepsHtml = '';
            if (template.steps && template.steps.length > 0) {
                stepsHtml += '<div class="mt-3"><h6>应用后步骤：</h6><ol>';
                template.steps.forEach(function(s) {
                    stepsHtml += '<li class="small">' + self._esc(s) + '</li>';
                });
                stepsHtml += '</ol></div>';
            }

            body.innerHTML = previewHtml + varHtml + stepsHtml;

            var applyBtn = document.getElementById('template-apply-btn');
            applyBtn.onclick = function() { self._apply(); };
            applyBtn.innerHTML = '<i class="bi bi-check-lg"></i> 确认应用';

            new bootstrap.Modal(document.getElementById('template-apply-modal')).show();
        },

        _apply: function() {
            var self = this;
            if (!this.currentTemplate) return;

            var prefix = window.__routePrefix || 'apigateway';
            var variables = {};

            if (this.currentTemplate.variables) {
                this.currentTemplate.variables.forEach(function(v) {
                    var input = document.getElementById('var-' + v.key);
                    if (input) variables[v.key] = input.value;
                });
            }

            var applyBtn = document.getElementById('template-apply-btn');
            applyBtn.disabled = true;
            applyBtn.innerHTML = '<span class="spinner-border spinner-border-sm"></span> 应用中...';

            fetch('/' + prefix + '/api/config/templates/' + this.currentTemplate.id + '/apply', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(variables)
            })
            .then(function(res) { return res.json(); })
            .then(function(json) {
                if (json.success !== false && json.data) {
                    bootstrap.Modal.getInstance(document.getElementById('template-apply-modal')).hide();
                    if (window.DashboardModals) {
                        window.DashboardModals.showSuccess(
                            '模板应用成功！创建 ' + (json.data.importedRoutes || 0) + ' 个路由，' +
                            (json.data.importedClusters || 0) + ' 个集群'
                        );
                    }
                    setTimeout(function() { window.location.reload(); }, 2000);
                } else {
                    applyBtn.disabled = false;
                    applyBtn.textContent = '确认应用';
                    if (window.DashboardModals) {
                        window.DashboardModals.showError(json.message || '应用失败');
                    }
                }
            })
            .catch(function(err) {
                applyBtn.disabled = false;
                applyBtn.textContent = '确认应用';
                if (window.DashboardModals) {
                    window.DashboardModals.showError('应用失败: ' + err.message);
                }
            });
        },

        _showDetails: function(templateId) {
            // Details view now uses the same apply modal which includes config preview
            this._showApply(templateId);
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
})();
