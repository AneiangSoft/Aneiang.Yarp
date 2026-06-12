/**
 * Notification Center Module - Unified notification and alert management
 */
(function() {
    'use strict';

    var NotificationModule = {
        name: 'notification',
        initialized: false,
        autoRefreshInterval: null,
        currentPage: 1,
        pageSize: 50,
        currentFilters: { eventType: '', severity: '' },

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
            this.setupEvents();
            this.setupTabs();
            this.loadAll();
            this.initialized = true;
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

        loadHistory: async function(page) {
            if (page) this.currentPage = page;

            try {
                var params = {
                    page: this.currentPage,
                    pageSize: this.pageSize,
                    eventType: this.currentFilters.eventType || undefined,
                    severity: this.currentFilters.severity || undefined
                };
                var resp = await DashboardApi.get('/api/notifications/history', params);
                if (resp) {
                    this._data.history = resp.entries || [];
                    this.renderHistory(resp);
                }
            } catch (e) {
                console.error('[Notification] Load history failed:', e);
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
            var errorCount = document.getElementById('notif-error-count');
            var warningCount = document.getElementById('notif-warning-count');
            var channelCount = document.getElementById('notif-channel-count');

            if (totalCount) totalCount.textContent = data.total || 0;
            if (errorCount) errorCount.textContent = (data.bySeverity && data.bySeverity.Error) || 0;
            if (warningCount) warningCount.textContent = (data.bySeverity && data.bySeverity.Warning) || 0;
            if (channelCount) channelCount.textContent = data.channelsConfigured || 0;
        },

        renderHistory: function(data) {
            var container = document.getElementById('notif-history-list');
            if (!container) return;

            if (!data.entries || data.entries.length === 0) {
                container.innerHTML = '<div class="empty-state">' +
                    '<i class="bi bi-bell"></i>' +
                    '<p>' + __('notif.emptyHistory') + '</p>' +
                    '</div>';
                this.renderPagination(data.total, data.page, data.pageSize);
                return;
            }

            var html = '';
            var self = this;
            data.entries.forEach(function(entry) {
                var sevClass = entry.severity === 'Error' ? 'error' :
                               entry.severity === 'Warning' ? 'warning' : 'info';
                var time = DashboardI18n.formatDate(entry.timestamp);
                var details = self.buildHistoryDetails(entry);

                html += '<div class="notification-item" onclick="NotificationModule.toggleNotification(this)" data-id="' + entry.id + '">' +
                    '<span class="notif-time">' + time + '</span>' +
                    '<span class="notif-badge ' + sevClass + '">' + entry.severity + '</span>' +
                    '<span class="notif-badge" style="background:#f1f5f9;color:#475569;">' + entry.eventType + '</span>' +
                    '<span class="notif-message" style="flex:1;">' + DashboardUtils.escapeHtml(entry.title) + '</span>' +
                    '<i class="bi bi-chevron-right notif-arrow"></i>' +
                    '<div class="notif-details">' + details + '</div>' +
                    '</div>';
            });

            container.innerHTML = html;
            this.renderPagination(data.total, data.page, data.pageSize);
        },

        buildHistoryDetails: function(entry) {
            var html = '<div class="notif-detail-row">';
            if (entry.clusterId) html += '<span><strong>Cluster:</strong> <code>' + DashboardUtils.escapeHtml(entry.clusterId) + '</code></span>';
            if (entry.routeId) html += '<span><strong>Route:</strong> <code>' + DashboardUtils.escapeHtml(entry.routeId) + '</code></span>';
            if (entry.clientIp) html += '<span><strong>IP:</strong> <code>' + DashboardUtils.escapeHtml(entry.clientIp) + '</code></span>';
            html += '</div>';
            if (entry.message) html += '<div style="margin-top:8px;">' + DashboardUtils.escapeHtml(entry.message) + '</div>';
            if (entry.blockReason) html += '<div style="margin-top:4px;color:#dc2626;"><strong>Reason:</strong> ' + DashboardUtils.escapeHtml(entry.blockReason) + '</div>';
            if (entry.requestUri) html += '<div style="margin-top:4px;"><strong>URI:</strong> <code>' + DashboardUtils.escapeHtml(entry.requestUri) + '</code></div>';
            if (entry.errorMessage) html += '<div style="margin-top:4px;"><strong>Error:</strong> ' + DashboardUtils.escapeHtml(entry.errorMessage) + '</div>';
            if (entry.attemptCount) html += '<div style="margin-top:4px;"><strong>Attempts:</strong> ' + entry.attemptCount + '</div>';
            return html;
        },

        renderPagination: function(total, page, pageSize) {
            var container = document.getElementById('notif-history-pagination');
            if (!container || total <= pageSize) {
                if (container) container.innerHTML = '';
                return;
            }

            var totalPages = Math.ceil(total / pageSize);
            var html = '';

            html += '<button class="pagination-btn" onclick="NotificationModule.loadHistory(' + (page - 1) + ')" ' + (page <= 1 ? 'disabled' : '') + '>' +
                '<i class="bi bi-chevron-left"></i></button>';

            var start = Math.max(1, page - 2);
            var end = Math.min(totalPages, page + 2);

            if (start > 1) {
                html += '<button class="pagination-btn" onclick="NotificationModule.loadHistory(1)">1</button>';
                if (start > 2) html += '<span style="padding:0 8px;color:#94a3b8;">...</span>';
            }

            for (var i = start; i <= end; i++) {
                html += '<button class="pagination-btn ' + (i === page ? 'active' : '') + '" onclick="NotificationModule.loadHistory(' + i + ')">' + i + '</button>';
            }

            if (end < totalPages) {
                if (end < totalPages - 1) html += '<span style="padding:0 8px;color:#94a3b8;">...</span>';
                html += '<button class="pagination-btn" onclick="NotificationModule.loadHistory(' + totalPages + ')">' + totalPages + '</button>';
            }

            html += '<button class="pagination-btn" onclick="NotificationModule.loadHistory(' + (page + 1) + ')" ' + (page >= totalPages ? 'disabled' : '') + '>' +
                '<i class="bi bi-chevron-right"></i></button>';

            container.innerHTML = html;
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
                    ? rule.eventTypes.map(function(e) { return '<span class="rule-event-tag">' + e + '</span>'; }).join('')
                    : '<span class="rule-event-tag">' + __('notif.allEvents') + '</span>';

                var channelChips = (rule.channelDetails || [])
                    .map(function(ch) { return '<span class="rule-channel-chip">' + DashboardUtils.escapeHtml(ch.name) + '</span>'; })
                    .join('');

                html += '<div class="rule-card ' + disabledClass + '" data-id="' + rule.id + '">' +
                    '<div class="rule-header">' +
                    '<span class="rule-name">' + DashboardUtils.escapeHtml(rule.name) + '</span>' +
                    '<div class="rule-meta">' +
                    '<span><i class="bi bi-clock"></i> ' + rule.cooldownSeconds + 's</span>' +
                    '<span><i class="bi bi-' + (rule.minSeverity === 'Error' ? 'exclamation-circle text-danger' : 'info-circle') + '"></i> ' + rule.minSeverity + '</span>' +
                    '</div>' +
                    '</div>' +
                    '<div class="rule-events">' + eventTags + '</div>' +
                    '<div class="rule-channels">' + channelChips + '</div>' +
                    '<div class="channel-actions" style="margin-top:12px;">' +
                    '<button class="btn btn-sm btn-outline-secondary" onclick="NotificationModule.editRule(\'' + rule.id + '\')">' +
                    '<i class="bi bi-pencil"></i></button>' +
                    '<button class="btn btn-sm btn-outline-danger" onclick="NotificationModule.deleteRule(\'' + rule.id + '\')">' +
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
            this.loadHistory(1);
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
            if (!confirm(__('notif.deleteChannelConfirm'))) return;
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
            document.getElementById('rule-severity').value = rule?.minSeverity || 'Info';
            document.getElementById('rule-cooldown').value = rule?.cooldownSeconds || 300;
            document.getElementById('rule-record').checked = rule?.recordToHistory !== false;
            document.getElementById('rule-enabled').checked = rule?.enabled !== false;

            // Event types
            var eventCheckboxes = document.querySelectorAll('#rule-event-types input[type="checkbox"]');
            eventCheckboxes.forEach(function(cb) {
                cb.checked = rule?.eventTypes?.includes(cb.value) || false;
            });

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
                minSeverity: document.getElementById('rule-severity').value,
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
            if (!confirm(__('notif.deleteRuleConfirm'))) return;
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
            if (!confirm(__('notif.clearHistoryConfirm'))) return;
            try {
                await DashboardApi.delete('/api/notifications/history');
                this.loadHistory();
                this.loadSummary();
                DashboardModals.showToast(__('notif.cleared'), 'success');
            } catch (e) {
                console.error('[Notification] Clear history failed:', e);
                DashboardModals.showError(__('notif.clearFailed'));
            }
        }
    };

    // Register with dashboard app
    if (window.DashboardApp) {
        window.DashboardApp.registerModule('notification', NotificationModule);
    }
    window.NotificationModule = NotificationModule;
})();
