/**
 * Configuration Wizard - Multi-step guided route creation.
 * 5 steps: Route ID -> Match Rules -> Target Cluster -> Advanced -> Confirm
 */
(function() {
    'use strict';

    window.DashboardWizard = {
        currentStep: 0,
        totalSteps: 5,
        data: {
            routeId: '',
            path: '',
            hosts: '',
            methods: [],
            clusterMode: 'existing', // 'existing' | 'new'
            clusterId: '',
            newClusterAddress: '',
            loadBalancing: 'PowerOfTwoChoices',
            enableCircuitBreaker: false,
            enableRetry: false,
            enableRateLimit: false,
            enableHealthCheck: false
        },
        existingClusters: [],

        /**
         * Open the wizard modal.
         */
        open: function(clusters) {
            this.existingClusters = clusters || [];
            this.existingRouteIds = [];
            this.currentStep = 0;
            this.data = {
                routeId: '',
                path: '/api/',
                hosts: '',
                methods: [],
                clusterMode: this.existingClusters.length > 0 ? 'existing' : 'new',
                clusterId: this.existingClusters.length > 0 ? this.existingClusters[0].clusterId : '',
                newClusterAddress: 'http://localhost:5001',
                loadBalancing: 'PowerOfTwoChoices',
                enableCircuitBreaker: false,
                enableRetry: false,
                enableRateLimit: false,
                enableHealthCheck: false
            };

            // Fetch existing route IDs for duplicate check
            var self = this;
            if (window.DashboardApi && window.DashboardApi.endpoints && window.DashboardApi.endpoints.getRoutes) {
                window.DashboardApi.endpoints.getRoutes()
                    .then(function(routes) {
                        self.existingRouteIds = (routes || []).map(function(r) {
                            return (r.routeId || r.RouteId || '').toLowerCase();
                        });
                    })
                    .catch(function() { self.existingRouteIds = []; });
            }

            this._showModal();
        },

        _showModal: function() {
            var self = this;

            // Remove existing modal
            var existing = document.getElementById('wizard-modal');
            if (existing) existing.remove();

            var modal = document.createElement('div');
            modal.className = 'modal fade';
            modal.id = 'wizard-modal';
            modal.setAttribute('tabindex', '-1');
            modal.innerHTML =
                '<div class="modal-dialog modal-xl">' +
                '<div class="modal-content">' +
                '<div class="modal-header">' +
                '<h5 class="modal-title"><i class="bi bi-magic"></i> 路由创建向导</h5>' +
                '<button type="button" class="btn-close" data-bs-dismiss="modal"></button>' +
                '</div>' +
                '<div class="modal-body" id="wizard-body"></div>' +
                '<div class="modal-footer" id="wizard-footer"></div>' +
                '</div>' +
                '</div>';

            document.body.appendChild(modal);
            this._renderStep();

            modal.addEventListener('hidden.bs.modal', function() {
                modal.remove();
            });

            new bootstrap.Modal(modal).show();
        },

        _renderStep: function() {
            var body = document.getElementById('wizard-body');
            var footer = document.getElementById('wizard-footer');
            if (!body || !footer) return;

            // Progress indicator
            var progress = '<div class="d-flex justify-content-between mb-3">';
            var stepNames = ['路由标识', '匹配规则', '目标集群', '高级选项', '确认创建'];
            for (var i = 0; i < this.totalSteps; i++) {
                var active = i === this.currentStep;
                var done = i < this.currentStep;
                var bg = done ? 'bg-success' : (active ? 'bg-primary' : 'bg-light text-muted');
                progress += '<div class="text-center flex-grow-1">' +
                    '<div class="rounded-circle mx-auto d-flex align-items-center justify-content-center ' +
                    bg + '" style="width:32px;height:32px;font-size:14px;">' +
                    (done ? '<i class="bi bi-check"></i>' : (i + 1)) + '</div>' +
                    '<small class="' + (active ? 'fw-bold' : 'text-muted') + ' d-block mt-1">' + stepNames[i] + '</small>' +
                    '</div>';
                if (i < this.totalSteps - 1) {
                    progress += '<div class="flex-grow-1 mt-2"><hr class="' + (done ? 'border-success' : '') + '"></div>';
                }
            }
            progress += '</div>';

            body.innerHTML = progress + '<div id="wizard-step-content"></div>';

            // Render step content
            var content = '';
            switch (this.currentStep) {
                case 0: content = this._renderStep0(); break;
                case 1: content = this._renderStep1(); break;
                case 2: content = this._renderStep2(); break;
                case 3: content = this._renderStep3(); break;
                case 4: content = this._renderStep4(); break;
            }
            document.getElementById('wizard-step-content').innerHTML = content;

            // Render footer buttons
            var footerHtml = '';
            if (this.currentStep > 0) {
                footerHtml += '<button class="btn btn-secondary" onclick="DashboardWizard._prev()"><i class="bi bi-arrow-left"></i> 上一步</button>';
            }
            if (this.currentStep < this.totalSteps - 1) {
                footerHtml += '<button class="btn btn-primary" onclick="DashboardWizard._next()">下一步 <i class="bi bi-arrow-right"></i></button>';
            } else {
                footerHtml += '<button class="btn btn-success" onclick="DashboardWizard._create()"><i class="bi bi-check-lg"></i> 确认创建</button>';
            }
            footer.innerHTML = footerHtml;

            // Bind inputs
            this._bindInputs();
        },

        // Step 0: Route ID
        _renderStep0: function() {
            return '<h6>路由标识</h6>' +
                '<p class="text-muted small">为路由指定一个唯一标识符，建议使用 kebab-case 格式。</p>' +
                '<div class="mb-3">' +
                '<label class="form-label">路由 ID <span class="text-danger">*</span></label>' +
                '<input type="text" class="form-control" id="wiz-routeId" value="' + this._esc(this.data.routeId) + '" placeholder="例如: order-service">' +
                '<small class="text-muted">使用小写字母和连字符，如 user-service、api-gateway</small>' +
                '<div id="wiz-routeId-warning" style="display:none;" class="mt-1"><span class="text-danger small"><i class="bi bi-exclamation-triangle"></i> 此路由 ID 已存在，将继续使用将覆盖现有配置</span></div>' +
                '</div>' +
                '<div class="alert alert-info small"><i class="bi bi-lightbulb"></i> 路由 ID 是路由的唯一标识，创建后可以重命名但不建议频繁修改。</div>';
        },

        // Step 1: Match Rules
        _renderStep1: function() {
            var methods = ['GET', 'POST', 'PUT', 'DELETE', 'PATCH', 'HEAD', 'OPTIONS'];
            var methodCheckboxes = methods.map(function(m) {
                var checked = this.data.methods.indexOf(m) >= 0 ? 'checked' : '';
                return '<div class="form-check form-check-inline">' +
                    '<input type="checkbox" class="form-check-input wiz-method" value="' + m + '" ' + checked + '>' +
                    '<label class="form-check-label">' + m + '</label></div>';
            }.bind(this)).join('');

            return '<h6>匹配规则</h6>' +
                '<p class="text-muted small">定义哪些请求会被此路由匹配。</p>' +
                '<div class="mb-3">' +
                '<label class="form-label">路径模式 <span class="text-danger">*</span></label>' +
                '<input type="text" class="form-control" id="wiz-path" value="' + this._esc(this.data.path) + '" placeholder="/api/orders/{**catch-all}">' +
                '<small class="text-muted">使用 YARP 路径模板，如 /api/orders/{**catch-all}</small>' +
                '</div>' +
                '<div class="mb-3">' +
                '<label class="form-label">Host（可选）</label>' +
                '<input type="text" class="form-control" id="wiz-hosts" value="' + this._esc(this.data.hosts) + '" placeholder="example.com">' +
                '<small class="text-muted">多个 Host 用逗号分隔</small>' +
                '</div>' +
                '<div class="mb-3">' +
                '<label class="form-label">HTTP 方法（可选）</label>' +
                '<div>' + methodCheckboxes + '</div>' +
                '<small class="text-muted">不选则匹配所有方法</small>' +
                '</div>';
        },

        // Step 2: Target Cluster
        _renderStep2: function() {
            var clusterOptions = this.existingClusters.map(function(c) {
                var dest = c.destinations ? Object.keys(c.destinations).length : 0;
                return '<option value="' + c.clusterId + '"' +
                    (c.clusterId === this.data.clusterId ? ' selected' : '') + '>' +
                    c.clusterId + ' (' + dest + ' 个后端)</option>';
            }.bind(this)).join('');

            var existingHtml = this.existingClusters.length > 0
                ? '<div class="mb-3">' +
                  '<label class="form-label">选择已有集群</label>' +
                  '<select class="form-select" id="wiz-clusterId">' + clusterOptions + '</select>' +
                  '</div>'
                : '<div class="alert alert-warning small">暂无已有集群，请创建新集群。</div>';

            return '<h6>目标集群</h6>' +
                '<p class="text-muted small">选择路由转发的目标集群，或创建新集群。</p>' +
                '<div class="mb-3">' +
                '<div class="form-check">' +
                '<input type="radio" class="form-check-input wiz-clusterMode" name="clusterMode" value="existing" id="mode-existing" ' +
                (this.data.clusterMode === 'existing' ? 'checked' : '') + '>' +
                '<label class="form-check-label" for="mode-existing">使用已有集群</label>' +
                '</div>' +
                '<div class="form-check">' +
                '<input type="radio" class="form-check-input wiz-clusterMode" name="clusterMode" value="new" id="mode-new" ' +
                (this.data.clusterMode === 'new' ? 'checked' : '') + '>' +
                '<label class="form-check-label" for="mode-new">创建新集群</label>' +
                '</div>' +
                '</div>' +
                '<div id="cluster-existing">' + existingHtml + '</div>' +
                '<div id="cluster-new" style="display:none;">' +
                '<div class="mb-3">' +
                '<label class="form-label">后端地址 <span class="text-danger">*</span></label>' +
                '<input type="text" class="form-control" id="wiz-newClusterAddress" value="' + this._esc(this.data.newClusterAddress) + '" placeholder="http://localhost:5001">' +
                '</div>' +
                '<div class="mb-3">' +
                '<label class="form-label">负载均衡策略</label>' +
                '<select class="form-select" id="wiz-loadBalancing">' +
                '<option value="PowerOfTwoChoices"' + (this.data.loadBalancing === 'PowerOfTwoChoices' ? ' selected' : '') + '>PowerOfTwoChoices - 推荐</option>' +
                '<option value="RoundRobin"' + (this.data.loadBalancing === 'RoundRobin' ? ' selected' : '') + '>RoundRobin - 轮询</option>' +
                '<option value="LeastRequests"' + (this.data.loadBalancing === 'LeastRequests' ? ' selected' : '') + '>LeastRequests - 最少请求</option>' +
                '<option value="Random"' + (this.data.loadBalancing === 'Random' ? ' selected' : '') + '>Random - 随机</option>' +
                '</select>' +
                '</div>' +
                '</div>';
        },

        // Step 3: Advanced Options
        _renderStep3: function() {
            var checkbox = function(id, label, desc, checked) {
                return '<div class="form-check mb-2">' +
                    '<input type="checkbox" class="form-check-input wiz-advanced" id="' + id + '" ' + (checked ? 'checked' : '') + '>' +
                    '<label class="form-check-label" for="' + id + '"><strong>' + label + '</strong></label>' +
                    '<small class="text-muted d-block">' + desc + '</small>' +
                    '</div>';
            };

            return '<h6>高级选项</h6>' +
                '<p class="text-muted small">选择需要启用的附加功能（可在策略管理页面后续配置）。</p>' +
                checkbox('wiz-cb', '熔断器', '连续失败时自动断开故障链路，防止级联故障', this.data.enableCircuitBreaker) +
                checkbox('wiz-retry', '请求重试', '临时故障时自动重发请求（仅幂等请求）', this.data.enableRetry) +
                checkbox('wiz-rl', '限流', '限制单位时间请求数量，防止过载和滥用', this.data.enableRateLimit) +
                checkbox('wiz-hc', '健康检查', '定期检测后端可用性，自动剔除不健康节点', this.data.enableHealthCheck) +
                '<div class="alert alert-info small mt-3"><i class="bi bi-info-circle"></i> 勾选的功能将在路由创建后提示你去策略管理页面配置详细参数。</div>';
        },

        // Step 4: Confirm
        _renderStep4: function() {
            this._collectData();

            var config = this._buildConfig();
            var summary = '<h6>确认创建</h6>' +
                '<p class="text-muted small">请确认以下配置信息，点击"确认创建"将创建路由' +
                (this.data.clusterMode === 'new' ? '和集群' : '') + '。</p>' +
                '<table class="table table-sm">' +
                '<tr><td class="text-muted" style="width:120px;">路由 ID</td><td><code>' + this._esc(this.data.routeId) + '</code></td></tr>' +
                '<tr><td class="text-muted">路径模式</td><td><code>' + this._esc(this.data.path) + '</code></td></tr>' +
                (this.data.hosts ? '<tr><td class="text-muted">Host</td><td>' + this._esc(this.data.hosts) + '</td></tr>' : '') +
                (this.data.methods.length > 0 ? '<tr><td class="text-muted">方法</td><td>' + this.data.methods.join(', ') + '</td></tr>' : '') +
                '<tr><td class="text-muted">集群</td><td><code>' + this._esc(this.data.clusterMode === 'new' ? this.data.routeId + '-cluster (新建)' : this.data.clusterId) + '</code></td></tr>' +
                (this.data.clusterMode === 'new' ? '<tr><td class="text-muted">后端地址</td><td><code>' + this._esc(this.data.newClusterAddress) + '</code></td></tr>' : '') +
                '</table>';

            // Advanced options summary
            var adv = [];
            if (this.data.enableCircuitBreaker) adv.push('熔断器');
            if (this.data.enableRetry) adv.push('重试');
            if (this.data.enableRateLimit) adv.push('限流');
            if (this.data.enableHealthCheck) adv.push('健康检查');
            if (adv.length > 0) {
                summary += '<div class="mb-2"><strong>附加功能:</strong> ' + adv.map(function(a) {
                    return '<span class="badge bg-primary">' + a + '</span>';
                }).join(' ') + '</div>';
            }

            summary += '<div class="mt-3"><strong>JSON 预览:</strong>' +
                '<pre class="small bg-dark text-light p-2 rounded" style="max-height:300px;overflow:auto;"><code>' +
                this._esc(JSON.stringify(config, null, 2)) + '</code></pre></div>';

            return summary;
        },

        _bindInputs: function() {
            var self = this;

            // Route ID
            var routeIdInput = document.getElementById('wiz-routeId');
            if (routeIdInput) {
                routeIdInput.addEventListener('input', function() {
                    self.data.routeId = this.value;
                    // Real-time duplicate check
                    var warning = document.getElementById('wiz-routeId-warning');
                    if (warning && self.existingRouteIds) {
                        var isDuplicate = self.existingRouteIds.indexOf(this.value.toLowerCase()) >= 0;
                        warning.style.display = isDuplicate && this.value ? 'block' : 'none';
                    }
                });
            }

            // Path
            var pathInput = document.getElementById('wiz-path');
            if (pathInput) {
                pathInput.addEventListener('input', function() {
                    self.data.path = this.value;
                });
            }

            // Hosts
            var hostsInput = document.getElementById('wiz-hosts');
            if (hostsInput) {
                hostsInput.addEventListener('input', function() {
                    self.data.hosts = this.value;
                });
            }

            // Methods
            var methodCheckboxes = document.querySelectorAll('.wiz-method');
            methodCheckboxes.forEach(function(cb) {
                cb.addEventListener('change', function() {
                    self.data.methods = Array.from(methodCheckboxes)
                        .filter(function(c) { return c.checked; })
                        .map(function(c) { return c.value; });
                });
            });

            // Cluster mode
            var clusterModeRadios = document.querySelectorAll('.wiz-clusterMode');
            clusterModeRadios.forEach(function(radio) {
                radio.addEventListener('change', function() {
                    self.data.clusterMode = this.value;
                    var existingDiv = document.getElementById('cluster-existing');
                    var newDiv = document.getElementById('cluster-new');
                    if (existingDiv) existingDiv.style.display = self.data.clusterMode === 'existing' ? '' : 'none';
                    if (newDiv) newDiv.style.display = self.data.clusterMode === 'new' ? '' : 'none';
                });
            });

            // Cluster ID select
            var clusterIdSelect = document.getElementById('wiz-clusterId');
            if (clusterIdSelect) {
                clusterIdSelect.addEventListener('change', function() {
                    self.data.clusterId = this.value;
                });
            }

            // New cluster address
            var newAddrInput = document.getElementById('wiz-newClusterAddress');
            if (newAddrInput) {
                newAddrInput.addEventListener('input', function() {
                    self.data.newClusterAddress = this.value;
                });
            }

            // Load balancing
            var lbSelect = document.getElementById('wiz-loadBalancing');
            if (lbSelect) {
                lbSelect.addEventListener('change', function() {
                    self.data.loadBalancing = this.value;
                });
            }

            // Advanced checkboxes
            var advCheckboxes = document.querySelectorAll('.wiz-advanced');
            advCheckboxes.forEach(function(cb) {
                cb.addEventListener('change', function() {
                    var key = cb.id === 'wiz-cb' ? 'enableCircuitBreaker' :
                              cb.id === 'wiz-retry' ? 'enableRetry' :
                              cb.id === 'wiz-rl' ? 'enableRateLimit' :
                              cb.id === 'wiz-hc' ? 'enableHealthCheck' : '';
                    if (key) self.data[key] = cb.checked;
                });
            });
        },

        _collectData: function() {
            // Data already collected via input bindings
        },

        _buildConfig: function() {
            var clusterId = this.data.clusterMode === 'new'
                ? this.data.routeId + '-cluster'
                : this.data.clusterId;

            var route = {
                ClusterId: clusterId,
                Order: 100,
                Match: { Path: this.data.path }
            };

            if (this.data.hosts) {
                route.Match.Hosts = this.data.hosts.split(',').map(function(h) { return h.trim(); }).filter(function(h) { return h; });
            }
            if (this.data.methods.length > 0) {
                route.Match.Methods = this.data.methods;
            }

            var config = {
                Routes: {},
                Clusters: {}
            };
            config.Routes[this.data.routeId] = route;

            if (this.data.clusterMode === 'new') {
                config.Clusters[clusterId] = {
                    LoadBalancingPolicy: this.data.loadBalancing,
                    Destinations: {
                        node1: { Address: this.data.newClusterAddress }
                    }
                };

                if (this.data.enableHealthCheck) {
                    config.Clusters[clusterId].HealthCheck = {
                        Active: {
                            Enabled: true,
                            Interval: '00:00:15',
                            Timeout: '00:00:10',
                            Path: '/health'
                        }
                    };
                }
            }

            return config;
        },

        _next: function() {
            // Validate current step
            if (this.currentStep === 0 && !this.data.routeId.trim()) {
                if (window.DashboardModals) window.DashboardModals.showError('请输入路由 ID');
                return;
            }
            if (this.currentStep === 1 && !this.data.path.trim()) {
                if (window.DashboardModals) window.DashboardModals.showError('请输入路径模式');
                return;
            }

            this.currentStep++;
            this._renderStep();
        },

        _prev: function() {
            this.currentStep--;
            this._renderStep();
        },

        _create: function() {
            var self = this;
            var config = this._buildConfig();

            // Use the existing DashboardApi import endpoint
            if (!window.DashboardApi || !window.DashboardApi.endpoints || !window.DashboardApi.endpoints.importConfig) {
                if (window.DashboardModals) window.DashboardModals.showError('API not available');
                return;
            }

            window.DashboardApi.endpoints.importConfig(config)
                .then(function(response) {
                    var modalEl = document.getElementById('wizard-modal');
                    if (modalEl) {
                        var bsModal = bootstrap.Modal.getInstance(modalEl);
                        if (bsModal) bsModal.hide();
                    }
                    if (window.DashboardModals) {
                        window.DashboardModals.showSuccess('路由创建成功！');
                    }

                    // Auto-create policies if checked
                    var clusterId = self.data.clusterMode === 'new'
                        ? self.data.routeId + '-cluster'
                        : self.data.clusterId;
                    var routeId = self.data.routeId;
                    var policyPromises = [];

                    // Create circuit breaker policy
                    if (self.data.enableCircuitBreaker) {
                        policyPromises.push(
                            self._createPolicy('cluster', {
                                name: routeId + '-cb',
                                policyId: routeId + '-cb',
                                failureThreshold: 5,
                                recoveryTimeoutSeconds: 30,
                                halfOpenMaxAttempts: 3,
                                failureStatusCodes: [500, 502, 503, 504],
                                clusterIds: [clusterId]
                            })
                        );
                    }

                    // Create retry policy
                    if (self.data.enableRetry) {
                        policyPromises.push(
                            self._createPolicy('route', {
                                name: routeId + '-retry',
                                policyId: routeId + '-retry',
                                retryEnabled: true,
                                maxRetries: 3,
                                routeIds: [routeId]
                            })
                        );
                    }

                    // Create rate limit policy
                    if (self.data.enableRateLimit) {
                        policyPromises.push(
                            self._createPolicy('route', {
                                name: routeId + '-rl',
                                policyId: routeId + '-rl',
                                rateLimitEnabled: true,
                                permitLimit: 100,
                                window: '1m',
                                routeIds: [routeId]
                            })
                        );
                    }

                    // Wait for all policy creations
                    if (policyPromises.length > 0) {
                        Promise.allSettled(policyPromises).then(function(results) {
                            var succeeded = results.filter(function(r) { return r.status === 'fulfilled'; }).length;
                            var failed = results.filter(function(r) { return r.status === 'rejected'; }).length;
                            if (failed > 0 && window.DashboardModals) {
                                setTimeout(function() {
                                    window.DashboardModals.showInfo(
                                        succeeded + ' 个策略创建成功，' + failed + ' 个失败。请到策略管理页面检查。'
                                    );
                                }, 1500);
                            }
                            setTimeout(function() { window.location.reload(); }, 2500);
                        });
                    } else {
                        setTimeout(function() { window.location.reload(); }, 2500);
                    }
                })
                .catch(function(err) {
                    if (window.DashboardModals) {
                        window.DashboardModals.showError('创建失败: ' + (err.message || err));
                    }
                });
        },

        _esc: function(s) {
            if (!s) return '';
            var d = document.createElement('div');
            d.textContent = s;
            return d.innerHTML;
        },

        /**
         * Create a policy via the API.
         * @param {string} type - 'routes' or 'clusters'
         * @param {object} policyData - Policy configuration
         * @returns {Promise}
         */
        _createPolicy: function(type, policyData) {
            if (window.DashboardApi && window.DashboardApi.endpoints && window.DashboardApi.endpoints.createPolicy) {
                return window.DashboardApi.endpoints.createPolicy(type, policyData);
            }
            return Promise.reject(new Error('Policy API not available'));
        },

        // ═══════════════════════════════════════════
        // CLUSTER WIZARD (3 steps)
        // ═══════════════════════════════════════════

        clusterStep: 0,
        clusterTotalSteps: 3,
        clusterData: {
            clusterId: '',
            loadBalancing: 'PowerOfTwoChoices',
            destinations: [{ address: 'http://localhost:5001' }],
            enableHealthCheck: false,
            healthPath: '/health',
            healthInterval: '00:00:15',
            healthTimeout: '00:00:10'
        },

        /**
         * Open the cluster creation wizard (3 steps).
         */
        openCluster: function() {
            this.clusterStep = 0;
            this.clusterData = {
                clusterId: '',
                loadBalancing: 'PowerOfTwoChoices',
                destinations: [{ address: 'http://localhost:5001' }],
                enableHealthCheck: false,
                healthPath: '/health',
                healthInterval: '00:00:15',
                healthTimeout: '00:00:10'
            };
            this._showClusterModal();
        },

        _showClusterModal: function() {
            var existing = document.getElementById('cluster-wizard-modal');
            if (existing) existing.remove();

            var modal = document.createElement('div');
            modal.className = 'modal fade';
            modal.id = 'cluster-wizard-modal';
            modal.setAttribute('tabindex', '-1');
            modal.innerHTML =
                '<div class="modal-dialog modal-lg">' +
                '<div class="modal-content">' +
                '<div class="modal-header">' +
                '<h5 class="modal-title"><i class="bi bi-magic"></i> 集群创建向导</h5>' +
                '<button type="button" class="btn-close" data-bs-dismiss="modal"></button>' +
                '</div>' +
                '<div class="modal-body" id="cluster-wizard-body"></div>' +
                '<div class="modal-footer" id="cluster-wizard-footer"></div>' +
                '</div></div>';

            document.body.appendChild(modal);
            this._renderClusterStep();

            modal.addEventListener('hidden.bs.modal', function() { modal.remove(); });
            new bootstrap.Modal(modal).show();
        },

        _renderClusterStep: function() {
            var body = document.getElementById('cluster-wizard-body');
            var footer = document.getElementById('cluster-wizard-footer');
            if (!body || !footer) return;

            var stepNames = ['集群标识', '后端地址', '健康检查'];
            var progress = '<div class="d-flex justify-content-between mb-3">';
            for (var i = 0; i < this.clusterTotalSteps; i++) {
                var active = i === this.clusterStep;
                var done = i < this.clusterStep;
                var bg = done ? 'bg-success' : (active ? 'bg-primary' : 'bg-light text-muted');
                progress += '<div class="text-center flex-grow-1">' +
                    '<div class="rounded-circle mx-auto d-flex align-items-center justify-content-center ' + bg +
                    '" style="width:32px;height:32px;font-size:14px;">' +
                    (done ? '✓' : (i + 1)) + '</div>' +
                    '<small class="' + (active ? 'fw-bold' : 'text-muted') + ' d-block mt-1">' + stepNames[i] + '</small></div>';
                if (i < this.clusterTotalSteps - 1) progress += '<div class="flex-grow-1 mt-2"><hr></div>';
            }
            progress += '</div>';

            var content = '';
            switch (this.clusterStep) {
                case 0: content = this._renderClusterStep0(); break;
                case 1: content = this._renderClusterStep1(); break;
                case 2: content = this._renderClusterStep2(); break;
            }

            body.innerHTML = progress + content;

            var footerHtml = '';
            if (this.clusterStep > 0) footerHtml += '<button class="btn btn-secondary" onclick="DashboardWizard._clusterPrev()">← 上一步</button>';
            if (this.clusterStep < this.clusterTotalSteps - 1) {
                footerHtml += '<button class="btn btn-primary" onclick="DashboardWizard._clusterNext()">下一步 →</button>';
            } else {
                footerHtml += '<button class="btn btn-success" onclick="DashboardWizard._createCluster()">✓ 确认创建</button>';
            }
            footer.innerHTML = footerHtml;

            this._bindClusterInputs();
        },

        _renderClusterStep0: function() {
            return '<h6>集群标识</h6>' +
                '<p class="text-muted small">为集群指定唯一标识和负载均衡策略。</p>' +
                '<div class="mb-3"><label class="form-label">集群 ID <span class="text-danger">*</span></label>' +
                '<input type="text" class="form-control" id="cw-clusterId" value="' + this._esc(this.clusterData.clusterId) + '" placeholder="例如: order-cluster">' +
                '<small class="text-muted">使用小写字母和连字符</small></div>' +
                '<div class="mb-3"><label class="form-label">负载均衡策略</label>' +
                '<select class="form-select" id="cw-loadBalancing">' +
                '<option value="PowerOfTwoChoices"' + (this.clusterData.loadBalancing === 'PowerOfTwoChoices' ? ' selected' : '') + '>PowerOfTwoChoices - 推荐</option>' +
                '<option value="RoundRobin"' + (this.clusterData.loadBalancing === 'RoundRobin' ? ' selected' : '') + '>RoundRobin - 轮询</option>' +
                '<option value="LeastRequests"' + (this.clusterData.loadBalancing === 'LeastRequests' ? ' selected' : '') + '>LeastRequests - 最少请求</option>' +
                '<option value="Random"' + (this.clusterData.loadBalancing === 'Random' ? ' selected' : '') + '>Random - 随机</option>' +
                '</select></div>';
        },

        _renderClusterStep1: function() {
            var destHtml = '';
            this.clusterData.destinations.forEach(function(d, i) {
                destHtml += '<div class="input-group mb-2" id="dest-row-' + i + '">' +
                    '<span class="input-group-text">node' + (i + 1) + '</span>' +
                    '<input type="text" class="form-control cw-dest" data-index="' + i + '" value="' + d.address + '" placeholder="http://10.0.0.1:8080">' +
                    (i > 0 ? '<button class="btn btn-outline-danger" onclick="DashboardWizard._removeDest(' + i + ')"><i class="bi bi-dash"></i></button>' : '') +
                    '</div>';
            });
            return '<h6>后端地址</h6>' +
                '<p class="text-muted small">添加一个或多个后端服务地址，至少需要 1 个。建议 2 个以上实现高可用。</p>' +
                '<div id="dest-list">' + destHtml + '</div>' +
                '<button class="btn btn-outline-primary btn-sm" onclick="DashboardWizard._addDest()"><i class="bi bi-plus"></i> 添加后端</button>';
        },

        _renderClusterStep2: function() {
            var hcHtml = this.clusterData.enableHealthCheck ?
                '<div id="hc-config">' +
                '<div class="mb-2"><label class="form-label small">健康检查路径</label>' +
                '<input type="text" class="form-control form-control-sm" id="cw-healthPath" value="' + this._esc(this.clusterData.healthPath) + '"></div>' +
                '<div class="row"><div class="col-6 mb-2"><label class="form-label small">检查间隔</label>' +
                '<input type="text" class="form-control form-control-sm" id="cw-healthInterval" value="' + this._esc(this.clusterData.healthInterval) + '"></div>' +
                '<div class="col-6 mb-2"><label class="form-label small">超时时间</label>' +
                '<input type="text" class="form-control form-control-sm" id="cw-healthTimeout" value="' + this._esc(this.clusterData.healthTimeout) + '"></div></div>' +
                '</div>' : '<div id="hc-config" style="display:none;"></div>';

            var config = this._buildClusterConfig();
            return '<h6>健康检查（可选）</h6>' +
                '<p class="text-muted small">启用主动健康检查自动检测后端可用性。</p>' +
                '<div class="form-check mb-3"><input type="checkbox" class="form-check-input" id="cw-enableHc" ' + (this.clusterData.enableHealthCheck ? 'checked' : '') + '>' +
                '<label class="form-check-label" for="cw-enableHc">启用主动健康检查</label></div>' +
                hcHtml +
                '<hr><h6>确认创建</h6>' +
                '<table class="table table-sm"><tr><td class="text-muted" style="width:120px;">集群 ID</td><td><code>' + this._esc(this.clusterData.clusterId) + '</code></td></tr>' +
                '<tr><td class="text-muted">负载均衡</td><td>' + this._esc(this.clusterData.loadBalancing) + '</td></tr>' +
                '<tr><td class="text-muted">后端数量</td><td>' + this.clusterData.destinations.length + '</td></tr>' +
                '<tr><td class="text-muted">健康检查</td><td>' + (this.clusterData.enableHealthCheck ? '✅ 启用' : '❌ 未启用') + '</td></tr></table>' +
                '<pre class="small bg-dark text-light p-2 rounded" style="max-height:200px;overflow:auto;"><code>' +
                this._esc(JSON.stringify(config, null, 2)) + '</code></pre>';
        },

        _bindClusterInputs: function() {
            var self = this;
            var idInput = document.getElementById('cw-clusterId');
            if (idInput) idInput.oninput = function() { self.clusterData.clusterId = this.value; };
            var lbSelect = document.getElementById('cw-loadBalancing');
            if (lbSelect) lbSelect.onchange = function() { self.clusterData.loadBalancing = this.value; };

            var destInputs = document.querySelectorAll('.cw-dest');
            destInputs.forEach(function(input) {
                input.oninput = function() {
                    var idx = parseInt(this.getAttribute('data-index'));
                    self.clusterData.destinations[idx].address = this.value;
                };
            });

            var hcCheckbox = document.getElementById('cw-enableHc');
            if (hcCheckbox) hcCheckbox.onchange = function() {
                self.clusterData.enableHealthCheck = this.checked;
                var hcConfig = document.getElementById('hc-config');
                if (hcConfig) {
                    hcConfig.style.display = this.checked ? '' : 'none';
                    if (this.checked && !hcConfig.innerHTML) {
                        hcConfig.innerHTML = '<div class="mb-2"><label class="form-label small">健康检查路径</label>' +
                            '<input type="text" class="form-control form-control-sm" id="cw-healthPath" value="' + self._esc(self.clusterData.healthPath) + '"></div>' +
                            '<div class="row"><div class="col-6 mb-2"><label class="form-label small">检查间隔</label>' +
                            '<input type="text" class="form-control form-control-sm" id="cw-healthInterval" value="' + self._esc(self.clusterData.healthInterval) + '"></div>' +
                            '<div class="col-6 mb-2"><label class="form-label small">超时时间</label>' +
                            '<input type="text" class="form-control form-control-sm" id="cw-healthTimeout" value="' + self._esc(self.clusterData.healthTimeout) + '"></div></div>';
                    }
                }
            };

            var hpInput = document.getElementById('cw-healthPath');
            if (hpInput) hpInput.oninput = function() { self.clusterData.healthPath = this.value; };
            var hiInput = document.getElementById('cw-healthInterval');
            if (hiInput) hiInput.oninput = function() { self.clusterData.healthInterval = this.value; };
            var htInput = document.getElementById('cw-healthTimeout');
            if (htInput) htInput.oninput = function() { self.clusterData.healthTimeout = this.value; };
        },

        _addDest: function() {
            this.clusterData.destinations.push({ address: 'http://' });
            this._renderClusterStep();
        },

        _removeDest: function(idx) {
            if (this.clusterData.destinations.length <= 1) return;
            this.clusterData.destinations.splice(idx, 1);
            this._renderClusterStep();
        },

        _clusterNext: function() {
            if (this.clusterStep === 0 && !this.clusterData.clusterId.trim()) {
                if (window.DashboardModals) window.DashboardModals.showError('请输入集群 ID');
                return;
            }
            if (this.clusterStep === 1) {
                // Collect destination addresses
                var inputs = document.querySelectorAll('.cw-dest');
                this.clusterData.destinations = [];
                inputs.forEach(function(input) { this.clusterData.destinations.push({ address: input.value }); }.bind(this));
                if (this.clusterData.destinations.length === 0 || !this.clusterData.destinations[0].address.trim()) {
                    if (window.DashboardModals) window.DashboardModals.showError('至少需要 1 个后端地址');
                    return;
                }
            }
            this.clusterStep++;
            this._renderClusterStep();
        },

        _clusterPrev: function() {
            this.clusterStep--;
            this._renderClusterStep();
        },

        _buildClusterConfig: function() {
            var destinations = {};
            this.clusterData.destinations.forEach(function(d, i) {
                destinations['node' + (i + 1)] = { Address: d.address };
            });

            var cluster = {
                LoadBalancingPolicy: this.clusterData.loadBalancing,
                Destinations: destinations
            };

            if (this.clusterData.enableHealthCheck) {
                cluster.HealthCheck = {
                    Active: {
                        Enabled: true,
                        Interval: this.clusterData.healthInterval,
                        Timeout: this.clusterData.healthTimeout,
                        Path: this.clusterData.healthPath
                    }
                };
            }

            return { Clusters: {} };
        },

        _createCluster: function() {
            var self = this;
            var config = this._buildClusterConfig();
            var clusterId = this.clusterData.clusterId;
            var destinations = {};
            this.clusterData.destinations.forEach(function(d, i) {
                destinations['node' + (i + 1)] = { Address: d.address };
            });

            // Use DashboardApi to create cluster
            if (!window.DashboardApi || !window.DashboardApi.endpoints || !window.DashboardApi.endpoints.saveCluster) {
                if (window.DashboardModals) window.DashboardModals.showError('API not available');
                return;
            }

            var clusterConfig = {
                loadBalancingPolicy: this.clusterData.loadBalancing,
                destinations: destinations
            };

            if (this.clusterData.enableHealthCheck) {
                clusterConfig.healthCheck = {
                    active: {
                        enabled: true,
                        interval: this.clusterData.healthInterval,
                        timeout: this.clusterData.healthTimeout,
                        path: this.clusterData.healthPath
                    }
                };
            }

            window.DashboardApi.endpoints.saveCluster(clusterId, clusterConfig)
                .then(function() {
                    var modalEl = document.getElementById('cluster-wizard-modal');
                    if (modalEl) { var m = bootstrap.Modal.getInstance(modalEl); if (m) m.hide(); }
                    if (window.DashboardModals) window.DashboardModals.showSuccess('集群创建成功！');
                    setTimeout(function() { window.location.reload(); }, 2000);
                })
                .catch(function(err) {
                    if (window.DashboardModals) window.DashboardModals.showError('创建失败: ' + (err.message || err));
                });
        }
    };
})();
