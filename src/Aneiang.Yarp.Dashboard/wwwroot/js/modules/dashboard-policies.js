/**
 * Policy Management Module - Route policies & Cluster policies with apply/unapply
 */
(function() {
    'use strict';

    var PolicyModule = {
        name: 'policy',
        initialized: false,
        autoRefreshInterval: null,
        currentTab: 'route',
        editingId: null,
        editingType: null,
        modal: null,

        init: function() {
            if (this.initialized) return;
            this.setupEvents();
            this.initialized = true;
            var self = this;
            setTimeout(function() {
                if (window.bootstrap) {
                    self.modal = new window.bootstrap.Modal(document.getElementById('policyModal'), { backdrop: 'static', keyboard: false });
                }
            }, 100);
        },

        setupEvents: function() {
            var self = this;
            document.addEventListener('dashboard:ready', function() {
                if (self.autoRefreshInterval) clearInterval(self.autoRefreshInterval);
                self.autoRefreshInterval = setInterval(function() {
                    self.load();
                }, 30000);
            });
            document.addEventListener('dashboard:localeChange', function() { self.load(); });

            var tabBtns = document.querySelectorAll('[data-policy-tab]');
            tabBtns.forEach(function(btn) {
                btn.addEventListener('click', function() {
                    self.currentTab = this.getAttribute('data-policy-tab');
                    self.updateTabUI();
                    self.load();
                });
            });
        },

        destroy: function() {
            if (this.autoRefreshInterval) {
                clearInterval(this.autoRefreshInterval);
                this.autoRefreshInterval = null;
            }
            this.initialized = false;
        },

        load: async function() {
            try {
                var container = document.getElementById('policy-content');
                if (!container) return;

                window.DashboardDOM.showLoading(container, __('policy.loading'));

                if (this.currentTab === 'route') {
                    var data = await window.DashboardApi.getPolicies('routes');
                    this.renderRoutePolicies(data, container);
                } else {
                    var data = await window.DashboardApi.getPolicies('clusters');
                    this.renderClusterPolicies(data, container);
                }
                this.updateRefreshTime();
            } catch (error) {
                console.error('[Policy] Load failed:', error);
                var container = document.getElementById('policy-content');
                if (container) {
                    container.innerHTML = '<div class="alert alert-danger">' + __('policy.loadFailed') + '</div>';
                }
            }
        },

        updateTabUI: function() {
            var btns = document.querySelectorAll('[data-policy-tab]');
            btns.forEach(function(btn) {
                var tab = btn.getAttribute('data-policy-tab');
                btn.className = tab === PolicyModule.currentTab
                    ? 'btn btn-sm btn-primary'
                    : 'btn btn-sm btn-outline-secondary';
            });
        },

        // ─── Route Policies ─────────────────────────────

        renderRoutePolicies: function(data, container) {
            window.DashboardDOM.clear(container);
            var policies = (data && data.data) || data || [];

            if (policies.length === 0) {
                container.innerHTML =
                    '<div class="text-center py-5">' +
                        '<i class="bi bi-sliders text-muted" style="font-size:48px;"></i>' +
                        '<p class="text-muted mt-3">' + __('policy.empty') + '</p>' +
                        '<p class="text-muted small">' + __('policy.emptyHelp') + '</p>' +
                    '</div>';
                return;
            }

            var rows = policies.map(function(policy) {
                var enabledBadge = policy.enabled
                    ? '<span class="badge bg-success">' + __('policy.enabled') + '</span>'
                    : '<span class="badge bg-secondary">' + __('policy.disabled') + '</span>';
                var features = [];
                if (policy.retry && policy.retry.enabled) features.push(__('policy.retry'));
                if (policy.rateLimit && policy.rateLimit.enabled) features.push(__('policy.rateLimit'));
                if (policy.wafEnabled === true) features.push(__('policy.wafOn'));
                else if (policy.wafEnabled === false) features.push(__('policy.wafOff'));
                var featureBadges = features.length > 0
                    ? features.map(function(f) { return '<span class="badge bg-light text-dark border me-1">' + window.DashboardUtils.escapeHtml(f) + '</span>'; }).join('')
                    : '<span class="text-muted">-</span>';
                var createdAt = policy.createdAt ? window.DashboardI18n.formatDate(policy.createdAt) : '-';
                var routeCount = (policy.appliedRoutes && policy.appliedRoutes.length) || 0;
                var toggleIcon = policy.enabled ? 'bi-toggle-on text-success' : 'bi-toggle-off text-secondary';

                return '<tr class="align-middle">' +
                    '<td><code>' + window.DashboardUtils.escapeHtml(policy.policyId) + '</code></td>' +
                    '<td>' + window.DashboardUtils.escapeHtml(policy.displayName || '-') + '</td>' +
                    '<td>' + enabledBadge + '</td>' +
                    '<td><div class="d-flex flex-wrap gap-1">' + featureBadges + '</div></td>' +
                    '<td class="text-center"><span class="badge bg-info">' + routeCount + '</span></td>' +
                    '<td class="text-muted small">' + createdAt + '</td>' +
                    '<td>' +
                        '<button class="btn btn-sm btn-outline-secondary me-1" onclick="PolicyModule.openApplyModal(\'route\',\'' + window.DashboardUtils.escapeHtml(policy.policyId) + '\')" title="' + __('policy.apply') + '"><i class="bi bi-link-45deg"></i></button>' +
                        '<button class="btn btn-sm btn-outline-primary me-1" onclick="PolicyModule.openEditModal(\'route\',\'' + window.DashboardUtils.escapeHtml(policy.policyId) + '\')" title="' + __('policy.edit') + '"><i class="bi bi-pencil"></i></button>' +
                        '<button class="btn btn-sm btn-outline-danger" onclick="PolicyModule.deletePolicy(\'route\',\'' + window.DashboardUtils.escapeHtml(policy.policyId) + '\')" title="' + __('policy.delete') + '"><i class="bi bi-trash"></i></button>' +
                    '</td>' +
                '</tr>';
            }).join('');

            container.innerHTML =
                '<div class="table-responsive">' +
                    '<table class="table table-hover align-middle">' +
                        '<thead><tr>' +
                            '<th>' + __('policy.name') + '</th>' +
                            '<th>' + __('policy.displayName') + '</th>' +
                            '<th>' + __('policy.status') + '</th>' +
                            '<th>' + __('policy.features') + '</th>' +
                            '<th class="text-center">' + __('policy.appliedRoutes') + '</th>' +
                            '<th>' + __('policy.createdTime') + '</th>' +
                            '<th style="width:130px">' + __('policy.actions') + '</th>' +
                        '</tr></thead>' +
                        '<tbody>' + rows + '</tbody>' +
                    '</table>' +
                '</div>';
        },

        // ─── Cluster Policies ───────────────────────────

        renderClusterPolicies: function(data, container) {
            window.DashboardDOM.clear(container);
            var policies = (data && data.data) || data || [];

            if (policies.length === 0) {
                container.innerHTML =
                    '<div class="text-center py-5">' +
                        '<i class="bi bi-lightning-charge text-muted" style="font-size:48px;"></i>' +
                        '<p class="text-muted mt-3">' + __('policy.emptyCluster') + '</p>' +
                    '</div>';
                return;
            }

            var rows = policies.map(function(policy) {
                var enabledBadge = policy.enabled
                    ? '<span class="badge bg-success">' + __('policy.enabled') + '</span>'
                    : '<span class="badge bg-secondary">' + __('policy.disabled') + '</span>';
                var cb = policy.circuitBreaker || {};
                var cbSummary = cb.enabled !== false
                    ? (cb.failureThreshold || 5) + '/' + (cb.recoveryTimeoutSeconds || 30) + 's'
                    : __('policy.disabled');
                var createdAt = policy.createdAt ? window.DashboardI18n.formatDate(policy.createdAt) : '-';
                var clusterCount = (policy.appliedClusters && policy.appliedClusters.length) || 0;

                return '<tr class="align-middle">' +
                    '<td><code>' + window.DashboardUtils.escapeHtml(policy.policyId) + '</code></td>' +
                    '<td>' + window.DashboardUtils.escapeHtml(policy.displayName || '-') + '</td>' +
                    '<td>' + enabledBadge + '</td>' +
                    '<td><code>' + window.DashboardUtils.escapeHtml(cbSummary) + '</code></td>' +
                    '<td class="text-center"><span class="badge bg-info">' + clusterCount + '</span></td>' +
                    '<td class="text-muted small">' + createdAt + '</td>' +
                    '<td>' +
                        '<button class="btn btn-sm btn-outline-secondary me-1" onclick="PolicyModule.openApplyModal(\'cluster\',\'' + window.DashboardUtils.escapeHtml(policy.policyId) + '\')" title="' + __('policy.apply') + '"><i class="bi bi-link-45deg"></i></button>' +
                        '<button class="btn btn-sm btn-outline-primary me-1" onclick="PolicyModule.openEditModal(\'cluster\',\'' + window.DashboardUtils.escapeHtml(policy.policyId) + '\')" title="' + __('policy.edit') + '"><i class="bi bi-pencil"></i></button>' +
                        '<button class="btn btn-sm btn-outline-danger" onclick="PolicyModule.deletePolicy(\'cluster\',\'' + window.DashboardUtils.escapeHtml(policy.policyId) + '\')" title="' + __('policy.delete') + '"><i class="bi bi-trash"></i></button>' +
                    '</td>' +
                '</tr>';
            }).join('');

            container.innerHTML =
                '<div class="table-responsive">' +
                    '<table class="table table-hover align-middle">' +
                        '<thead><tr>' +
                            '<th>' + __('policy.name') + '</th>' +
                            '<th>' + __('policy.displayName') + '</th>' +
                            '<th>' + __('policy.status') + '</th>' +
                            '<th>' + __('policy.circuitBreaker') + '</th>' +
                            '<th class="text-center">' + __('policy.appliedClusters') + '</th>' +
                            '<th>' + __('policy.createdTime') + '</th>' +
                            '<th style="width:130px">' + __('policy.actions') + '</th>' +
                        '</tr></thead>' +
                        '<tbody>' + rows + '</tbody>' +
                    '</table>' +
                '</div>';
        },

        // ─── Create / Edit ──────────────────────────────

        openCreateModal: function() {
            this.editingId = null;
            this.editingType = this.currentTab;
            this.renderFormForType(this.currentTab);
            if (this.modal) this.modal.show();
        },

        openEditModal: async function(type, policyId) {
            try {
                var endpoint = type === 'route' ? 'routes' : 'clusters';
                var data = await window.DashboardApi.getPolicy(endpoint, policyId);
                var policy = data.data || data;
                this.editingId = policyId;
                this.editingType = type;
                this.renderFormForType(type, policy);
                if (this.modal) this.modal.show();
            } catch (error) {
                console.error('[Policy] Load policy failed:', error);
            }
        },

        renderFormForType: function(type, existing) {
            var isEdit = !!existing;
            var title = isEdit ? __('policy.edit') : __('policy.add');
            var typeName = type === 'route' ? __('policy.routePolicy') : __('policy.clusterPolicy');
            document.getElementById('policyModalTitle').textContent = title + ' - ' + typeName;
            document.getElementById('policySaveBtn').querySelector('span').textContent = isEdit ? __('policy.update') : __('policy.create');

            var body = document.getElementById('policyModalBody');
            var policy = existing || {};

            if (type === 'route') {
                var retry = policy.retry || {};
                var rl = policy.rateLimit || {};
                var wafVal = policy.wafEnabled;
                var wafTrue = wafVal === true ? 'checked' : '';
                var wafFalse = wafVal === false ? 'checked' : '';
                var wafDefault = wafVal == null ? 'checked' : '';

                body.innerHTML =
                    '<form id="policyForm">' +
                        '<input type="hidden" id="policy-type-input" value="route" />' +
                        '<input type="hidden" id="policy-id-hidden" value="' + window.DashboardUtils.escapeHtml(policy.policyId || '') + '" />' +
                        '<div class="row g-3 mb-3">' +
                            '<div class="col-md-6">' +
                                '<label class="form-label">' + __('policy.name') + '</label>' +
                                '<input type="text" class="form-control" id="policy-id-input" placeholder="e.g.: high-reliability" value="' + window.DashboardUtils.escapeHtml(policy.policyId || '') + '" ' + (isEdit ? 'disabled' : '') + ' />' +
                            '</div>' +
                            '<div class="col-md-6">' +
                                '<label class="form-label">' + __('policy.displayName') + '</label>' +
                                '<input type="text" class="form-control" id="policy-displayName" value="' + window.DashboardUtils.escapeHtml(policy.displayName || '') + '" />' +
                            '</div>' +
                        '</div>' +
                        '<div class="mb-3">' +
                            '<label class="form-label">' + __('policy.description') + '</label>' +
                            '<textarea class="form-control" id="policy-description" rows="2">' + window.DashboardUtils.escapeHtml(policy.description || '') + '</textarea>' +
                        '</div>' +

                        '<div class="border rounded p-3 mb-3">' +
                            '<div class="d-flex justify-content-between align-items-center mb-2">' +
                                '<h6 class="mb-0"><i class="bi bi-arrow-repeat me-2"></i>' + __('policy.retry') + '</h6>' +
                                '<div class="form-check form-switch"><input class="form-check-input" type="checkbox" id="retry-enabled" ' + (retry.enabled ? 'checked' : '') + '></div>' +
                            '</div>' +
                            '<div class="row g-3">' +
                                '<div class="col-md-6"><label class="form-label">' + __('policy.maxRetries') + '</label><input type="number" class="form-control" id="retry-max" value="' + (retry.maxRetries || 3) + '" /></div>' +
                                '<div class="col-md-6"><label class="form-label">' + __('policy.backoffBase') + '</label><input type="number" class="form-control" id="retry-backoff" value="' + (retry.backoffBaseMs || 100) + '" /></div>' +
                            '</div>' +
                        '</div>' +

                        '<div class="border rounded p-3 mb-3">' +
                            '<div class="d-flex justify-content-between align-items-center mb-2">' +
                                '<h6 class="mb-0"><i class="bi bi-speedometer2 me-2"></i>' + __('policy.rateLimit') + '</h6>' +
                                '<div class="form-check form-switch"><input class="form-check-input" type="checkbox" id="rl-enabled" ' + (rl.enabled ? 'checked' : '') + '></div>' +
                            '</div>' +
                            '<div class="row g-3">' +
                                '<div class="col-md-4"><label class="form-label">' + __('policy.limit') + '</label><input type="number" class="form-control" id="rl-limit" value="' + (rl.permitLimit || 100) + '" /></div>' +
                                '<div class="col-md-4"><label class="form-label">' + __('policy.window') + '</label><input type="text" class="form-control" id="rl-window" value="' + (rl.window || '1m') + '" /></div>' +
                                '<div class="col-md-4"><label class="form-label">' + __('policy.algorithm') + '</label>' +
                                    '<select class="form-select" id="rl-algorithm">' +
                                        '<option value="FixedWindow"' + (rl.algorithm === 'FixedWindow' ? ' selected' : '') + '>FixedWindow</option>' +
                                        '<option value="SlidingWindow"' + (rl.algorithm === 'SlidingWindow' ? ' selected' : '') + '>SlidingWindow</option>' +
                                        '<option value="TokenBucket"' + (rl.algorithm === 'TokenBucket' ? ' selected' : '') + '>TokenBucket</option>' +
                                    '</select>' +
                                '</div>' +
                            '</div>' +
                        '</div>' +

                        '<div class="border rounded p-3">' +
                            '<div class="d-flex justify-content-between align-items-center mb-2">' +
                                '<h6 class="mb-0"><i class="bi bi-shield-lock me-2"></i>WAF</h6>' +
                            '</div>' +
                            '<div class="form-check"><input class="form-check-input" type="radio" name="waf-mode" id="waf-on" value="true" ' + wafTrue + ' /><label class="form-check-label" for="waf-on">' + __('policy.wafForceOn') + '</label></div>' +
                            '<div class="form-check"><input class="form-check-input" type="radio" name="waf-mode" id="waf-off" value="false" ' + wafFalse + ' /><label class="form-check-label" for="waf-off">' + __('policy.wafForceOff') + '</label></div>' +
                            '<div class="form-check"><input class="form-check-input" type="radio" name="waf-mode" id="waf-default" value="null" ' + wafDefault + ' /><label class="form-check-label" for="waf-default">' + __('policy.wafFollowGlobal') + '</label></div>' +
                        '</div>' +
                    '</form>';
            } else {
                var cb = policy.circuitBreaker || {};
                body.innerHTML =
                    '<form id="policyForm">' +
                        '<input type="hidden" id="policy-type-input" value="cluster" />' +
                        '<input type="hidden" id="policy-id-hidden" value="' + window.DashboardUtils.escapeHtml(policy.policyId || '') + '" />' +
                        '<div class="row g-3 mb-3">' +
                            '<div class="col-md-6">' +
                                '<label class="form-label">' + __('policy.name') + '</label>' +
                                '<input type="text" class="form-control" id="policy-id-input" placeholder="e.g.: fast-trip" value="' + window.DashboardUtils.escapeHtml(policy.policyId || '') + '" ' + (isEdit ? 'disabled' : '') + ' />' +
                            '</div>' +
                            '<div class="col-md-6">' +
                                '<label class="form-label">' + __('policy.displayName') + '</label>' +
                                '<input type="text" class="form-control" id="policy-displayName" value="' + window.DashboardUtils.escapeHtml(policy.displayName || '') + '" />' +
                            '</div>' +
                        '</div>' +
                        '<div class="mb-3">' +
                            '<label class="form-label">' + __('policy.description') + '</label>' +
                            '<textarea class="form-control" id="policy-description" rows="2">' + window.DashboardUtils.escapeHtml(policy.description || '') + '</textarea>' +
                        '</div>' +
                        '<div class="border rounded p-3">' +
                            '<div class="d-flex justify-content-between align-items-center mb-2">' +
                                '<h6 class="mb-0"><i class="bi bi-lightning-charge me-2"></i>' + __('policy.circuitBreaker') + '</h6>' +
                                '<div class="form-check form-switch"><input class="form-check-input" type="checkbox" id="cb-enabled" ' + (cb.enabled !== false ? 'checked' : '') + '></div>' +
                            '</div>' +
                            '<div class="row g-3">' +
                                '<div class="col-md-4"><label class="form-label">' + __('policy.failureThreshold') + '</label><input type="number" class="form-control" id="cb-threshold" value="' + (cb.failureThreshold || 5) + '" /></div>' +
                                '<div class="col-md-4"><label class="form-label">' + __('policy.recoveryTimeout') + '</label><input type="number" class="form-control" id="cb-timeout" value="' + (cb.recoveryTimeoutSeconds || 30) + '" /></div>' +
                                '<div class="col-md-4"><label class="form-label">' + __('policy.halfOpenMax') + '</label><input type="number" class="form-control" id="cb-halfopen" value="' + (cb.halfOpenMaxAttempts || 1) + '" /></div>' +
                            '</div>' +
                        '</div>' +
                    '</form>';
            }
        },

        // ─── Save ──────────────────────────────────────

        save: async function() {
            var type = document.getElementById('policy-type-input').value;
            var policyId = this.editingId || document.getElementById('policy-id-input').value.trim();
            if (!policyId) {
                if (window.DashboardModals) window.DashboardModals.showError(__('modal.idRequired'));
                return;
            }

            var endpoint = type === 'route' ? 'routes' : 'clusters';
            var policy;

            if (type === 'route') {
                var wafRadio = document.querySelector('input[name="waf-mode"]:checked');
                var wafVal = wafRadio ? wafRadio.value : 'null';
                policy = {
                    policyId: policyId,
                    displayName: document.getElementById('policy-displayName').value.trim(),
                    description: document.getElementById('policy-description').value.trim(),
                    enabled: true,
                    retry: {
                        enabled: document.getElementById('retry-enabled').checked,
                        maxRetries: parseInt(document.getElementById('retry-max').value) || 3,
                        backoffBaseMs: parseInt(document.getElementById('retry-backoff').value) || 100
                    },
                    rateLimit: {
                        enabled: document.getElementById('rl-enabled').checked,
                        permitLimit: parseInt(document.getElementById('rl-limit').value) || 100,
                        window: document.getElementById('rl-window').value || '1m',
                        algorithm: document.getElementById('rl-algorithm').value || 'FixedWindow'
                    },
                    wafEnabled: wafVal === 'true' ? true : (wafVal === 'false' ? false : null)
                };
            } else {
                policy = {
                    policyId: policyId,
                    displayName: document.getElementById('policy-displayName').value.trim(),
                    description: document.getElementById('policy-description').value.trim(),
                    enabled: true,
                    circuitBreaker: {
                        enabled: document.getElementById('cb-enabled').checked,
                        failureThreshold: parseInt(document.getElementById('cb-threshold').value) || 5,
                        recoveryTimeoutSeconds: parseInt(document.getElementById('cb-timeout').value) || 30,
                        halfOpenMaxAttempts: parseInt(document.getElementById('cb-halfopen').value) || 1
                    }
                };
            }

            try {
                if (this.editingId) {
                    await window.DashboardApi.updatePolicy(endpoint, this.editingId, policy);
                    if (window.DashboardModals) window.DashboardModals.showToast(__('policy.updated'), 'success');
                } else {
                    await window.DashboardApi.createPolicy(endpoint, policy);
                    if (window.DashboardModals) window.DashboardModals.showToast(__('policy.created'), 'success');
                }
                if (this.modal) this.modal.hide();
                await this.load();
            } catch (error) {
                console.error('[Policy] Save failed:', error);
                if (window.DashboardModals) {
                    window.DashboardModals.showError(this.editingId ? __('policy.saveFailed') : __('policy.createdFailed'));
                }
            }
        },

        // ─── Delete ─────────────────────────────────────

        deletePolicy: async function(type, policyId) {
            var endpoint = type === 'route' ? 'routes' : 'clusters';
            var msg = __('policy.deleteConfirm').replace('{name}', policyId);
            if (!confirm(msg)) return;
            try {
                await window.DashboardApi.deletePolicy(endpoint, policyId);
                if (window.DashboardModals) window.DashboardModals.showToast(__('policy.deleteSuccess'), 'success');
                await this.load();
            } catch (error) {
                console.error('[Policy] Delete failed:', error);
                if (window.DashboardModals) window.DashboardModals.showError(__('policy.deleteFailed'));
            }
        },

        // ─── Apply / Unapply ────────────────────────────

        openApplyModal: async function(type, policyId) {
            var endpoint = type === 'route' ? 'routes' : 'clusters';
            try {
                var targets = type === 'route'
                    ? await window.DashboardApi.getRoutes()
                    : await window.DashboardApi.getClusters();
                var items = (targets && targets.data) || targets || [];
                var policy = type === 'route'
                    ? await window.DashboardApi.getPolicy('routes', policyId)
                    : await window.DashboardApi.getPolicy('clusters', policyId);
                var policyData = policy.data || policy;
                var applied = type === 'route'
                    ? (policyData.appliedRoutes || [])
                    : (policyData.appliedClusters || []);

                var itemRows = items.map(function(item) {
                    var id = type === 'route' ? item.routeId : item.clusterId;
                    var label = type === 'route' ? (item.matchPath || id) : id;
                    var isApplied = applied.indexOf(id) >= 0;
                    return '<div class="form-check">' +
                        '<input class="form-check-input policy-target-check" type="checkbox" value="' + window.DashboardUtils.escapeHtml(id) + '" id="target-' + window.DashboardUtils.escapeHtml(id) + '" ' + (isApplied ? 'checked' : '') + ' />' +
                        '<label class="form-check-label" for="target-' + window.DashboardUtils.escapeHtml(id) + '">' + window.DashboardUtils.escapeHtml(label) + '</label>' +
                    '</div>';
                }).join('');

                var title = __('policy.applyTitle') + ': ' + policyId;
                var body = '<div class="mb-2 text-muted small">' + __('policy.applyHelp' + (type === 'route' ? 'Route' : 'Cluster')) + '</div>' +
                    '<div style="max-height:300px;overflow-y:auto">' + itemRows + '</div>';

                document.getElementById('policyModalTitle').textContent = title;
                document.getElementById('policyModalBody').innerHTML = body;
                document.getElementById('policySaveBtn').onclick = function() { PolicyModule.saveApply(type, policyId); };
                document.getElementById('policySaveBtn').querySelector('span').textContent = __('policy.confirm');
                document.getElementById('policy-type-input') || document.body.insertAdjacentHTML('beforeend', '<input type="hidden" id="policy-apply-type" value="' + type + '" />');
                var hiddenType = document.getElementById('policy-apply-type');
                if (hiddenType) hiddenType.value = type;

                if (this.modal) this.modal.show();
            } catch (error) {
                console.error('[Policy] Load targets failed:', error);
            }
        },

        saveApply: async function(type, policyId) {
            var apiType = type === 'route' ? 'routes' : 'clusters';
            var checkboxes = document.querySelectorAll('.policy-target-check');
            var promises = [];

            checkboxes.forEach(function(cb) {
                var targetId = cb.value;
                if (cb.checked) {
                    promises.push(window.DashboardApi.applyPolicy(apiType, policyId, targetId));
                } else {
                    promises.push(window.DashboardApi.unapplyPolicy(apiType, policyId, targetId).catch(function() {
                    if (window.DashboardModals) window.DashboardModals.showToast(__('policy.unapplyFailed'), 'warning');
                }));
                }
            });

            try {
                await Promise.all(promises);
                if (window.DashboardModals) window.DashboardModals.showToast(__('policy.applySuccess'), 'success');
                if (this.modal) this.modal.hide();
                document.getElementById('policySaveBtn').onclick = function() { PolicyModule.save(); };
                await this.load();
            } catch (error) {
                console.error('[Policy] Apply failed:', error);
                if (window.DashboardModals) window.DashboardModals.showError(__('policy.applyFailed'));
            }
        },

        updateRefreshTime: function() {
            var el = document.getElementById('policy-refresh-time');
            if (el) {
                el.textContent = window.DashboardI18n.formatDate(new Date());
            }
        }
    };

    if (window.DashboardApp) {
        window.DashboardApp.registerModule('policy', PolicyModule);
    }
    window.PolicyModule = PolicyModule;
})();
