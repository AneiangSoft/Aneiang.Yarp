/**
 * Security Events Module - WAF event history viewer
 * Optimized with test button, IP search, time grouping, relative time
 */
(function() {
    'use strict';

    var SecurityModule = {
        name: 'security',
        initialized: false,
        autoRefreshInterval: null,
        currentFilter: { type: '', ip: '' },
        _allEntries: [],
        _typeCounts: {},
        _topIps: {},

        init: function() {
            if (this.initialized) return;
            this.setupEvents();
            this.initialized = true;
        },

        setupEvents: function() {
            var self = this;
            document.addEventListener('dashboard:ready', function() {
                if (self.autoRefreshInterval) clearInterval(self.autoRefreshInterval);
                self.autoRefreshInterval = setInterval(function() { self.load(); }, 15000);
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

                this._allEntries = (eventsData && eventsData.entries) || [];
                if (summaryData) {
                    this._typeCounts = summaryData.typeCounts || {};
                    this._topIps = summaryData.topIps || {};
                } else {
                    this._typeCounts = {};
                    this._topIps = {};
                }

                this.renderWithData(container);
                this.updateRefreshTime();
                this.bindFilterEvents();
                this.bindTestButton();
            } catch (error) {
                console.error('[Security] Load failed:', error);
                var container = document.getElementById('security-content');
                if (container) {
                    container.innerHTML = '<div class="alert alert-danger">' + __('security.loadFailed') + '</div>';
                }
            }
        },

        /** Get full label for event type from raw eventType string */
        getEventTypeLabel: function(type) {
            var labels = {
                'SqlInjectionBlocked': __('security.sqli'),
                'SqlInjectionValueBlocked': __('security.sqli') + ' (Value)',
                'XssBlocked': __('security.xss'),
                'PathTraversalBlocked': __('security.pathTraversal'),
                'PathTraversalInQueryBlocked': __('security.pathTraversal') + ' (Query)',
                'IpBlocked': __('security.ipBlock'),
                'RequestSizeBlocked': 'Oversized Request',
                'MalformedHeadersBlocked': 'Malformed Headers',
                'UriTooLongBlocked': 'URI Too Long'
            };
            return labels[type] || this._simpleType(type);
        },

        _simpleType: function(type) {
            if (type.indexOf('SqlInjection') >= 0) return __('security.sqli');
            if (type.indexOf('Xss') >= 0) return __('security.xss');
            if (type.indexOf('PathTraversal') >= 0) return __('security.pathTraversal');
            if (type.indexOf('IpBlock') >= 0) return __('security.ipBlock');
            return type;
        },

        getEventTypeClass: function(type) {
            if (type.indexOf('SqlInjection') >= 0) return 'bg-danger';
            if (type.indexOf('Xss') >= 0) return 'bg-warning text-dark';
            if (type.indexOf('PathTraversal') >= 0) return 'bg-info';
            if (type.indexOf('IpBlock') >= 0) return 'bg-dark';
            if (type.indexOf('RequestSize') >= 0) return 'bg-secondary';
            if (type.indexOf('MalformedHeaders') >= 0) return 'bg-secondary';
            if (type.indexOf('UriTooLong') >= 0) return 'bg-secondary';
            return 'bg-secondary';
        },

        /** Group entries by time: today, yesterday, older */
        _groupByTime: function(entries) {
            var now = new Date();
            var todayStart = new Date(now.getFullYear(), now.getMonth(), now.getDate()).getTime();
            var yesterdayStart = todayStart - 86400000;
            var groups = { today: [], yesterday: [], older: [] };
            for (var i = 0; i < entries.length; i++) {
                var ts = new Date(entries[i].timestamp).getTime();
                if (ts >= todayStart) groups.today.push(entries[i]);
                else if (ts >= yesterdayStart) groups.yesterday.push(entries[i]);
                else groups.older.push(entries[i]);
            }
            return groups;
        },

        /** Format relative time string */
        _fmtRelTime: function(timestamp) {
            if (!timestamp) return '-';
            var diff = Math.floor((Date.now() - new Date(timestamp).getTime()) / 1000);
            if (diff < 5) return '\u521a\u521a';
            if (diff < 60) return diff + 's';
            if (diff < 3600) return Math.floor(diff / 60) + 'm';
            if (diff < 86400) return Math.floor(diff / 3600) + 'h';
            return Math.floor(diff / 86400) + 'd';
        },

        _eH: function(str) {
            if (typeof str !== 'string') return str || '';
            var div = document.createElement('div');
            div.textContent = str;
            return div.innerHTML;
        },

        /** Main render method using this._allEntries, this._typeCounts, this._topIps */
        renderWithData: function(container) {
            if (!container) return;
            window.DashboardDOM.clear(container);
            var entries = this._allEntries || [];

            // Build sections HTML
            var html = '';

            // --- Summary Section ---
            html += this._renderSummarySection();

            // --- Filter Section ---
            html += this._renderFilterSection(entries.length);

            if (entries.length === 0) {
                html += this._renderEmptyState();
                container.innerHTML = html;
                return;
            }

            // --- Grouped Event List ---
            var groups = this._groupByTime(entries);
            var groupedHtml = '';

            if (groups.today.length > 0) {
                groupedHtml += this._renderGroup('today', groups.today);
            }
            if (groups.yesterday.length > 0) {
                groupedHtml += this._renderGroup('yesterday', groups.yesterday);
            }
            if (groups.older.length > 0) {
                groupedHtml += this._renderGroup('older', groups.older);
            }

            html += '<div class="audit-list">' + groupedHtml + '</div>';
            container.innerHTML = html;
        },

        _renderSummarySection: function() {
            var html = '<div class="row g-2 mb-3">';

            // Type counts bar
            var typeKeys = Object.keys(this._typeCounts).slice(0, 5);
            var typeRows = '';
            if (typeKeys.length > 0) {
                var maxTypeCount = this._typeCounts[typeKeys[0]] || 1;
                typeRows = typeKeys.map(function(type) {
                    var count = this._typeCounts[type];
                    var pct = Math.min(100, (count / maxTypeCount) * 100);
                    var cls = this.getEventTypeClass(type);
                    var label = this._simpleType(type);
                    return '<div class="stats-bar-row">' +
                        '<span class="stats-bar-label"><span class="badge ' + cls + '" style="font-size:9px;">' + this._eH(label) + '</span></span>' +
                        '<div class="stats-bar-track"><div class="stats-bar-fill" style="width:' + pct + '%;background:#ef4444;"></div></div>' +
                        '<span class="stats-bar-value" style="color:#dc2626;font-weight:600;">' + count + '</span>' +
                    '</div>';
                }.bind(this)).join('');
            } else {
                typeRows = '<div class="text-muted small py-2">' + __('security.empty') + '</div>';
            }

            html += '<div class="col-md-7">' +
                '<div class="border rounded-3 p-3 h-100" style="background:#fcfcfc;">' +
                    '<div class="small fw-medium mb-2"><i class="bi bi-bar-chart-fill me-1 text-danger"></i>' + __('security.byType') + '</div>' +
                    typeRows +
                '</div>' +
            '</div>';

            // Top IPs
            var ipKeys = Object.keys(this._topIps).slice(0, 5);
            var ipRows = '';
            if (ipKeys.length > 0) {
                var maxIpCount = this._topIps[ipKeys[0]] || 1;
                ipRows = ipKeys.map(function(ip) {
                    var count = this._topIps[ip];
                    var pct = Math.min(100, (count / maxIpCount) * 100);
                    return '<div class="stats-bar-row">' +
                        '<span class="stats-bar-label" style="font-family:monospace;font-size:11px;">' + this._eH(ip) + '</span>' +
                        '<div class="stats-bar-track"><div class="stats-bar-fill bg-dark" style="width:' + pct + '%"></div></div>' +
                        '<span class="stats-bar-value">' + count + '</span>' +
                    '</div>';
                }.bind(this)).join('');
            } else {
                ipRows = '<div class="text-muted small py-2">' + __('security.empty') + '</div>';
            }

            html += '<div class="col-md-5">' +
                '<div class="border rounded-3 p-3 h-100" style="background:#fcfcfc;">' +
                    '<div class="small fw-medium mb-2"><i class="bi bi-geo-alt-fill me-1 text-dark"></i>' + __('security.topIps') + '</div>' +
                    ipRows +
                '</div>' +
            '</div>';

            html += '</div>';
            return html;
        },

        _renderFilterSection: function(totalCount) {
            return '<div class="card-body py-2 border-bottom mb-3">' +
                '<div class="row g-2 align-items-center">' +
                    '<div class="col-auto">' +
                        '<select class="form-select form-select-sm" id="sec-type-filter" style="width:140px;">' +
                            '<option value="">' + __('alert.type.all') + '</option>' +
                            '<option value="SqlInjection">SQL Injection</option>' +
                            '<option value="Xss">XSS</option>' +
                            '<option value="PathTraversal">Path Traversal</option>' +
                            '<option value="IpBlock">IP Block</option>' +
                        '</select>' +
                    '</div>' +
                    '<div class="col-auto">' +
                        '<div class="input-group input-group-sm">' +
                            '<span class="input-group-text" style="background:transparent;border-right:none;"><i class="bi bi-search" style="font-size:11px;"></i></span>' +
                            '<input type="text" class="form-control form-control-sm" id="sec-ip-search" placeholder="' + __('security.ip') + '" style="width:140px;border-left:none;padding-left:0;">' +
                        '</div>' +
                    '</div>' +
                    '<div class="col-auto">' +
                        '<span class="text-muted small">' +
                            __('security.total').replace('{count}', totalCount) +
                        '</span>' +
                    '</div>' +
                '</div>' +
            '</div>';
        },

        _renderEmptyState: function() {
            return '<div class="text-center py-5">' +
                '<i class="bi bi-shield-lock text-muted" style="font-size:48px;"></i>' +
                '<p class="text-muted mt-3">' + __('security.empty') + '</p>' +
                '<p class="text-muted small">' + __('security.emptyHelp') + '</p>' +
                '<button class="btn btn-sm btn-outline-danger mt-2" id="sec-empty-test-btn">' +
                    '<i class="bi bi-bug me-1"></i>' + __('waf.testEvent') +
                '</button>' +
            '</div>';
        },

        _renderGroup: function(groupKey, entries) {
            var labels = {
                today: '\u4eca\u5929',
                yesterday: '\u6628\u5929',
                older: '\u66f4\u65e9'
            };
            var label = labels[groupKey] || groupKey;

            var rows = entries.map(function(entry) {
                var typeClass = this.getEventTypeClass(entry.eventType);
                var typeLabel = this.getEventTypeLabel(entry.eventType);
                var timeStr = this._fmtRelTime(entry.timestamp);
                var fullTime = window.DashboardI18n.formatDate(entry.timestamp);
                var blockedBadge = entry.blocked
                    ? '<span class="badge bg-danger" style="font-size:10px;">' + __('security.blocked') + '</span>'
                    : '<span class="badge bg-success" style="font-size:10px;">' + __('security.allowed') + '</span>';

                var detailFields = [];
                if (entry.requestUri) detailFields.push('<span><strong>URI:</strong> <code>' + this._eH(entry.requestUri) + '</code></span>');
                if (entry.requestMethod) detailFields.push('<span><strong>Method:</strong> ' + this._eH(entry.requestMethod) + '</span>');
                if (entry.ruleName) detailFields.push('<span><strong>Rule:</strong> ' + this._eH(entry.ruleName) + '</span>');
                if (entry.matchedValue) detailFields.push('<span><strong>Match:</strong> <code class="text-danger">' + this._eH(entry.matchedValue) + '</code></span>');
                if (entry.statusCode) detailFields.push('<span><strong>Status:</strong> ' + entry.statusCode + '</span>');

                return '<div class="audit-entry" onclick="SecurityModule.toggleDetail(this)" data-sec-type="' + this._eH(entry.eventType) + '" data-sec-ip="' + this._eH(entry.clientIp || '') + '">' +
                    '<div class="audit-row">' +
                        '<span class="audit-time" title="' + this._eH(fullTime) + '">' + timeStr + '</span>' +
                        '<span class="badge ' + typeClass + '" style="font-size:10px;">' + this._eH(typeLabel) + '</span>' +
                        '<span class="audit-target" style="flex:1;"><i class="bi bi-geo-alt me-1"></i><code>' + this._eH(entry.clientIp) + '</code></span>' +
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

            if (rows.length === 0) return '';

            return '<div class="mb-1">' +
                '<div class="d-flex align-items-center gap-2 mb-2 px-1">' +
                    '<span class="small fw-medium text-muted">' + label + '</span>' +
                    '<span class="badge bg-light text-muted" style="font-size:10px;">' + entries.length + '</span>' +
                '</div>' +
                rows +
            '</div>';
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

            var ipSearch = document.getElementById('sec-ip-search');
            if (ipSearch) {
                ipSearch.value = this.currentFilter.ip;
                // Debounced search
                var debounceTimer = null;
                ipSearch.oninput = function(e) {
                    if (debounceTimer) clearTimeout(debounceTimer);
                    debounceTimer = setTimeout(function() {
                        self.currentFilter.ip = e.target.value.trim();
                        self.applyFilters();
                    }, 300);
                };
            }
        },

        applyFilters: function() {
            var entries = document.querySelectorAll('#security-content .audit-entry[data-sec-type]');
            var typeFilter = this.currentFilter.type;
            var ipFilter = this.currentFilter.ip.toLowerCase();

            entries.forEach(function(el) {
                var type = el.getAttribute('data-sec-type') || '';
                var ip = (el.getAttribute('data-sec-ip') || '').toLowerCase();
                var matchType = !typeFilter || type.indexOf(typeFilter) >= 0;
                var matchIp = !ipFilter || ip.indexOf(ipFilter) >= 0;
                el.style.display = (matchType && matchIp) ? '' : 'none';

                // Also handle visibility of its parent group
                var group = el.closest('.mb-1');
                if (group) {
                    var visibleEntries = group.querySelectorAll('.audit-entry[style*="display: none"]');
                    var allEntries = group.querySelectorAll('.audit-entry');
                    group.style.display = (visibleEntries.length === allEntries.length) ? 'none' : '';
                }
            });
        },

        toggleDetail: function(el) {
            var detail = el.querySelector('.audit-detail');
            var arrow = el.querySelector('.audit-arrow');
            if (detail) detail.classList.toggle('expanded');
            if (arrow) arrow.classList.toggle('expanded');
        },

        clearAll: function() {
            var self = this;
            window.DashboardModals.showConfirm(__('security.clearConfirm'), async function() {
                try {
                    await window.DashboardApi.clearSecurityEvents();
                    window.DashboardModals.showSuccess(__('security.clearSuccess'));
                    await self.load();
                } catch (error) {
                    console.error('[Security] Clear failed:', error);
                    window.DashboardModals.showError(__('security.clearFailed'));
                }
            }, null, { danger: true, confirmText: __('security.clear') });
        },

        updateRefreshTime: function() {
            var el = document.getElementById('security-refresh-time');
            if (el) {
                el.textContent = window.DashboardI18n.formatDate(new Date());
            }
        },

        bindTestButton: function() {
            var self = this;

            // Bind header test button
            var headerBtn = document.getElementById('sec-test-event-btn');
            if (headerBtn) {
                headerBtn.onclick = function() { self.fireTestEvent(); };
            }

            // Bind empty state test button  
            var emptyBtn = document.getElementById('sec-empty-test-btn');
            if (emptyBtn) {
                emptyBtn.onclick = function() { self.fireTestEvent(); };
            }
        },

        fireTestEvent: async function() {
            var types = ['SqlInjection', 'Xss', 'PathTraversal'];
            var uris = ['/api/users?id=1%27%20UNION%20SELECT%20*%20FROM%20users--', '/search?q=%3Cscript%3Ealert(1)%3C/script%3E', '/../../../etc/passwd'];
            var methods = ['GET', 'GET', 'GET'];
            var matchedValues = ["1' UNION SELECT * FROM users--", '<scr' + 'ipt>alert(1)<' + '/scr' + 'ipt>', '../../../etc/passwd'];
            var idx = Math.floor(Math.random() * types.length);

            var btns = document.querySelectorAll('#sec-test-event-btn, #sec-empty-test-btn');
            btns.forEach(function(b) { b.disabled = true; });

            try {
                var payload = {
                    clientIp: '192.168.' + Math.floor(Math.random() * 255) + '.' + Math.floor(Math.random() * 255),
                    eventType: types[idx],
                    uri: uris[idx],
                    method: methods[idx],
                    matchedValue: matchedValues[idx]
                };
                await window.DashboardApi.post('/api/security-events/test', payload);
                window.DashboardModals.showToast(__('waf.eventGenerated'), 'success');
                await this.load();
            } catch (e) {
                console.error('[Security] Test event failed:', e);
                window.DashboardModals.showError('Failed to generate test event');
            } finally {
                btns.forEach(function(b) { b.disabled = false; });
            }
        }
    };

    if (window.DashboardApp) {
        window.DashboardApp.registerModule('security', SecurityModule);
    }
    window.SecurityModule = SecurityModule;
})();
