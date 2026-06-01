/**
 * Policy Management Module - Gateway policy CRUD
 */
(function() {
    'use strict';

    var PolicyModule = {
        name: 'policy',
        initialized: false,
        autoRefreshInterval: null,
        editingId: null,
        modal: null,

        init: function() {
            if (this.initialized) return;
            this.setupEvents();
            this.initialized = true;
            var self = this;
            setTimeout(function() {
                if (window.bootstrap) {
                    self.modal = new window.bootstrap.Modal(document.getElementById('policyModal'), { backdrop: true });
                }
            }, 100);
        },

        setupEvents: function() {
            var self = this;
            document.addEventListener('dashboard:ready', function() {
                self.autoRefreshInterval = setInterval(function() {
                    self.load();
                }, 30000);
            });
            document.addEventListener('dashboard:localeChange', function() { self.load(); });
        },

        load: async function() {
            try {
                var container = document.getElementById('policy-content');
                if (!container) return;

                window.DashboardDOM.showLoading(container, __('policy.loading'));

                var data = await window.DashboardApi.getPolicies();
                this.render(data, container);
                this.updateRefreshTime();
            } catch (error) {
                console.error('[Policy] Load failed:', error);
                var container = document.getElementById('policy-content');
                if (container) {
                    container.innerHTML = '<div class="alert alert-danger">' + __('policy.loadFailed') + '</div>';
                }
            }
        },

        getFeatureSummary: function(policy) {
            var features = [];
            if (policy.circuitBreaker && policy.circuitBreaker.enabled) features.push(__('policy.circuitBreaker'));
            if (policy.retry && policy.retry.enabled) features.push(__('policy.retry'));
            if (policy.rateLimit && policy.rateLimit.enabled) features.push(__('policy.rateLimit'));
            if (policy.waf && policy.waf.enabled) features.push(__('policy.waf'));
            if (policy.customPlugins && Object.keys(policy.customPlugins).length > 0) features.push(__('policy.plugins'));
            return features.length > 0 ? features : ['-'];
        },

        render: function(data, container) {
            window.DashboardDOM.clear(container);

            var policies = (data && data.policies) || [];

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
                var features = this.getFeatureSummary(policy);
                var featureBadges = features.map(function(f) {
                    return '<span class="badge bg-light text-dark border me-1">' + window.DashboardUtils.escapeHtml(f) + '</span>';
                }).join('');
                var createdAt = policy.createdAt ? window.DashboardI18n.formatDate(policy.createdAt) : '-';
                var toggleIcon = policy.enabled ? 'bi-toggle-on text-success' : 'bi-toggle-off text-secondary';

                return '<tr class="align-middle">' +
                    '<td><code>' + window.DashboardUtils.escapeHtml(policy.policyId) + '</code></td>' +
                    '<td>' + window.DashboardUtils.escapeHtml(policy.displayName || '-') + '</td>' +
                    '<td class="text-muted small">' + window.DashboardUtils.escapeHtml(policy.description || '') + '</td>' +
                    '<td>' + enabledBadge + '</td>' +
                    '<td><div class="d-flex flex-wrap gap-1">' + featureBadges + '</div></td>' +
                    '<td class="text-center"><strong>' + policy.priority + '</strong></td>' +
                    '<td class="text-muted small">' + createdAt + '</td>' +
                    '<td>' +
                        '<button class="btn btn-sm btn-outline-secondary me-1" onclick="PolicyModule.togglePolicy(\'' + window.DashboardUtils.escapeHtml(policy.policyId) + '\', ' + !policy.enabled + ')" title="' + __('policy.toggle') + '">' +
                            '<i class="bi ' + toggleIcon + '"></i>' +
                        '</button>' +
                        '<button class="btn btn-sm btn-outline-primary me-1" onclick="PolicyModule.openEditModal(\'' + window.DashboardUtils.escapeHtml(policy.policyId) + '\')" title="' + __('policy.edit') + '">' +
                            '<i class="bi bi-pencil"></i>' +
                        '</button>' +
                        '<button class="btn btn-sm btn-outline-danger" onclick="PolicyModule.deletePolicy(\'' + window.DashboardUtils.escapeHtml(policy.policyId) + '\')" title="' + __('policy.delete') + '">' +
                            '<i class="bi bi-trash"></i>' +
                        '</button>' +
                    '</td>' +
                '</tr>';
            }.bind(this)).join('');

            container.innerHTML =
                '<div class="table-responsive">' +
                    '<table class="table table-hover align-middle">' +
                        '<thead>' +
                            '<tr>' +
                                '<th>' + __('policy.name') + '</th>' +
                                '<th>' + __('policy.displayName') + '</th>' +
                                '<th>' + __('policy.description') + '</th>' +
                                '<th>Status</th>' +
                                '<th>Features</th>' +
                                '<th class="text-center">' + __('policy.priority') + '</th>' +
                                '<th>Created</th>' +
                                '<th style="width:140px">' + __('circuit.state') + '</th>' +
                            '</tr>' +
                        '</thead>' +
                        '<tbody>' + rows + '</tbody>' +
                    '</table>' +
                '</div>';
        },

        openCreateModal: function() {
            this.editingId = null;
            document.getElementById('policyModalTitle').textContent = __('policy.add');
            document.getElementById('policySaveBtn').querySelector('span').textContent = __('policy.create');
            document.getElementById('policy-id-input').disabled = false;
            document.getElementById('policyForm').reset();
            document.getElementById('policy-id').value = '';
            document.getElementById('policy-enabled').checked = true;
            if (this.modal) this.modal.show();
        },

        openEditModal: async function(policyId) {
            try {
                var data = await window.DashboardApi.getPolicy(policyId);
                this.editingId = policyId;
                document.getElementById('policyModalTitle').textContent = __('policy.edit');
                document.getElementById('policySaveBtn').querySelector('span').textContent = __('policy.update');
                document.getElementById('policy-id-input').disabled = true;
                document.getElementById('policy-id').value = data.policyId;
                document.getElementById('policy-id-input').value = data.policyId;
                document.getElementById('policy-displayName').value = data.displayName || '';
                document.getElementById('policy-description').value = data.description || '';
                document.getElementById('policy-priority').value = data.priority || 50;
                document.getElementById('policy-enabled').checked = data.enabled !== false;

                var cb = data.circuitBreaker || {};
                document.getElementById('cb-enabled').checked = cb.enabled !== false;
                document.getElementById('cb-threshold').value = cb.failureThreshold || 5;
                document.getElementById('cb-timeout').value = cb.recoveryTimeoutSeconds || 30;
                document.getElementById('cb-halfopen').value = cb.halfOpenMaxAttempts || 1;

                var retry = data.retry || {};
                document.getElementById('retry-enabled').checked = retry.enabled === true;
                document.getElementById('retry-max').value = retry.maxRetries || 3;
                document.getElementById('retry-backoff').value = retry.backoffBaseMs || 100;

                var rl = data.rateLimit || {};
                document.getElementById('rl-enabled').checked = rl.enabled === true;
                document.getElementById('rl-limit').value = rl.permitLimit || 100;
                document.getElementById('rl-window').value = rl.window || '1m';
                document.getElementById('rl-algorithm').value = rl.algorithm || 'FixedWindow';

                var waf = data.waf || {};
                document.getElementById('waf-enabled').checked = waf.enabled === true;
                document.getElementById('waf-sqli').checked = waf.blockSqlInjection !== false;
                document.getElementById('waf-xss').checked = waf.blockXss !== false;
                document.getElementById('waf-path').checked = waf.blockPathTraversal !== false;

                if (this.modal) this.modal.show();
            } catch (error) {
                console.error('[Policy] Load policy failed:', error);
            }
        },

        save: async function() {
            var policyId = document.getElementById('policy-id-input').value.trim();
            if (!policyId) {
                if (window.DashboardModals) window.DashboardModals.showError(__('modal.idRequired'));
                return;
            }

            var policy = {
                policyId: policyId,
                displayName: document.getElementById('policy-displayName').value.trim(),
                description: document.getElementById('policy-description').value.trim(),
                priority: parseInt(document.getElementById('policy-priority').value) || 50,
                enabled: document.getElementById('policy-enabled').checked,
                circuitBreaker: {
                    enabled: document.getElementById('cb-enabled').checked,
                    failureThreshold: parseInt(document.getElementById('cb-threshold').value) || 5,
                    recoveryTimeoutSeconds: parseInt(document.getElementById('cb-timeout').value) || 30,
                    halfOpenMaxAttempts: parseInt(document.getElementById('cb-halfopen').value) || 1
                },
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
                waf: {
                    enabled: document.getElementById('waf-enabled').checked,
                    blockSqlInjection: document.getElementById('waf-sqli').checked,
                    blockXss: document.getElementById('waf-xss').checked,
                    blockPathTraversal: document.getElementById('waf-path').checked
                }
            };

            try {
                if (this.editingId) {
                    await window.DashboardApi.updatePolicy(this.editingId, policy);
                    if (window.DashboardModals) window.DashboardModals.showToast(__('policy.updated'), 'success');
                } else {
                    await window.DashboardApi.createPolicy(policy);
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

        togglePolicy: async function(policyId, enable) {
            try {
                await window.DashboardApi.togglePolicy(policyId);
                if (window.DashboardModals) window.DashboardModals.showToast(__('policy.toggleSuccess'), 'success');
                await this.load();
            } catch (error) {
                console.error('[Policy] Toggle failed:', error);
                if (window.DashboardModals) window.DashboardModals.showError(__('policy.toggleFailed'));
            }
        },

        deletePolicy: async function(policyId) {
            var msg = __('policy.deleteConfirm').replace('{name}', policyId);
            if (!confirm(msg)) return;
            try {
                await window.DashboardApi.deletePolicy(policyId);
                if (window.DashboardModals) window.DashboardModals.showToast(__('policy.deleteSuccess'), 'success');
                await this.load();
            } catch (error) {
                console.error('[Policy] Delete failed:', error);
                if (window.DashboardModals) window.DashboardModals.showError(__('policy.deleteFailed'));
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
