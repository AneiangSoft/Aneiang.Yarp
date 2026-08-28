/**
 * Dashboard Audit Module - Configuration change audit log viewer
 */
(function() {
    'use strict';

    /* Action semantic colors: add=green, update=blue, remove=red, rollback=amber */
    var ACTION_META = {
        'AddRoute':        { cls: 'add',  icon: 'bi-plus-circle' },
        'AddCluster':      { cls: 'add',  icon: 'bi-plus-circle' },
        'UpdateRoute':     { cls: 'upd',  icon: 'bi-pencil-circle' },
        'UpdateCluster':   { cls: 'upd',  icon: 'bi-pencil-circle' },
        'RenameCluster':   { cls: 'upd',  icon: 'bi-pencil-circle' },
        'SetRouteEnabled': { cls: 'upd',  icon: 'bi-toggle-on' },
        'RemoveRoute':     { cls: 'rmv',  icon: 'bi-dash-circle' },
        'RemoveCluster':   { cls: 'rmv',  icon: 'bi-dash-circle' },
        'Rollback':        { cls: 'rb',   icon: 'bi-clock-history' },
        'RollbackConfig':  { cls: 'rb',   icon: 'bi-clock-history' },
        'ReplaceAll':      { cls: 'rb',   icon: 'bi-clock-history' }
    };

    var AuditModule = {
        name: 'audit',
        initialized: false,

        init: async function() {
            if (this.initialized) return;
            this.setupEvents();
            this.initialized = true;
            await this.loadAuditLogs();
        },

        loadAuditLogs: async function() {
            try {
                var container = window.DashboardDOM.safe('#audit-content');
                if (!container) return;

                window.DashboardDOM.showLoading(container, __('audit.loading'));

                var actionFilter = window.DashboardState.get('filters.audit.action') || '';
                var data = await window.DashboardApi.get('/api/audit-logs', { page: 1, pageSize: 100, action: actionFilter });

                this.renderMeta(data);
                this.renderAuditLogs(data, container);
                this.renderFilterBar();
                this.markRefreshTime();
            } catch (error) {
                console.error('[Audit] Load failed:', error);
                var container = window.DashboardDOM.safe('#audit-content');
                if (container) window.DashboardDOM.showError(container, __('audit.loadFailed'));
            }
        },

        markRefreshTime: function() {
            var el = window.DashboardDOM.safe('#audit-refresh-time');
            if (el && window.DashboardUtils && window.DashboardUtils.timeAgo) {
                el.textContent = window.DashboardUtils.timeAgo(Date.now());
            }
        },

        /* Flat meta pill line: total · evicted(only when >0) · filter status */
        renderMeta: function(data) {
            var el = window.DashboardDOM.safe('#audit-meta-line');
            if (!el) return;
            var total = data.total || 0;
            var evicted = data.evicted || 0;
            var entries = (data.entries || []).length;
            var failed = 0;
            (data.entries || []).forEach(function(e) { if (!e.success) failed++; });

            var html = '';
            html += '<span class="pc-meta-item"><i class="bi bi-journal-text"></i><span class="pc-meta-num">' + total + '</span><span class="pc-meta-label">' + __('audit.totalLabel', '日志') + '</span></span>';
            html += '<span class="pc-meta-item"><i class="bi bi-collection"></i><span class="pc-meta-num">' + entries + '</span><span class="pc-meta-label">' + __('audit.showingLabel', '展示') + '</span></span>';
            if (failed > 0) {
                html += '<span class="pc-meta-item pc-meta-item--danger"><i class="bi bi-x-circle"></i><span class="pc-meta-num">' + failed + '</span><span class="pc-meta-label">' + __('audit.failed') + '</span></span>';
            }
            if (evicted > 0) {
                html += '<span class="pc-meta-item pc-meta-item--warn"><i class="bi bi-archive"></i><span class="pc-meta-num">' + evicted + '</span><span class="pc-meta-label">' + __('audit.evictedLabel', '已淘汰') + '</span></span>';
            }

            var actionFilter = window.DashboardState.get('filters.audit.action') || '';
            html += '<div class="pc-meta-actions">';
            html += '<select class="form-select form-select-sm audit-filter-select" id="audit-action-filter" style="width:170px;">';
            html += '<option value="">' + __('audit.filterAll') + '</option>';
            ['AddRoute','UpdateRoute','RemoveRoute','AddCluster','UpdateCluster','RemoveCluster','RenameCluster','SetRouteEnabled','Rollback','RollbackConfig','ReplaceAll'].forEach(function(a) {
                html += '<option value="' + a + '"' + (actionFilter === a ? ' selected' : '') + '>' + a + '</option>';
            });
            html += '</select>';
            html += '</div>';

            el.innerHTML = html;

            var filterSelect = window.DashboardDOM.safe('#audit-action-filter');
            if (filterSelect) {
                filterSelect.onchange = function(e) {
                    window.DashboardState.set('filters.audit.action', e.target.value);
                    window.AuditModule.loadAuditLogs();
                };
            }
        },

        /* Filter bar rendered into audit-content head (kept for compatibility) */
        renderFilterBar: function() {
            var container = window.DashboardDOM.safe('#audit-filter-container');
            if (container) container.innerHTML = '';
        },

        renderAuditLogs: function(data, container) {
            window.DashboardDOM.clear(container);

            if (!data.entries || data.entries.length === 0) {
                container.innerHTML =
                    '<div class="pr-empty">' +
                        '<i class="bi bi-shield-check"></i>' +
                        '<p class="mb-0">' + __('audit.empty') + '</p>' +
                    '</div>';
                return;
            }

            var self = this;
            var rows = data.entries.map(function(entry, idx) {
                var meta = ACTION_META[entry.action] || { cls: 'rb', icon: 'bi-gear' };
                var time = window.DashboardI18n.formatDate(entry.timestamp);
                var target = window.DashboardUtils.escapeHtml(entry.target || '-');
                var hasDetail = entry.before || entry.after || entry.errorMessage;

                return '<tr class="audit-entry' + (entry.success ? '' : ' audit-row--failed') + '" data-idx="' + idx + '">' +
                    '<td class="audit-time">' + time + '</td>' +
                    '<td><span class="audit-action audit-action--' + meta.cls + '"><i class="bi ' + meta.icon + '"></i>' + window.DashboardUtils.escapeHtml(entry.action) + '</span></td>' +
                    '<td><code class="audit-target-code">' + target + '</code></td>' +
                    '<td><span class="plg-health" style="color:' + (entry.success ? '#10b981' : '#f87171') + ';"><span class="plg-dot" style="background:' + (entry.success ? '#10b981' : '#f87171') + ';"></span>' + (entry.success ? __('audit.success') : __('audit.failed')) + '</span></td>' +
                    '<td class="audit-operator">' + window.DashboardUtils.escapeHtml(entry.operator || '-') + '</td>' +
                    '<td class="audit-ip">' + (entry.clientIp ? window.DashboardUtils.escapeHtml(entry.clientIp) : '') + '</td>' +
                    '<td class="audit-toggle">' + (hasDetail ? '<i class="bi bi-chevron-right audit-arrow"></i>' : '') + '</td>' +
                '</tr>' +
                (hasDetail ? self.renderDetailRow(entry) : '');
            }).join('');

            container.innerHTML =
                '<div class="plg-table-wrap"><table class="plg-table audit-table">' +
                '<thead><tr>' +
                    '<th style="width:150px;">' + __('audit.time', '时间') + '</th>' +
                    '<th style="width:150px;">' + __('audit.actionLabel', '操作') + '</th>' +
                    '<th>' + __('audit.targetLabel', '目标') + '</th>' +
                    '<th style="width:100px;">' + __('audit.statusLabel', '状态') + '</th>' +
                    '<th style="width:100px;">' + __('audit.operatorLabel', '操作人') + '</th>' +
                    '<th style="width:130px;">' + __('audit.ipLabel', '来源 IP') + '</th>' +
                    '<th style="width:36px;"></th>' +
                '</tr></thead>' +
                '<tbody>' + rows + '</tbody>' +
                '</table></div>';

            // Delegate toggle clicks: whole row clickable (except on links/inputs)
            var tbody = container.querySelector('tbody');
            if (tbody) {
                tbody.addEventListener('click', function(e) {
                    if (e.target.closest('a, button, input, select')) return;
                    var row = e.target.closest('tr.audit-entry');
                    if (!row) return;
                    var detailRow = row.nextElementSibling;
                    if (detailRow && detailRow.classList.contains('audit-detail-row')) {
                        var expanded = detailRow.classList.toggle('expanded');
                        var arrow = row.querySelector('.audit-arrow');
                        if (arrow) arrow.classList.toggle('expanded', expanded);
                        row.classList.toggle('audit-row--open', expanded);
                    }
                });
            }
        },

        /* Expandable detail row: before/after side-by-side + error strip */
        renderDetailRow: function(entry) {
            var self = this;
            var parts = [];
            if (entry.before) {
                parts.push('<div class="audit-diff-col"><div class="audit-diff-head"><i class="bi bi-file-earmark-minus"></i>' + __('audit.before') + '</div><pre class="audit-json">' + window.DashboardUtils.escapeHtml(self.formatJson(entry.before)) + '</pre></div>');
            }
            if (entry.after) {
                parts.push('<div class="audit-diff-col"><div class="audit-diff-head audit-diff-head--after"><i class="bi bi-file-earmark-plus"></i>' + __('audit.after') + '</div><pre class="audit-json">' + window.DashboardUtils.escapeHtml(self.formatJson(entry.after)) + '</pre></div>');
            }
            var cols = parts.length > 1 ? 'two' : 'one';
            var errHtml = '';
            if (entry.errorMessage) {
                errHtml = '<div class="audit-error-strip"><i class="bi bi-exclamation-triangle"></i><pre class="audit-json audit-json--err">' + window.DashboardUtils.escapeHtml(entry.errorMessage) + '</pre></div>';
            }
            return '<tr class="audit-detail-row"><td colspan="7"><div class="audit-detail audit-detail--' + cols + '"><div class="audit-diff-grid">' + parts.join('') + '</div>' + errHtml + '</div></td></tr>';
        },

        toggleDetail: function(el) {
            // legacy: rows now toggle via delegated click on .audit-toggle
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
