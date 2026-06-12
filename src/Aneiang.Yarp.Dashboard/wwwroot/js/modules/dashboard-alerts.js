/**
 * Alert Center Module - Alert history viewer and management
 */
(function() {
    'use strict';

    var AlertModule = {
        name: 'alert',
        initialized: false,
        autoRefreshInterval: null,
        currentFilter: { type: '', severity: '' },

        init: function() {
            if (this.initialized) return;
            this.setupEvents();
            this.initialized = true;
        },

        setupEvents: function() {
            var self = this;
            document.addEventListener('dashboard:ready', function() {
                if (self.autoRefreshInterval) clearInterval(self.autoRefreshInterval);
                self.autoRefreshInterval = setInterval(function() {
                    self.load();
                }, 20000);
                self.loadChannelStatus();
            });
            document.addEventListener('dashboard:localeChange', function() {
                self.load();
                self.loadChannelStatus();
            });
        },

        destroy: function() {
            if (this.autoRefreshInterval) {
                clearInterval(this.autoRefreshInterval);
                this.autoRefreshInterval = null;
            }
            this.initialized = false;
        },

        loadChannelStatus: async function() {
            try {
                var prefix = window.__dashboard ? window.__dashboard.basePath : '';
                var linkEl = document.getElementById('alert-goto-notif');
                if (linkEl) linkEl.href = prefix + '/notifications';

                var whSettings = await window.DashboardApi.endpoints.getWebhookSettings();
                var alertSettings = await window.DashboardApi.get('/api/config/alert-settings');
                var banner = document.getElementById('alert-channel-banner');
                var body = document.getElementById('alert-channel-body');
                if (!banner || !body) return;

                var dingCount = (whSettings && whSettings.dingtalk) ? whSettings.dingtalk.length : 0;
                var genCount = (whSettings && whSettings.generic) ? whSettings.generic.length : 0;
                var totalEndpoints = dingCount + genCount;
                var alertOn = alertSettings ? alertSettings.alertEnabled !== false : false;

                var html = '<div style="display:flex;align-items:center;gap:12px;flex-wrap:wrap;">';

                // Alert switch status
                html += '<span style="display:inline-flex;align-items:center;gap:6px;padding:6px 14px;border-radius:8px;font-size:13px;font-weight:500;' +
                    'background:' + (alertOn ? '#fef3c7' : '#f1f5f9') + ';color:' + (alertOn ? '#92400e' : '#64748b') + ';border:1px solid ' + (alertOn ? '#fcd34d' : '#e2e8f0') + ';">' +
                    '<i class="bi ' + (alertOn ? 'bi-bell-fill' : 'bi-bell-slash') + '"></i> ' +
                    (alertOn ? (typeof __ === 'function' ? __('alert.alertOn') : '告警已开启') : (typeof __ === 'function' ? __('alert.alertOff') : '告警已关闭')) + '</span>';

                // Webhook status
                html += '<span style="display:inline-flex;align-items:center;gap:6px;padding:6px 14px;border-radius:8px;font-size:13px;font-weight:500;' +
                    'background:' + (totalEndpoints > 0 ? '#eef2ff' : '#f1f5f9') + ';color:' + (totalEndpoints > 0 ? '#4338ca' : '#64748b') + ';border:1px solid ' + (totalEndpoints > 0 ? '#c7d2fe' : '#e2e8f0') + ';">' +
                    '<i class="bi ' + (totalEndpoints > 0 ? 'bi-megaphone-fill' : 'bi-megaphone') + '"></i> ' +
                    (totalEndpoints > 0
                        ? (typeof __ === 'function' ? __('notif.channelsActive') : '通知渠道已启用') + ' (' + totalEndpoints + ')'
                        : (typeof __ === 'function' ? __('notif.channelsInactive') : '通知渠道未配置')) + '</span>';

                // Per-alert-type indicators
                if (alertOn && alertSettings) {
                    var types = [
                        { key: 'alertCircuitBreakerOpen', label: 'CircuitBreaker', color: '#ef4444' },
                        { key: 'alertRetryExhausted', label: 'RetryExhausted', color: '#f97316' },
                        { key: 'alertWafBlocks', label: 'WafBlock', color: '#8b5cf6' },
                        { key: 'alertProxyErrors', label: 'ProxyError', color: '#ef4444' },
                        { key: 'alertRateLimitExceeded', label: 'RateLimit', color: '#06b6d4' }
                    ];
                    types.forEach(function(t) {
                        var isOn = alertSettings[t.key] !== false;
                        html += '<span style="display:inline-flex;align-items:center;gap:4px;padding:4px 10px;border-radius:6px;font-size:11px;font-weight:500;' +
                            'background:' + (isOn ? t.color + '10' : '#f8fafc') + ';color:' + (isOn ? t.color : '#94a3b8') + ';border:1px solid ' + (isOn ? t.color + '30' : '#e2e8f0') + ';">' +
                            '<i class="bi ' + (isOn ? 'bi-check-circle-fill' : 'bi-x-circle') + '" style="font-size:10px;"></i> ' + t.label + '</span>';
                    });
                }

                html += '</div>';
                body.innerHTML = html;
                banner.style.display = '';
            } catch (e) {
                console.error('[Alert] Channel status load failed:', e);
            }
        },

        load: async function() {
            try {
                var container = document.getElementById('alert-content');
                if (!container) return;

                window.DashboardDOM.showLoading(container, __('alert.loading'));

                var alertsData = await window.DashboardApi.getAlerts(200);
                var summaryData = null;
                try {
                    summaryData = await window.DashboardApi.getAlertSummary();
                } catch (e) { /* ignore */ }

                this.render(alertsData, summaryData, container);
                this.updateRefreshTime();
            } catch (error) {
                console.error('[Alert] Load failed:', error);
                var container = document.getElementById('alert-content');
                if (container) {
                    container.innerHTML = '<div class="alert alert-danger">' + __('alert.loadFailed') + '</div>';
                }
            }
        },

        render: function(alertsData, summaryData, container) {
            window.DashboardDOM.clear(container);

            var entries = (alertsData && alertsData.entries) || [];

            var summaryHtml = '';
            if (summaryData) {
                var sev = summaryData.severityCounts || {};
                summaryHtml =
                    '<div class="row mb-3">' +
                        '<div class="col-md-3 col-6">' +
                            '<div class="stat-mini-card">' +
                                '<div class="stat-mini-value text-danger">' + (sev['Error'] || 0) + '</div>' +
                                '<div class="stat-mini-label">Error</div>' +
                            '</div>' +
                        '</div>' +
                        '<div class="col-md-3 col-6">' +
                            '<div class="stat-mini-card">' +
                                '<div class="stat-mini-value text-warning">' + (sev['Warning'] || 0) + '</div>' +
                                '<div class="stat-mini-label">Warning</div>' +
                            '</div>' +
                        '</div>' +
                        '<div class="col-md-3 col-6">' +
                            '<div class="stat-mini-card">' +
                                '<div class="stat-mini-value text-info">' + (sev['Info'] || 0) + '</div>' +
                                '<div class="stat-mini-label">Info</div>' +
                            '</div>' +
                        '</div>' +
                        '<div class="col-md-3 col-6">' +
                            '<div class="stat-mini-card">' +
                                '<div class="stat-mini-value text-secondary">' + (alertsData && alertsData.total || 0) + '</div>' +
                                '<div class="stat-mini-label">Total</div>' +
                            '</div>' +
                        '</div>' +
                    '</div>';
            }

            var filterHtml =
                '<div class="card-body py-2 border-bottom mb-3">' +
                    '<div class="row g-2 align-items-center">' +
                        '<div class="col-auto">' +
                            '<select class="form-select form-select-sm" id="alert-type-filter" style="width:150px;">' +
                                '<option value="">' + __('alert.type.all') + '</option>' +
                                '<option value="CircuitBreakerOpen">CircuitBreakerOpen</option>' +
                                '<option value="RetryExhausted">RetryExhausted</option>' +
                                '<option value="WafBlock">WafBlock</option>' +
                                '<option value="ProxyError">ProxyError</option>' +
                                '<option value="RateLimitExceeded">RateLimitExceeded</option>' +
                                '<option value="TestAlert">TestAlert</option>' +
                            '</select>' +
                        '</div>' +
                        '<div class="col-auto">' +
                            '<select class="form-select form-select-sm" id="alert-severity-filter" style="width:120px;">' +
                                '<option value="">' + __('alert.severity.all') + '</option>' +
                                '<option value="Error">Error</option>' +
                                '<option value="Warning">Warning</option>' +
                                '<option value="Info">Info</option>' +
                            '</select>' +
                        '</div>' +
                    '</div>' +
                '</div>';

            if (entries.length === 0) {
                container.innerHTML = summaryHtml + filterHtml +
                    '<div class="text-center py-5">' +
                        '<i class="bi bi-bell text-muted" style="font-size:48px;"></i>' +
                        '<p class="text-muted mt-3">' + __('alert.empty') + '</p>' +
                        '<p class="text-muted small">' + __('alert.emptyHelp') + '</p>' +
                    '</div>';
                this.bindFilterEvents();
                return;
            }

            var rows = entries.map(function(entry) {
                var sevClass = entry.severity === 'Error' ? 'bg-danger' :
                               entry.severity === 'Warning' ? 'bg-warning text-dark' : 'bg-info';
                var time = window.DashboardI18n.formatDate(entry.timestamp);

                var detailFields = [];
                if (entry.clusterId) detailFields.push('<span><strong>Cluster:</strong> <code>' + window.DashboardUtils.escapeHtml(entry.clusterId) + '</code></span>');
                if (entry.routeId) detailFields.push('<span><strong>Route:</strong> <code>' + window.DashboardUtils.escapeHtml(entry.routeId) + '</code></span>');
                if (entry.clientIp) detailFields.push('<span><strong>IP:</strong> <code>' + window.DashboardUtils.escapeHtml(entry.clientIp) + '</code></span>');
                if (entry.blockReason) detailFields.push('<span><strong>Reason:</strong> ' + window.DashboardUtils.escapeHtml(entry.blockReason) + '</span>');
                if (entry.errorMessage) detailFields.push('<span><strong>Error:</strong> ' + window.DashboardUtils.escapeHtml(entry.errorMessage) + '</span>');
                if (entry.attemptCount) detailFields.push('<span><strong>Attempts:</strong> ' + entry.attemptCount + '</span>');

                return '<div class="audit-entry" onclick="AlertModule.toggleDetail(this)" data-alert-type="' + window.DashboardUtils.escapeHtml(entry.alertType) + '" data-alert-severity="' + window.DashboardUtils.escapeHtml(entry.severity) + '">' +
                    '<div class="audit-row">' +
                        '<span class="audit-time">' + time + '</span>' +
                        '<span class="badge ' + sevClass + '">' + window.DashboardUtils.escapeHtml(entry.severity) + '</span>' +
                        '<span class="badge bg-secondary">' + window.DashboardUtils.escapeHtml(entry.alertType) + '</span>' +
                        '<span class="audit-target" style="flex:1;">' + window.DashboardUtils.escapeHtml(entry.message) + '</span>' +
                        '<i class="bi bi-chevron-right audit-arrow"></i>' +
                    '</div>' +
                    '<div class="audit-detail">' +
                        '<div class="row g-2">' +
                            '<div class="col-12">' + detailFields.join('&nbsp;') + '</div>' +
                        '</div>' +
                    '</div>' +
                '</div>';
            }).join('');

            container.innerHTML = summaryHtml + filterHtml +
                '<div class="audit-list">' + rows + '</div>';

            this.bindFilterEvents();
        },

        bindFilterEvents: function() {
            var self = this;
            var typeFilter = document.getElementById('alert-type-filter');
            var sevFilter = document.getElementById('alert-severity-filter');

            if (typeFilter) {
                typeFilter.value = this.currentFilter.type;
                typeFilter.onchange = function(e) {
                    self.currentFilter.type = e.target.value;
                    self.applyFilters();
                };
            }
            if (sevFilter) {
                sevFilter.value = this.currentFilter.severity;
                sevFilter.onchange = function(e) {
                    self.currentFilter.severity = e.target.value;
                    self.applyFilters();
                };
            }
        },

        applyFilters: function() {
            var entries = document.querySelectorAll('#alert-content .audit-entry[data-alert-type]');
            var typeFilter = this.currentFilter.type;
            var sevFilter = this.currentFilter.severity;

            entries.forEach(function(el) {
                var type = el.getAttribute('data-alert-type') || '';
                var sev = el.getAttribute('data-alert-severity') || '';
                var show = true;
                if (typeFilter && type !== typeFilter) show = false;
                if (sevFilter && sev !== sevFilter) show = false;
                el.style.display = show ? '' : 'none';
            });
        },

        toggleDetail: function(el) {
            var detail = el.querySelector('.audit-detail');
            var arrow = el.querySelector('.audit-arrow');
            if (detail) detail.classList.toggle('expanded');
            if (arrow) arrow.classList.toggle('expanded');
        },

        clearAll: async function() {
            if (!confirm(__('alert.clearConfirm'))) return;
            try {
                await window.DashboardApi.clearAlerts();
                if (window.DashboardModals) {
                    window.DashboardModals.showToast(__('alert.clearSuccess'), 'success');
                }
                await this.load();
            } catch (error) {
                console.error('[Alert] Clear failed:', error);
                if (window.DashboardModals) {
                    window.DashboardModals.showError(__('alert.clearFailed'));
                }
            }
        },

        sendTest: async function() {
            try {
                await window.DashboardApi.testAlert({
                    alertType: 'TestAlert',
                    title: 'Test Alert',
                    message: 'This is a test alert from the Dashboard.',
                    severity: 'Info'
                });
                if (window.DashboardModals) {
                    window.DashboardModals.showToast(__('alert.testSuccess'), 'success');
                }
            } catch (error) {
                console.error('[Alert] Test failed:', error);
                if (window.DashboardModals) {
                    window.DashboardModals.showError(__('alert.testFailed'));
                }
            }
        },

        updateRefreshTime: function() {
            var el = document.getElementById('alert-refresh-time');
            if (el) {
                el.textContent = window.DashboardI18n.formatDate(new Date());
            }
        }
    };

    if (window.DashboardApp) {
        window.DashboardApp.registerModule('alert', AlertModule);
    }
    window.AlertModule = AlertModule;
})();
