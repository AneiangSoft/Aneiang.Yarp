/**
 * Notification Center Module - Unified notification and alert management
 */
(function() {
    'use strict';

    // Shared event type metadata — used for both the filter dropdown and rule modal checkboxes.
    // Add new event types here; all UI rendering and i18n labels flow from this single source.
    window.NOTIF_EVENT_TYPES = [
        // Runtime events
        { value: 'CircuitBreakerOpen', i18nKey: 'notif.event.CircuitBreakerOpen', group: 'runtime' },
        { value: 'RetryExhausted',     i18nKey: 'notif.event.RetryExhausted',     group: 'runtime' },
        { value: 'WafBlock',           i18nKey: 'notif.event.WafBlock',           group: 'runtime' },
        { value: 'ProxyError',         i18nKey: 'notif.event.ProxyError',         group: 'runtime' },
        { value: 'RateLimitExceeded',  i18nKey: 'notif.event.RateLimitExceeded',  group: 'runtime' },
        // Config change events (DISPATCHED by ConfigChangeEventDispatcher)
        { value: 'AddRoute',           i18nKey: 'notif.event.AddRoute',           group: 'config' },
        { value: 'UpdateRoute',        i18nKey: 'notif.event.UpdateRoute',        group: 'config' },
        { value: 'RemoveRoute',        i18nKey: 'notif.event.RemoveRoute',        group: 'config' },
        { value: 'AddCluster',         i18nKey: 'notif.event.AddCluster',         group: 'config' },
        { value: 'UpdateCluster',      i18nKey: 'notif.event.UpdateCluster',      group: 'config' },
        { value: 'RemoveCluster',      i18nKey: 'notif.event.RemoveCluster',      group: 'config' },
        { value: 'RollbackConfig',     i18nKey: 'notif.event.RollbackConfig',     group: 'config' },
        { value: 'TestNotification',   i18nKey: 'notif.event.TestNotification',   group: 'misc' },
    ];

    /**
     * Look up an i18n key from window.I18N, falling back to the raw value.
     * Mirrors the pattern used throughout the dashboard JS codebase.
     */
    window._notifI18n = function(key, fallback) {
        return (window.I18N && window.I18N[key]) || fallback;
    };

    var NotificationModule = {
        name: 'notification',
        initialized: false,
        autoRefreshInterval: null,
        currentPage: 1,
        pageSize: 50,
        currentFilters: { eventType: '', keyword: '', dateStart: '', dateEnd: '' },

        // Data cache
        _data: {
            settings: null,
            channels: [],
            rules: [],
            globalSettings: null,
            history: [],
            summary: null
        },

        init: function() {
            if (this.initialized) return;
            this._renderNotifTypeFilter();
            this._renderRuleEventTypes();
            this.setupEvents();
            this.setupTabs();
            this.loadAll();
            this.initialized = true;
        },

        // ─── Event Type Rendering ───────────────────────────────────────────

        /**
         * Render the notification-type filter dropdown using the shared event metadata.
         * Preserves the first "all types" option from the Razor template.
         */
        _renderNotifTypeFilter: function() {
            var select = document.getElementById('notif-type-filter');
            if (!select) return;

            // Keep the first "all types" option; clear the rest
            var allOpt = select.options[0];
            select.innerHTML = '';
            select.appendChild(allOpt);

            var self = this;
            window.NOTIF_EVENT_TYPES.forEach(function(evt) {
                var opt = document.createElement('option');
                opt.value = evt.value;
                opt.textContent = window._notifI18n(evt.i18nKey, evt.value);
                select.appendChild(opt);
            });

            // Re-attach the change listener
            select.addEventListener('change', function() {
                self.applyFilters();
            });
        },

        /**
         * Render the rule modal event-type checkboxes using the shared event metadata.
         * Includes a "Select All" toggle and group headers (runtime / config).
         */
        _renderRuleEventTypes: function() {
            var container = document.getElementById('rule-event-types');
            if (!container) return;
            container.innerHTML = '';

            // "Select All" toggle
            var selectAllLabel = document.createElement('label');
            selectAllLabel.className = 'event-type-item event-type-select-all';
            selectAllLabel.innerHTML = '<input type="checkbox" id="event-select-all" onchange="NotificationModule._toggleAllEvents(this)"> '
                + '<strong>' + __('notif.selectAll') + '</strong>';
            container.appendChild(selectAllLabel);
            container.appendChild(document.createElement('hr'));

            var groups = {};
            window.NOTIF_EVENT_TYPES.forEach(function(evt) {
                var g = evt.group || 'other';
                if (!groups[g]) groups[g] = [];
                groups[g].push(evt);
            });

            var groupLabels = {
                runtime: __('notif.groupRuntime') || 'Runtime Events',
                config: __('notif.groupConfig') || 'Config Change Events',
                misc: __('notif.groupMisc') || 'Other'
            };

            ['runtime', 'config', 'misc'].forEach(function(groupKey) {
                var list = groups[groupKey];
                if (!list || list.length === 0) return;
                var groupHeader = document.createElement('div');
                groupHeader.className = 'event-type-group-label';
                groupHeader.textContent = groupLabels[groupKey] || groupKey;
                container.appendChild(groupHeader);

                list.forEach(function(evt) {
                    var label = document.createElement('label');
                    label.className = 'event-type-item';
                    label.innerHTML = '<input type="checkbox" value="' + evt.value + '" class="event-type-cb"> '
                        + window._notifI18n(evt.i18nKey, evt.value);
                    container.appendChild(label);
                });
            });
        },

        /**
         * Toggle all event-type checkboxes when the "Select All" master checkbox changes.
         */
        _toggleAllEvents: function(master) {
            document.querySelectorAll('#rule-event-types input.event-type-cb').forEach(function(cb) {
                cb.checked = master.checked;
            });
        },

        /**
         * Apply a quick preset to the rule event type checkboxes.
         * @param {'all'|'config'|'runtime'|'test'} preset
         */
        _applyPreset: function(preset) {
            var allCbs = document.querySelectorAll('#rule-event-types input.event-type-cb');
            var nameInput = document.getElementById('rule-name');

            // Uncheck all first
            allCbs.forEach(function(cb) { cb.checked = false; });

            var targets = [];
            switch (preset) {
                case 'all':
                    targets = [];
                    allCbs.forEach(function(cb) { cb.checked = true; });
                    if (!nameInput.value) nameInput.value = __('notif.preset.allName') || '全部事件通知';
                    break;
                case 'config':
                    targets = ['AddRoute','UpdateRoute','RemoveRoute','AddCluster','UpdateCluster','RemoveCluster','RollbackConfig'];
                    if (!nameInput.value) nameInput.value = __('notif.preset.configName') || '配置变更通知';
                    break;
                case 'runtime':
                    targets = ['CircuitBreakerOpen','RetryExhausted','WafBlock','ProxyError','RateLimitExceeded'];
                    if (!nameInput.value) nameInput.value = __('notif.preset.runtimeName') || '运行时告警通知';
                    break;
                case 'test':
                    targets = ['TestNotification'];
                    if (!nameInput.value) nameInput.value = __('notif.preset.testName') || '测试通知';
                    break;
            }

            if (targets.length > 0) {
                allCbs.forEach(function(cb) {
                    if (targets.indexOf(cb.value) >= 0) cb.checked = true;
                });
            }

            // Refresh master checkbox
            var master = document.getElementById('event-select-all');
            if (master) {
                master.checked = Array.from(allCbs).every(function(c) { return c.checked; });
            }
        },

        /**
         * Generate a test history entry directly via API for debugging purposes.
         */
        generateTestEntry: async function() {
            try {
                var resp = await DashboardApi.post('/api/notifications/test-entry');
                if (resp && resp.ok !== false) {
                    DashboardModals.showSuccess(__('notif.testEntry.created') || '测试记录已生成');
                    this.loadHistory();
                } else {
                    DashboardModals.showError((resp && resp.error) || __('notif.testEntry.failed') || '生成失败');
                }
            } catch (e) {
                console.error('[Notification] Generate test entry failed:', e);
                DashboardModals.showError(__('notif.testEntry.failed') || '生成失败');
            }
        },

        setupEvents: function() {
            var self = this;
            document.addEventListener('dashboard:ready', function() {
                // Auto refresh every 30 seconds
                if (self.autoRefreshInterval) clearInterval(self.autoRefreshInterval);
                self.autoRefreshInterval = setInterval(function() {
                    self.loadSummary();
                }, 30000);
            });
        },

        setupTabs: function() {
            var self = this;
            document.querySelectorAll('.tab-btn[data-tab]').forEach(function(btn) {
                btn.addEventListener('click', function() {
                    var tab = this.getAttribute('data-tab');
                    self.switchTab(tab);
                });
            });
        },

        switchTab: function(tab) {
            document.querySelectorAll('.tab-btn').forEach(function(b) {
                b.classList.toggle('active', b.getAttribute('data-tab') === tab);
                b.setAttribute('aria-selected', b.getAttribute('data-tab') === tab ? 'true' : 'false');
            });
            document.querySelectorAll('.tab-content').forEach(function(c) {
                c.classList.toggle('active', c.getAttribute('data-tab-content') === tab);
            });
        },

        // ─── Data Loading ─────────────────────────────────────────────────────

        loadAll: function() {
            this.loadSummary();
            this.loadHistory();
            this.loadSettings();
        },

        loadSummary: async function() {
            try {
                var resp = await DashboardApi.get('/api/notifications/summary');
                if (resp) {
                    this._data.summary = resp;
                    this.renderSummary(resp);
                }
            } catch (e) {
                console.error('[Notification] Load summary failed:', e);
            }
        },

        loadHistory: async function(page, silent) {
            if (page) this.currentPage = page;

            try {
                if (!silent) this.showHistoryLoading(true);

                var params = {
                    page: this.currentPage,
                    pageSize: this.pageSize,
                    eventType: this.currentFilters.eventType || undefined,
                    dateStart: this.currentFilters.dateStart || undefined,
                    dateEnd: this.currentFilters.dateEnd || undefined
                };
                var resp = await DashboardApi.get('/api/notifications/history', params);
                if (resp) {
                    // Client-side keyword search + date range fallback
                    var entries = resp.entries || [];
                    if (this.currentFilters.keyword) {
                        var kw = this.currentFilters.keyword.toLowerCase();
                        entries = entries.filter(function(e) {
                            return (e.title && e.title.toLowerCase().indexOf(kw) >= 0) ||
                                   (e.message && e.message.toLowerCase().indexOf(kw) >= 0) ||
                                   (e.eventType && e.eventType.toLowerCase().indexOf(kw) >= 0) ||
                                   (e.clusterId && e.clusterId.toLowerCase().indexOf(kw) >= 0) ||
                                   (e.routeId && e.routeId.toLowerCase().indexOf(kw) >= 0) ||
                                   (e.clientIp && e.clientIp.toLowerCase().indexOf(kw) >= 0);
                        });
                    }

                    this._data.history = entries;
                    this.renderHistory({ entries: entries, total: resp.total, page: resp.page, pageSize: resp.pageSize });
                }
            } catch (e) {
                console.error('[Notification] Load history failed:', e);
                if (!silent) {
                    var container = document.getElementById('notif-history-list');
                    if (container) container.innerHTML = '<div class="empty-state"><i class="bi bi-exclamation-triangle-fill" style="color:#ef4444;"></i><p>' + (__('notif.loadFailed') || '加载失败') + '</p></div>';
                }
            }
        },

        showHistoryLoading: function(show) {
            var container = document.getElementById('notif-history-list');
            if (container && show) {
                container.innerHTML = '<div class="loading-state"><div class="loading-spinner"></div><div class="loading-text">' + (__('notif.loading') || '加载中...') + '</div></div>';
            }
        },

        loadSettings: async function() {
            try {
                var resp = await DashboardApi.get('/api/notifications/settings');
                if (resp) {
                    this._data.settings = resp;
                    this._data.channels = resp.channels || [];
                    this._data.rules = resp.rules || [];
                    this._data.globalSettings = resp.globalSettings || {};
                    this.renderChannels(this._data.channels);
                    this.renderRules(this._data.rules);
                    this.renderSettings(this._data.globalSettings);
                }
            } catch (e) {
                console.error('[Notification] Load settings failed:', e);
            }
        },

        // ─── Rendering ────────────────────────────────────────────────────────

        renderSummary: function(data) {
            var totalCount = document.getElementById('notif-total-count');
            var channelCount = document.getElementById('notif-channel-count');
            var rulesCount = document.getElementById('notif-rules-count');
            var lastEventEl = document.getElementById('notif-last-event');

            if (totalCount) totalCount.textContent = data.total || 0;
            if (channelCount) channelCount.textContent = data.channelsConfigured || 0;
            if (rulesCount) rulesCount.textContent = data.rulesActive || 0;
            if (lastEventEl) {
                if (data.lastEvent && data.lastEvent.timestamp) {
                    var t = typeof DashboardI18n !== 'undefined' && DashboardI18n.formatDate
                        ? DashboardI18n.formatDate(data.lastEvent.timestamp)
                        : new Date(data.lastEvent.timestamp).toLocaleString();
                    lastEventEl.textContent = t;
                } else {
                    lastEventEl.textContent = '-';
                }
            }
        },

        renderHistory: function(data) {
            var container = document.getElementById('notif-history-list');
            if (!container) return;

            var total = data.total || 0;
            var page = data.page || 1;
            var pageSize = data.pageSize || this.pageSize;
            var entries = data.entries || [];

            // Show result info
            var infoEl = document.getElementById('notif-result-info');
            if (infoEl) {
                if (entries.length > 0) {
                    var from = (page - 1) * pageSize + 1;
                    var to = Math.min(page * pageSize, total);
                    infoEl.textContent = (__('notif.showing') || '显示') + ' ' + from + '-' + to + ' / ' + (__('notif.of') || '共') + ' ' + total + ' ' + (__('notif.records') || '条');
                } else {
                    infoEl.textContent = '';
                }
            }

            if (entries.length === 0) {
                var hasFilter = this.currentFilters.eventType || this.currentFilters.keyword || this.currentFilters.dateStart || this.currentFilters.dateEnd;
                var emptyMsg = hasFilter
                    ? '<p>' + (__('notif.emptyFilter') || '没有符合筛选条件的记录') + '</p><small style="color:#94a3b8;">' + (__('notif.emptyFilterHint') || '请尝试调整筛选条件或清除筛选') + '</small>'
                    : '<p>' + (__('notif.emptyHistory') || '暂无通知记录') + '</p><small style="color:#94a3b8;">' + (__('notif.emptyHistoryHint') || '通知事件将在中间件触发后自动记录，或点击"生成测试"创建测试数据') + '</small>';
                container.innerHTML = '<div class="empty-state"><i class="bi bi-bell"></i>' + emptyMsg + '</div>';
                this.renderPagination(total, page, pageSize);
                return;
            }

            var html = '';
            var self = this;
            entries.forEach(function(entry) {
                var time = typeof DashboardI18n !== 'undefined' && DashboardI18n.formatDate
                    ? DashboardI18n.formatDate(entry.timestamp)
                    : (entry.timestamp ? new Date(entry.timestamp).toLocaleString() : '');
                var details = self.buildHistoryDetails(entry);
                var evtMeta = window.NOTIF_EVENT_TYPES.find(function(e) { return e.value === entry.eventType; });
                var evtLabel = evtMeta ? window._notifI18n(evtMeta.i18nKey, evtMeta.value) : entry.eventType;

                // Channel chips
                var channelChips = '';
                if (entry.notifiedChannels && entry.notifiedChannels.length > 0) {
                    channelChips = '<span style="font-size:11px;color:#94a3b8;margin-left:8px;">' +
                        entry.notifiedChannels.map(function(c) { return '<span style="background:#e0e7ff;padding:1px 6px;border-radius:4px;margin-left:4px;color:#4338ca;">' + DashboardUtils.escapeHtml(c) + '</span>'; }).join('') +
                        '</span>';
                }

                var deliveryIcon = '';
                if (entry.notifiedChannels && entry.notifiedChannels.length > 0) {
                    deliveryIcon = entry.deliverySuccess
                        ? '<i class="bi bi-check-circle-fill" style="color:#16a34a;font-size:12px;margin-left:4px;" title="推送成功"></i>'
                        : '<i class="bi bi-x-circle-fill" style="color:#dc2626;font-size:12px;margin-left:4px;" title="推送失败"></i>';
                }

                html += '<div class="notification-item" onclick="NotificationModule.toggleNotification(this)" data-id="' + entry.id + '">' +
                    '<span class="notif-time">' + time + '</span>' +
                    '<span class="notif-badge" style="background:#f1f5f9;color:#475569;">' + evtLabel + '</span>' +
                    '<span class="notif-message">' + DashboardUtils.escapeHtml(entry.title) + '</span>' +
                    channelChips + deliveryIcon +
                    '<i class="bi bi-chevron-right notif-arrow"></i>' +
                    '<div class="notif-details">' + details + '</div>' +
                    '</div>';
            });

            container.innerHTML = html;
            this.renderPagination(total, page, pageSize);
        },

        buildHistoryDetails: function(entry) {
            var html = '<div class="notif-detail-row">';
            if (entry.clusterId) html += '<span><strong>' + (__('notif.detail.cluster') || 'Cluster') + ':</strong> <code>' + DashboardUtils.escapeHtml(entry.clusterId) + '</code></span>';
            if (entry.routeId) html += '<span><strong>' + (__('notif.detail.route') || 'Route') + ':</strong> <code>' + DashboardUtils.escapeHtml(entry.routeId) + '</code></span>';
            if (entry.clientIp) html += '<span><strong>' + (__('notif.detail.ip') || 'IP') + ':</strong> <code>' + DashboardUtils.escapeHtml(entry.clientIp) + '</code></span>';
            html += '</div>';
            if (entry.message) html += '<div style="margin-top:8px;color:#475569;">' + DashboardUtils.escapeHtml(entry.message) + '</div>';
            if (entry.blockReason) html += '<div style="margin-top:4px;color:#dc2626;"><strong>' + (__('notif.detail.reason') || 'Reason') + ':</strong> ' + DashboardUtils.escapeHtml(entry.blockReason) + '</div>';
            if (entry.requestUri) html += '<div style="margin-top:4px;"><strong>' + (__('notif.detail.uri') || 'URI') + ':</strong> <code>' + DashboardUtils.escapeHtml(entry.requestUri) + '</code></div>';
            if (entry.errorMessage) html += '<div style="margin-top:4px;color:#dc2626;"><strong>' + (__('notif.detail.error') || 'Error') + ':</strong> ' + DashboardUtils.escapeHtml(entry.errorMessage) + '</div>';
            if (entry.attemptCount != null) html += '<div style="margin-top:4px;"><strong>' + (__('notif.detail.attempts') || 'Attempts') + ':</strong> ' + entry.attemptCount + '</div>';
            if (entry.lastStatusCode != null) html += '<div style="margin-top:4px;"><strong>' + (__('notif.detail.statusCode') || 'Status') + ':</strong> ' + entry.lastStatusCode + '</div>';
            // Show notification ID for debugging
            if (entry.id) html += '<div style="margin-top:8px;font-size:10px;color:#94a3b8;"><strong>ID:</strong> <code style="font-size:10px;">' + entry.id + '</code></div>';
            return html;
        },

        renderPagination: function(total, page, pageSize) {
            var container = document.getElementById('notif-history-pagination');
            if (!container) return;

            var totalPages = Math.max(1, Math.ceil(total / pageSize));

            // Always show pagination info, even for single page
            if (total <= pageSize && totalPages <= 1) {
                container.innerHTML = total > 0
                    ? '<div style="text-align:center;padding:12px;color:#94a3b8;font-size:13px;">' + (__('notif.allRecords') || '共 {0} 条记录').replace('{0}', total) + '</div>'
                    : '';
                return;
            }

            var html = '<div style="display:flex;align-items:center;justify-content:center;gap:4px;flex-wrap:wrap;">';

            // Previous
            html += '<button class="pagination-btn" onclick="NotificationModule.loadHistory(' + (page - 1) + ')" ' + (page <= 1 ? 'disabled' : '') + ' title="' + (__('notif.prevPage') || '上一页') + '">' +
                '<i class="bi bi-chevron-left"></i></button>';

            var start = Math.max(1, page - 2);
            var end = Math.min(totalPages, page + 2);

            if (start > 1) {
                html += '<button class="pagination-btn" onclick="NotificationModule.loadHistory(1)">1</button>';
                if (start > 2) html += '<span style="padding:0 6px;color:#94a3b8;font-size:13px;">...</span>';
            }

            for (var i = start; i <= end; i++) {
                html += '<button class="pagination-btn ' + (i === page ? 'active' : '') + '" onclick="NotificationModule.loadHistory(' + i + ')">' + i + '</button>';
            }

            if (end < totalPages) {
                if (end < totalPages - 1) html += '<span style="padding:0 6px;color:#94a3b8;font-size:13px;">...</span>';
                html += '<button class="pagination-btn" onclick="NotificationModule.loadHistory(' + totalPages + ')">' + totalPages + '</button>';
            }

            // Next
            html += '<button class="pagination-btn" onclick="NotificationModule.loadHistory(' + (page + 1) + ')" ' + (page >= totalPages ? 'disabled' : '') + ' title="' + (__('notif.nextPage') || '下一页') + '">' +
                '<i class="bi bi-chevron-right"></i></button>';

            // Page jump
            html += '<span style="margin-left:12px;font-size:12px;color:#94a3b8;">' +
                '<span>' + (__('notif.page') || '第') + ' </span>' +
                '<input type="number" class="pagination-jump" id="notif-page-jump" value="' + page + '" min="1" max="' + totalPages + '" style="width:50px;text-align:center;border:1px solid #e2e8f0;border-radius:6px;padding:4px 6px;font-size:13px;" onkeydown="NotificationModule._onPageJump(event)">' +
                '<span> / ' + totalPages + ' ' + (__('notif.pageUnit') || '页') + '</span>' +
                '</span>';

            html += '</div>';
            container.innerHTML = html;
        },

        _onPageJump: function(e) {
            if (e.key !== 'Enter') return;
            var page = parseInt(e.target.value);
            if (page > 0) this.loadHistory(page);
        },

        renderChannels: function(channels) {
            var container = document.getElementById('notif-channels-list');
            if (!container) return;

            if (!channels || channels.length === 0) {
                container.innerHTML = '<div class="empty-state">' +
                    '<i class="bi bi-megaphone"></i>' +
                    '<p>' + __('notif.noChannels') + '</p>' +
                    '</div>';
                return;
            }

            var html = '';
            channels.forEach(function(ch) {
                var iconClass = ch.type === 'DingTalk' ? 'dingtalk' : 'generic';
                var icon = ch.type === 'DingTalk' ? 'bi-chat-dots-fill' : 'bi-link-45deg';
                var disabledClass = ch.enabled ? '' : 'disabled';

                html += '<div class="channel-card ' + disabledClass + '" data-id="' + ch.id + '">' +
                    '<div class="channel-header">' +
                    '<div class="channel-icon ' + iconClass + '"><i class="bi ' + icon + '"></i></div>' +
                    '<div class="channel-info">' +
                    '<div class="channel-name">' + DashboardUtils.escapeHtml(ch.name) + '</div>' +
                    '<div class="channel-type">' + ch.type + (ch.hasSecret ? ' • Secret' : '') + '</div>' +
                    '</div>' +
                    '</div>' +
                    '<div class="channel-url" title="' + DashboardUtils.escapeHtml(ch.url) + '">' + DashboardUtils.escapeHtml(ch.url) + '</div>' +
                    '<div class="channel-actions">' +
                    '<button class="btn btn-sm btn-outline-primary" onclick="NotificationModule.testChannel(\'' + ch.id + '\')">' +
                    '<i class="bi bi-send"></i></button>' +
                    '<button class="btn btn-sm btn-outline-secondary" onclick="NotificationModule.editChannel(\'' + ch.id + '\')">' +
                    '<i class="bi bi-pencil"></i></button>' +
                    '<button class="btn btn-sm btn-outline-danger" onclick="NotificationModule.deleteChannel(\'' + ch.id + '\')">' +
                    '<i class="bi bi-trash"></i></button>' +
                    '</div>' +
                    '</div>';
            });

            container.innerHTML = html;
        },

        renderRules: function(rules) {
            var container = document.getElementById('notif-rules-list');
            if (!container) return;

            if (!rules || rules.length === 0) {
                container.innerHTML = '<div class="empty-state">' +
                    '<i class="bi bi-filter-circle"></i>' +
                    '<p>' + __('notif.noRules') + '</p>' +
                    '</div>';
                return;
            }

            var html = '';
            rules.forEach(function(rule) {
                var disabledClass = rule.enabled ? '' : 'disabled';
                var eventTags = rule.eventTypes && rule.eventTypes.length > 0
                    ? rule.eventTypes.map(function(e) {
                        var meta = window.NOTIF_EVENT_TYPES.find(function(m) { return m.value === e; });
                        var label = meta ? window._notifI18n(meta.i18nKey, meta.value) : e;
                        return '<span class="rule-event-tag">' + label + '</span>';
                    }).join('')
                    : '<span class="rule-event-tag">' + __('notif.allEvents') + '</span>';

                var channelChips = (rule.channelDetails || [])
                    .map(function(ch) { return '<span class="rule-channel-chip">' + DashboardUtils.escapeHtml(ch.name) + '</span>'; })
                    .join('');

                html += '<div class="rule-card ' + disabledClass + '" data-id="' + rule.id + '">' +
                    '<div class="rule-header">' +
                    '<div>' +
                    '<span class="rule-name">' + DashboardUtils.escapeHtml(rule.name) + '</span>' +
                    '<div class="rule-meta">' +
                    '<span><i class="bi bi-tags"></i> ' + (rule.eventTypes && rule.eventTypes.length > 0 ? rule.eventTypes.length + ' ' + (__('notif.events') || 'events') : (__('notif.allEvents') || 'All')) + '</span>' +
                    '<span><i class="bi bi-clock"></i> ' + rule.cooldownSeconds + 's</span>' +
                    '<span><i class="bi bi-megaphone"></i> ' + (rule.channelDetails ? rule.channelDetails.length : 0) + ' ' + (__('notif.channels') || 'channels') + '</span>' +
                    '</div>' +
                    '</div>' +
                    '<div class="d-flex align-items-center gap-2">' +
                    (rule.enabled ? '<span style="font-size:11px;color:#16a34a;background:#f0fdf4;padding:2px 8px;border-radius:10px;">' + (__('notif.enabled') || 'Enabled') + '</span>' : '<span style="font-size:11px;color:#94a3b8;background:#f1f5f9;padding:2px 8px;border-radius:10px;">' + (__('notif.disabled') || 'Disabled') + '</span>') +
                    '</div>' +
                    '</div>' +
                    '<div class="rule-events">' + eventTags + '</div>' +
                    '<div class="rule-channels">' + channelChips + '</div>' +
                    '<div class="channel-actions" style="margin-top:12px;">' +
                    '<button class="btn btn-sm btn-outline-secondary" onclick="NotificationModule.editRule(\'' + rule.id + '\')" title="' + (__('notif.editRule') || '编辑规则') + '">' +
                    '<i class="bi bi-pencil"></i></button>' +
                    '<button class="btn btn-sm btn-outline-danger" onclick="NotificationModule.deleteRule(\'' + rule.id + '\')" title="' + (__('notif.deleteRule') || '删除规则') + '">' +
                    '<i class="bi bi-trash"></i></button>' +
                    '</div>' +
                    '</div>';
            });

            container.innerHTML = html;
        },

        renderSettings: function(settings) {
            var container = document.getElementById('notif-settings-form');
            if (!container) return;

            var html = '<div class="settings-section">' +
                '<h6 class="settings-section-title"><i class="bi bi-gear me-2"></i>' + __('notif.settings.general') + '</h6>' +
                '<div class="mb-3">' +
                '<label class="form-label">' + __('notif.settings.enabled') + '</label>' +
                '<div class="form-check form-switch">' +
                '<input type="checkbox" class="form-check-input" id="settings-enabled" ' + (settings.enabled ? 'checked' : '') + '>' +
                '</div>' +
                '</div>' +
                '<div class="mb-3">' +
                '<label class="form-label">' + __('notif.settings.maxRecords') + '</label>' +
                '<input type="number" class="form-control" id="settings-maxRecords" value="' + (settings.maxHistoryRecords || 500) + '" min="50" max="10000" step="50">' +
                '</div>' +
                '</div>' +

                '<div class="settings-section">' +
                '<h6 class="settings-section-title"><i class="bi bi-globe me-2"></i>' + __('notif.settings.webhook') + '</h6>' +
                '<div class="mb-3">' +
                '<label class="form-label">' + __('notif.settings.timeout') + ' (s)</label>' +
                '<input type="number" class="form-control" id="settings-timeout" value="' + (settings.defaultTimeoutSeconds || 10) + '" min="1" max="60">' +
                '</div>' +
                '<div class="mb-3">' +
                '<label class="form-label">' + __('notif.settings.retryCount') + '</label>' +
                '<input type="number" class="form-control" id="settings-retry" value="' + (settings.defaultRetryCount || 1) + '" min="0" max="5">' +
                '</div>' +
                '</div>' +

                '<button class="btn btn-primary" onclick="NotificationModule.saveGlobalSettings()">' +
                '<i class="bi bi-check-lg me-2"></i>' + __('notif.save') + '</button>';

            container.innerHTML = html;
        },

        // ─── Toggle & Interactions ───────────────────────────────────────────

        toggleNotification: function(el) {
            el.classList.toggle('expanded');
        },

        // ─── Filter Handling ───────────────────────────────────────────────

        applyFilters: function() {
            this.currentFilters.eventType = document.getElementById('notif-type-filter')?.value || '';
            this.currentFilters.severity = document.getElementById('notif-severity-filter')?.value || '';
            this.currentFilters.keyword = document.getElementById('notif-keyword-filter')?.value || '';
            this.currentFilters.dateStart = document.getElementById('notif-date-start')?.value || '';
            this.currentFilters.dateEnd = document.getElementById('notif-date-end')?.value || '';
            this.loadHistory(1);
        },

        onKeywordChange: function(e) {
            // Debounce keyword search
            var self = this;
            clearTimeout(this._keywordTimer);
            this._keywordTimer = setTimeout(function() {
                self.applyFilters();
            }, 400);
        },

        onPageSizeChange: function() {
            var sel = document.getElementById('notif-page-size');
            if (sel) {
                this.pageSize = parseInt(sel.value) || 50;
                this.loadHistory(1);
            }
        },

        // ─── Channel Management ─────────────────────────────────────────────

        showChannelModal: function(channel) {
            document.getElementById('channel-id').value = channel?.id || '';
            document.getElementById('channel-name').value = channel?.name || '';
            document.getElementById('channel-type').value = channel?.type || 'Generic';
            document.getElementById('channel-url').value = channel?.url || '';
            document.getElementById('channel-secret').value = channel?.secret || '';
            document.getElementById('channel-secret').dataset.original = channel?.secret || '';
            document.getElementById('channel-enabled').checked = channel?.enabled !== false;

            var modal = new bootstrap.Modal(document.getElementById('channel-modal'));
            modal.show();
        },

        editChannel: function(id) {
            var channel = this._data.channels.find(function(c) { return c.id === id; });
            if (channel) this.showChannelModal(channel);
        },

        saveChannel: async function() {
            var id = document.getElementById('channel-id').value;
            var currentSecret = document.getElementById('channel-secret').value;
            var originalSecret = document.getElementById('channel-secret').dataset.original || '';

            var data = {
                id: id || undefined,
                name: document.getElementById('channel-name').value,
                type: document.getElementById('channel-type').value,
                url: document.getElementById('channel-url').value,
                enabled: document.getElementById('channel-enabled').checked
            };

            // On create, always send secret. On edit, only send if user actually changed it;
            // omitting it tells the backend to preserve the stored value.
            if (!id) {
                data.secret = currentSecret || undefined;
            } else if (currentSecret !== originalSecret) {
                data.secret = currentSecret || null; // empty input clears the secret
            }
            // else: user didn't touch secret → omit so backend preserves it

            if (!data.name || !data.url) {
                DashboardModals.showError(__('notif.validation.required'));
                return;
            }

            try {
                if (id) {
                    await DashboardApi.put('/api/notifications/channels/' + id, data);
                } else {
                    await DashboardApi.post('/api/notifications/channels', data);
                }
                bootstrap.Modal.getInstance(document.getElementById('channel-modal'))?.hide();
                this.loadSettings();
                DashboardModals.showToast(__('notif.saved'), 'success');
            } catch (e) {
                console.error('[Notification] Save channel failed:', e);
                DashboardModals.showError(__('notif.saveFailed'));
            }
        },

        deleteChannel: async function(id) {
            window.DashboardModals.showConfirm(__('notif.deleteChannelConfirm'), async function() {
                try {
                    await window.DashboardApi.deleteNotificationChannel(channelId);
                    window.DashboardModals.showSuccess(__('notif.channelDeleted'));
                    await self.loadChannels();
                } catch (e) { window.DashboardModals.showError(__('notif.deleteFailed')); }
            }, null, { danger: true });
            try {
                await DashboardApi.delete('/api/notifications/channels/' + id);
                this.loadSettings();
                DashboardModals.showToast(__('notif.deleted'), 'success');
            } catch (e) {
                console.error('[Notification] Delete channel failed:', e);
                DashboardModals.showError(__('notif.deleteFailed'));
            }
        },

        testChannel: async function(id) {
            try {
                var resp = await DashboardApi.post('/api/notifications/channels/' + id + '/test', {});
                DashboardModals.showToast(__('notif.testSent'), 'success');
            } catch (e) {
                console.error('[Notification] Test channel failed:', e);
                DashboardModals.showError(__('notif.testFailed'));
            }
        },

        // ─── Rule Management ───────────────────────────────────────────────

        showRuleModal: function(rule) {
            document.getElementById('rule-id').value = rule?.id || '';
            document.getElementById('rule-name').value = rule?.name || '';
            document.getElementById('rule-cooldown').value = rule?.cooldownSeconds || 300;
            document.getElementById('rule-record').checked = rule?.recordToHistory !== false;
            document.getElementById('rule-enabled').checked = rule?.enabled !== false;

            // Config-change sub-types for legacy "ConfigChange" umbrella compatibility
            var configSubTypes = ['AddRoute','UpdateRoute','RemoveRoute','AddCluster','UpdateCluster','RemoveCluster','RollbackConfig'];
            var hasLegacyConfigChange = rule?.eventTypes?.indexOf('ConfigChange') >= 0;

            // Event types
            var eventCheckboxes = document.querySelectorAll('#rule-event-types input[type="checkbox"]');
            eventCheckboxes.forEach(function(cb) {
                if (hasLegacyConfigChange && configSubTypes.indexOf(cb.value) >= 0) {
                    cb.checked = true;  // legacy umbrella → check all sub-types
                } else {
                    cb.checked = rule?.eventTypes?.indexOf(cb.value) >= 0 || false;
                }
            });

            // Refresh the "Select All" master checkbox if it exists
            var master = document.getElementById('event-select-all');
            if (master) {
                var allCbs = document.querySelectorAll('#rule-event-types input.event-type-cb');
                master.checked = allCbs.length > 0 && Array.from(allCbs).every(function(c) { return c.checked; });
            }

            // Channel selection
            this.renderChannelSelect(rule?.channelIds || []);

            var modal = new bootstrap.Modal(document.getElementById('rule-modal'));
            modal.show();
        },

        renderChannelSelect: function(selectedIds) {
            var container = document.getElementById('rule-channel-select');
            if (!container) return;

            var channels = this._data.channels;
            if (channels.length === 0) {
                container.innerHTML = '<div class="text-muted" style="font-size:13px;">' + __('notif.noChannels') + '</div>';
                return;
            }

            var html = '';
            var self = this;
            channels.forEach(function(ch) {
                var checked = selectedIds.includes(ch.id) ? 'checked' : '';
                html += '<label class="channel-select-item">' +
                    '<input type="checkbox" value="' + ch.id + '" ' + checked + '>' +
                    '<i class="bi ' + (ch.type === 'DingTalk' ? 'bi-chat-dots-fill' : 'bi-link-45deg') + ' me-1" style="color:' + (ch.type === 'DingTalk' ? '#0089ff' : '#6366f1') + ';"></i>' +
                    DashboardUtils.escapeHtml(ch.name) +
                    '</label>';
            });
            container.innerHTML = html;
        },

        editRule: function(id) {
            var rule = this._data.rules.find(function(r) { return r.id === id; });
            if (rule) this.showRuleModal(rule);
        },

        saveRule: async function() {
            var id = document.getElementById('rule-id').value;
            var eventTypes = [];
            document.querySelectorAll('#rule-event-types input[type="checkbox"]:checked').forEach(function(cb) {
                eventTypes.push(cb.value);
            });

            var channelIds = [];
            document.querySelectorAll('#rule-channel-select input[type="checkbox"]:checked').forEach(function(cb) {
                channelIds.push(cb.value);
            });

            var data = {
                id: id || undefined,
                name: document.getElementById('rule-name').value,
                minSeverity: 'Info',
                eventTypes: eventTypes,
                channelIds: channelIds,
                cooldownSeconds: parseInt(document.getElementById('rule-cooldown').value) || 300,
                recordToHistory: document.getElementById('rule-record').checked,
                enabled: document.getElementById('rule-enabled').checked
            };

            if (!data.name || channelIds.length === 0) {
                DashboardModals.showError(__('notif.validation.required'));
                return;
            }

            try {
                if (id) {
                    await DashboardApi.put('/api/notifications/rules/' + id, data);
                } else {
                    await DashboardApi.post('/api/notifications/rules', data);
                }
                bootstrap.Modal.getInstance(document.getElementById('rule-modal'))?.hide();
                this.loadSettings();
                DashboardModals.showToast(__('notif.saved'), 'success');
            } catch (e) {
                console.error('[Notification] Save rule failed:', e);
                DashboardModals.showError(__('notif.saveFailed'));
            }
        },

        deleteRule: async function(id) {
            window.DashboardModals.showConfirm(__('notif.deleteRuleConfirm'), async function() {
                try {
                    await window.DashboardApi.deleteNotificationRule(ruleId);
                    window.DashboardModals.showSuccess(__('notif.ruleDeleted'));
                    await self.loadRules();
                } catch (e) { window.DashboardModals.showError(__('notif.deleteFailed')); }
            }, null, { danger: true });
            try {
                await DashboardApi.delete('/api/notifications/rules/' + id);
                this.loadSettings();
                DashboardModals.showToast(__('notif.deleted'), 'success');
            } catch (e) {
                console.error('[Notification] Delete rule failed:', e);
                DashboardModals.showError(__('notif.deleteFailed'));
            }
        },

        // ─── Global Settings ───────────────────────────────────────────────

        saveGlobalSettings: async function() {
            var data = {
                globalSettings: {
                    enabled: document.getElementById('settings-enabled').checked,
                    maxHistoryRecords: parseInt(document.getElementById('settings-maxRecords').value) || 500,
                    defaultTimeoutSeconds: parseInt(document.getElementById('settings-timeout').value) || 10,
                    defaultRetryCount: parseInt(document.getElementById('settings-retry').value) || 1
                }
            };

            try {
                await DashboardApi.put('/api/notifications/settings', data);
                this.loadSettings();
                DashboardModals.showToast(__('notif.saved'), 'success');
            } catch (e) {
                console.error('[Notification] Save settings failed:', e);
                DashboardModals.showError(__('notif.saveFailed'));
            }
        },

        // ─── Actions ───────────────────────────────────────────────────────

        clearHistory: async function() {
            window.DashboardModals.showConfirm(__('notif.clearHistoryConfirm'), async function() {
                try {
                    await window.DashboardApi.clearNotificationHistory();
                    window.DashboardModals.showSuccess(__('notif.clearSuccess'));
                    await self.loadHistory();
                } catch (e) { window.DashboardModals.showError(__('notif.clearFailed')); }
            }, null, { danger: true });
            try {
                await DashboardApi.delete('/api/notifications/history');
                this.loadHistory();
                this.loadSummary();
                DashboardModals.showToast(__('notif.cleared'), 'success');
            } catch (e) {
                console.error('[Notification] Clear history failed:', e);
                DashboardModals.showError(__('notif.clearFailed'));
            }
        },

        testNotification: async function() {
            try {
                await DashboardApi.post('/api/notifications/test', {});
                // Refresh history and summary to show the test notification record immediately
                this.loadHistory(1, true);
                this.loadSummary();
                DashboardModals.showToast(__('notif.testSent') || 'Test notification sent', 'success');
            } catch (e) {
                console.error('[Notification] Test notification failed:', e);
                DashboardModals.showError(__('notif.testFailed') || 'Test failed');
            }
        }
    };

    // Register with dashboard app
    if (window.DashboardApp) {
        window.DashboardApp.registerModule('notification', NotificationModule);
    }
    window.NotificationModule = NotificationModule;
})();
