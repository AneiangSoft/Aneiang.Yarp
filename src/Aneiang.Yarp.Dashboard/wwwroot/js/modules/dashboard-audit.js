/**
 * Dashboard Audit Module - Configuration change audit log viewer
 */
(function() {
    'use strict';

    var AuditModule = {
        name: 'audit',
        initialized: false,

        init: async function() {
            if (this.initialized) return;
            this.setupEvents();
            this.initialized = true;
        },

        loadAuditLogs: async function() {
            try {
                var container = window.DashboardDOM.safe('#audit-content');
                if (!container) return;

                window.DashboardDOM.showLoading(container, __('audit.loading'));

                var actionFilter = window.DashboardState.get('filters.audit.action') || '';
                var data = await window.DashboardApi.get('/audit-logs', { count: 100, action: actionFilter });

                this.renderAuditLogs(data, container);
            } catch (error) {
                console.error('[Audit] Load failed:', error);
                var container = window.DashboardDOM.safe('#audit-content');
                if (container) window.DashboardDOM.showError(container, __('audit.loadFailed'));
            }
        },

        renderAuditLogs: function(data, container) {
            window.DashboardDOM.clear(container);

            if (!data.entries || data.entries.length === 0) {
                container.innerHTML =
                    '<div class="text-center py-5">' +
                        '<i class="bi bi-shield-check text-muted" style="font-size:48px;"></i>' +
                        '<p class="text-muted mt-3">' + __('audit.empty') + '</p>' +
                    '</div>';
                return;
            }

            var self = this;
            var rows = data.entries.map(function(entry) {
                var statusBadge = entry.success
                    ? '<span class="badge bg-success" style="font-size:10px">' + __('audit.success') + '</span>'
                    : '<span class="badge bg-danger" style="font-size:10px">' + __('audit.failed') + '</span>';

                var actionColors = {
                    'AddRoute': 'bg-primary',
                    'UpdateRoute': 'bg-info',
                    'RemoveRoute': 'bg-warning text-dark',
                    'AddCluster': 'bg-primary',
                    'UpdateCluster': 'bg-info',
                    'RemoveCluster': 'bg-warning text-dark',
                    'RenameCluster': 'bg-info',
                    'Rollback': 'bg-secondary',
                    'ReplaceAll': 'bg-secondary'
                };
                var actionClass = actionColors[entry.action] || 'bg-secondary';

                var time = window.DashboardI18n.formatDate(entry.timestamp);

                var detailHtml = '';
                if (entry.before) {
                    detailHtml += '<div class="col-6"><strong>' + __('audit.before') + '</strong><pre class="audit-json">' + window.DashboardUtils.escapeHtml(self.formatJson(entry.before)) + '</pre></div>';
                }
                if (entry.after) {
                    detailHtml += '<div class="col-6"><strong>' + __('audit.after') + '</strong><pre class="audit-json">' + window.DashboardUtils.escapeHtml(self.formatJson(entry.after)) + '</pre></div>';
                }
                if (entry.errorMessage) {
                    detailHtml += '<div class="col-12"><strong class="text-danger">' + __('audit.error') + '</strong><pre class="audit-json" style="color:#dc2626">' + window.DashboardUtils.escapeHtml(entry.errorMessage) + '</pre></div>';
                }

                var ipHtml = entry.clientIp
                    ? '<i class="bi bi-geo-alt"></i> ' + window.DashboardUtils.escapeHtml(entry.clientIp)
                    : '';

                return '<div class="audit-entry" onclick="window.AuditModule.toggleDetail(this)">' +
                    '<div class="audit-row">' +
                        '<span class="audit-time">' + time + '</span>' +
                        '<span class="badge ' + actionClass + ' audit-action">' + window.DashboardUtils.escapeHtml(entry.action) + '</span>' +
                        '<span class="audit-target"><code>' + window.DashboardUtils.escapeHtml(entry.target) + '</code></span>' +
                        statusBadge +
                        '<span class="audit-operator">' + window.DashboardUtils.escapeHtml(entry.operator || '-') + '</span>' +
                        '<span class="audit-ip">' + ipHtml + '</span>' +
                        '<i class="bi bi-chevron-right audit-arrow"></i>' +
                    '</div>' +
                    '<div class="audit-detail">' +
                        '<div class="row g-2">' + detailHtml + '</div>' +
                    '</div>' +
                '</div>';
            }).join('');

            container.innerHTML =
                '<div class="d-flex justify-content-between align-items-center mb-3">' +
                    '<div>' +
                        '<h6 class="mb-0"><i class="bi bi-shield-check me-1"></i>' + __('audit.title') + '</h6>' +
                        '<small class="text-muted">' + __('audit.total', { total: data.total, evicted: data.evicted }) + '</small>' +
                    '</div>' +
                '</div>' +
                '<div id="audit-filter-container"></div>' +
                '<div class="audit-list">' + rows + '</div>';

            this.renderToolbar(data);
        },

        renderToolbar: function(data) {
            var container = window.DashboardDOM.safe('#audit-filter-container');
            if (!container) return;

            container.innerHTML =
                '<div class="card-body py-2 border-bottom">' +
                    '<div class="row g-2 align-items-center">' +
                        '<div class="col-auto">' +
                            '<select class="form-select form-select-sm" id="audit-action-filter" style="width:160px;">' +
                                '<option value="">' + __('audit.filterAll') + '</option>' +
                                '<option value="AddRoute">AddRoute</option>' +
                                '<option value="UpdateRoute">UpdateRoute</option>' +
                                '<option value="RemoveRoute">RemoveRoute</option>' +
                                '<option value="AddCluster">AddCluster</option>' +
                                '<option value="UpdateCluster">UpdateCluster</option>' +
                                '<option value="RemoveCluster">RemoveCluster</option>' +
                                '<option value="RenameCluster">RenameCluster</option>' +
                                '<option value="Rollback">Rollback</option>' +
                                '<option value="ReplaceAll">ReplaceAll</option>' +
                            '</select>' +
                        '</div>' +
                        '<div class="col-auto">' +
                            '<button class="btn btn-sm btn-outline-secondary" onclick="window.AuditModule.loadAuditLogs()">' +
                                '<i class="bi bi-arrow-clockwise"></i>' +
                            '</button>' +
                        '</div>' +
                    '</div>' +
                '</div>';

            var filterSelect = window.DashboardDOM.safe('#audit-action-filter');
            if (filterSelect) {
                var currentFilter = window.DashboardState.get('filters.audit.action') || '';
                filterSelect.value = currentFilter;
                filterSelect.onchange = function(e) {
                    window.DashboardState.set('filters.audit.action', e.target.value);
                    window.AuditModule.loadAuditLogs();
                };
            }
        },

        toggleDetail: function(el) {
            var detail = el.querySelector('.audit-detail');
            var arrow = el.querySelector('.audit-arrow');
            if (detail) {
                detail.classList.toggle('expanded');
                if (arrow) arrow.classList.toggle('expanded');
            }
        },

        formatJson: function(str) {
            try {
                return JSON.stringify(JSON.parse(str), null, 2);
            } catch (e) {
                return str;
            }
        },

        setupEvents: function() {
            document.addEventListener('dashboard:shortcut:refresh', function() { AuditModule.loadAuditLogs(); });
            document.addEventListener('dashboard:localeChange', function() { AuditModule.loadAuditLogs(); });
        }
    };

    if (window.DashboardApp) {
        window.DashboardApp.registerModule('audit', AuditModule);
    }
    window.AuditModule = AuditModule;
})();
