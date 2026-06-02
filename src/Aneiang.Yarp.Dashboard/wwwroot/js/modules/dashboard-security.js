/**
 * Security Events Module - WAF event history viewer
 */
(function() {
    'use strict';

    var SecurityModule = {
        name: 'security',
        initialized: false,
        autoRefreshInterval: null,
        currentFilter: { type: '' },

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
                }, 15000);
            });
            document.addEventListener('dashboard:localeChange', function() { self.load(); });
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
                var container = document.getElementById('security-content');
                if (!container) return;

                window.DashboardDOM.showLoading(container, __('security.loading'));

                var eventsData = await window.DashboardApi.getSecurityEvents(200);
                var summaryData = null;
                try {
                    summaryData = await window.DashboardApi.getSecurityEventSummary();
                } catch (e) { /* ignore */ }

                this.render(eventsData, summaryData, container);
                this.updateRefreshTime();
            } catch (error) {
                console.error('[Security] Load failed:', error);
                var container = document.getElementById('security-content');
                if (container) {
                    container.innerHTML = '<div class="alert alert-danger">' + __('security.loadFailed') + '</div>';
                }
            }
        },

        getEventTypeLabel: function(type) {
            var labels = {
                'SqlInjection': __('security.sqli'),
                'Xss': __('security.xss'),
                'PathTraversal': __('security.pathTraversal'),
                'IpBlock': __('security.ipBlock'),
                'RequestSize': 'Oversized Request'
            };
            return labels[type] || type;
        },

        getEventTypeClass: function(type) {
            if (type === 'SqlInjection') return 'bg-danger';
            if (type === 'Xss') return 'bg-warning text-dark';
            if (type === 'PathTraversal') return 'bg-info';
            if (type === 'IpBlock') return 'bg-dark';
            return 'bg-secondary';
        },

        render: function(eventsData, summaryData, container) {
            window.DashboardDOM.clear(container);

            var entries = (eventsData && eventsData.entries) || [];

            var summaryHtml = '';
            if (summaryData) {
                var typeCounts = summaryData.typeCounts || {};
                var topIps = summaryData.topIps || {};
                var typeKeys = Object.keys(typeCounts).slice(0, 5);
                var ipKeys = Object.keys(topIps).slice(0, 5);

                var typeRows = typeKeys.map(function(type) {
                    var count = typeCounts[type];
                    var pct = Math.min(100, count * 10);
                    return '<div class="stats-bar-row">' +
                        '<span class="stats-bar-label">' + window.DashboardUtils.escapeHtml(this.getEventTypeLabel(type)) + '</span>' +
                        '<div class="stats-bar-track"><div class="stats-bar-fill" style="width:' + pct + '%"></div></div>' +
                        '<span class="stats-bar-value">' + count + '</span>' +
                    '</div>';
                }.bind(this)).join('');

                var ipRows = ipKeys.map(function(ip) {
                    var count = topIps[ip];
                    var pct = Math.min(100, count * 5);
                    return '<div class="stats-bar-row">' +
                        '<span class="stats-bar-label" title="' + window.DashboardUtils.escapeHtml(ip) + '">' + window.DashboardUtils.escapeHtml(ip) + '</span>' +
                        '<div class="stats-bar-track"><div class="stats-bar-fill bg-danger" style="width:' + pct + '%"></div></div>' +
                        '<span class="stats-bar-value">' + count + '</span>' +
                    '</div>';
                }).join('');

                summaryHtml =
                    '<div class="row mb-3">' +
                        '<div class="col-md-6">' +
                            '<div class="stats-card">' +
                                '<div class="stats-card-title">' + __('security.byType') + '</div>' +
                                (typeRows || '<div class="text-muted small">No data</div>') +
                            '</div>' +
                        '</div>' +
                        '<div class="col-md-6">' +
                            '<div class="stats-card">' +
                                '<div class="stats-card-title">' + __('security.topIps') + '</div>' +
                                (ipRows || '<div class="text-muted small">No data</div>') +
                            '</div>' +
                        '</div>' +
                    '</div>';
            }

            var filterHtml =
                '<div class="card-body py-2 border-bottom mb-3">' +
                    '<div class="row g-2 align-items-center">' +
                        '<div class="col-auto">' +
                            '<select class="form-select form-select-sm" id="sec-type-filter" style="width:150px;">' +
                                '<option value="">' + __('alert.type.all') + '</option>' +
                                '<option value="SqlInjection">SQL Injection</option>' +
                                '<option value="Xss">XSS</option>' +
                                '<option value="PathTraversal">Path Traversal</option>' +
                                '<option value="IpBlock">IP Block</option>' +
                            '</select>' +
                        '</div>' +
                        '<div class="col-auto">' +
                            '<span class="text-muted small">' +
                                __('security.total').replace('{count}', entries.length) +
                            '</span>' +
                        '</div>' +
                    '</div>' +
                '</div>';

            if (entries.length === 0) {
                container.innerHTML = summaryHtml + filterHtml +
                    '<div class="text-center py-5">' +
                        '<i class="bi bi-shield-lock text-muted" style="font-size:48px;"></i>' +
                        '<p class="text-muted mt-3">' + __('security.empty') + '</p>' +
                        '<p class="text-muted small">' + __('security.emptyHelp') + '</p>' +
                    '</div>';
                this.bindFilterEvents();
                return;
            }

            var rows = entries.map(function(entry) {
                var typeClass = this.getEventTypeClass(entry.eventType);
                var typeLabel = this.getEventTypeLabel(entry.eventType);
                var time = window.DashboardI18n.formatDate(entry.timestamp);
                var blockedBadge = entry.blocked
                    ? '<span class="badge bg-danger">' + __('security.blocked') + '</span>'
                    : '<span class="badge bg-success">' + __('security.allowed') + '</span>';

                var detailFields = [];
                if (entry.requestUri) detailFields.push('<span><strong>URI:</strong> <code>' + window.DashboardUtils.escapeHtml(entry.requestUri) + '</code></span>');
                if (entry.requestMethod) detailFields.push('<span><strong>Method:</strong> ' + window.DashboardUtils.escapeHtml(entry.requestMethod) + '</span>');
                if (entry.ruleName) detailFields.push('<span><strong>Rule:</strong> ' + window.DashboardUtils.escapeHtml(entry.ruleName) + '</span>');
                if (entry.matchedValue) detailFields.push('<span><strong>Match:</strong> <code class="text-danger">' + window.DashboardUtils.escapeHtml(entry.matchedValue) + '</code></span>');
                if (entry.statusCode) detailFields.push('<span><strong>Status:</strong> ' + entry.statusCode + '</span>');

                return '<div class="audit-entry" onclick="SecurityModule.toggleDetail(this)" data-sec-type="' + window.DashboardUtils.escapeHtml(entry.eventType) + '">' +
                    '<div class="audit-row">' +
                        '<span class="audit-time">' + time + '</span>' +
                        '<span class="badge ' + typeClass + '">' + window.DashboardUtils.escapeHtml(typeLabel) + '</span>' +
                        '<span class="audit-target" style="flex:1;"><i class="bi bi-geo-alt me-1"></i><code>' + window.DashboardUtils.escapeHtml(entry.clientIp) + '</code></span>' +
                        blockedBadge +
                        '<i class="bi bi-chevron-right audit-arrow"></i>' +
                    '</div>' +
                    '<div class="audit-detail">' +
                        '<div class="row g-2">' +
                            '<div class="col-12">' + detailFields.join('&nbsp;') + '</div>' +
                        '</div>' +
                    '</div>' +
                '</div>';
            }.bind(this)).join('');

            container.innerHTML = summaryHtml + filterHtml +
                '<div class="audit-list">' + rows + '</div>';

            this.bindFilterEvents();
        },

        bindFilterEvents: function() {
            var self = this;
            var typeFilter = document.getElementById('sec-type-filter');
            if (typeFilter) {
                typeFilter.value = this.currentFilter.type;
                typeFilter.onchange = function(e) {
                    self.currentFilter.type = e.target.value;
                    self.applyFilters();
                };
            }
        },

        applyFilters: function() {
            var entries = document.querySelectorAll('#security-content .audit-entry[data-sec-type]');
            var typeFilter = this.currentFilter.type;

            entries.forEach(function(el) {
                var type = el.getAttribute('data-sec-type') || '';
                el.style.display = (!typeFilter || type === typeFilter) ? '' : 'none';
            });
        },

        toggleDetail: function(el) {
            var detail = el.querySelector('.audit-detail');
            var arrow = el.querySelector('.audit-arrow');
            if (detail) detail.classList.toggle('expanded');
            if (arrow) arrow.classList.toggle('expanded');
        },

        clearAll: async function() {
            if (!confirm(__('security.clearConfirm'))) return;
            try {
                await window.DashboardApi.clearSecurityEvents();
                if (window.DashboardModals) {
                    window.DashboardModals.showToast(__('security.clearSuccess'), 'success');
                }
                await this.load();
            } catch (error) {
                console.error('[Security] Clear failed:', error);
                if (window.DashboardModals) {
                    window.DashboardModals.showError(__('security.clearFailed'));
                }
            }
        },

        updateRefreshTime: function() {
            var el = document.getElementById('security-refresh-time');
            if (el) {
                el.textContent = window.DashboardI18n.formatDate(new Date());
            }
        }
    };

    if (window.DashboardApp) {
        window.DashboardApp.registerModule('security', SecurityModule);
    }
    window.SecurityModule = SecurityModule;
})();
