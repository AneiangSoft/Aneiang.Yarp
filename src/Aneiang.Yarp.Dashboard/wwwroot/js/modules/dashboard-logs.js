/**
 * Dashboard Logs Module - Log viewer with filtering and polling
 */
(function() {
    'use strict';

    const LogsModule = {
        name: 'logs',
        initialized: false,
        pollTimer: null,

        // ===== Initialization =====
        init: async function() {
            if (this.initialized) return;
            
            console.log('[Logs] Initializing...');
            
            try {
                // Load initial logs
                await this.loadLogs();
                
                // Setup event listeners
                this.setupEvents();
                
                this.initialized = true;
                console.log('[Logs] Initialized');
            } catch (error) {
                console.error('[Logs] Init failed:', error);
                throw error;
            }
        },

        // ===== Load Logs =====
        loadLogs: async function() {
            try {
                const container = window.DashboardDOM.safe('#log-entries');
                if (!container) return;

                const state = window.DashboardState;
                const maxCount = state.get('filters.logs.maxCount') || 100;

                window.DashboardDOM.showLoading(container, __('index.log.loading'));

                const result = await window.DashboardApi.endpoints.getLogs(maxCount);
                
                // Update state
                state.set('data.logs', result.entries || []);

                // Render logs
                this.renderLogs();

            } catch (error) {
                console.error('[Logs] Load failed:', error);
                const container = window.DashboardDOM.safe('#log-entries');
                if (container) {
                    window.DashboardDOM.showError(container, __('index.log.loadFailed'));
                }
            }
        },

        // ===== Render Logs =====
        renderLogs: function() {
            const state = window.DashboardState;
            const entries = state.getFilteredLogs();
            
            // Render filter toolbar
            this.renderFilterToolbar();
            
            const container = window.DashboardDOM.safe('#log-entries');
            const scrollEl = window.DashboardDOM.safe('#log-scroll-container');
            
            if (!container) return;

            if (entries.length === 0) {
                window.DashboardDOM.showEmpty(
                    container, 
                    __('index.log.empty'),
                    'bi bi-journal-x'
                );
            } else {
                this.renderLogEntries(entries, container);
            }

            // Update counts
            this.updateLogCounts(entries);

            // Auto-scroll if polling
            if (state.get('filters.logs.autoRefresh')) {
                requestAnimationFrame(() => {
                    if (scrollEl) scrollEl.scrollTop = 0;
                });
            }
        },

        // ===== Render Filter Toolbar =====
        renderFilterToolbar: function() {
            const container = window.DashboardDOM.safe('#log-filter-container');
            if (!container || container.dataset.initialized) return;

            container.innerHTML = `
                <div class="card-body py-2 border-bottom">
                    <div class="row g-2 align-items-center">
                        <div class="col-auto">
                            <button class="btn btn-sm btn-outline-success" id="log-listen-btn">
                                <i class="bi bi-play-circle me-1"></i>${__('index.log.startListen')}
                            </button>
                        </div>
                        <div class="col-auto">
                            <button class="btn btn-sm btn-outline-secondary" onclick="LogsModule.loadLogs()">
                                <i class="bi bi-arrow-clockwise me-1"></i>${__('index.btn.refresh')}
                            </button>
                        </div>
                        <div class="col-auto">
                            <button class="btn btn-sm btn-outline-danger" onclick="LogsModule.clearLogs()">
                                <i class="bi bi-trash me-1"></i>${__('index.log.clear')}
                            </button>
                        </div>
                        <div class="col-auto">
                            <select class="form-select form-select-sm" id="log-count-select" style="width:auto;">
                                <option value="50">50 ${__('index.log.entries')}</option>
                                <option value="100" selected>100 ${__('index.log.entries')}</option>
                                <option value="200">200 ${__('index.log.entries')}</option>
                                <option value="500">500 ${__('index.log.entries')}</option>
                            </select>
                        </div>
                        <div class="col">
                            <input type="text" class="form-control form-control-sm" id="log-search-input" 
                                   placeholder="${__('index.log.search')}...">
                        </div>
                        <div class="col-auto">
                            <select class="form-select form-select-sm" id="log-status-select" style="width:auto;">
                                <option value="all">${__('index.log.status.all')}</option>
                                <option value="success">${__('index.log.status.success')}</option>
                                <option value="error">${__('index.log.status.error')}</option>
                            </select>
                        </div>
                    </div>
                </div>
            `;

            container.dataset.initialized = 'true';
            this.initFilterHandlers();
        },

        // ===== Initialize Filter Handlers =====
        initFilterHandlers: function() {
            // Listen button
            const listenBtn = window.DashboardDOM.safe('#log-listen-btn');
            if (listenBtn) {
                listenBtn.addEventListener('click', () => this.togglePolling());
            }

            // Count select
            const countSelect = window.DashboardDOM.safe('#log-count-select');
            if (countSelect) {
                countSelect.addEventListener('change', (e) => {
                    window.DashboardState.set('filters.logs.maxCount', parseInt(e.target.value));
                    this.loadLogs();
                });
            }

            // Search input (debounced)
            const searchInput = window.DashboardDOM.safe('#log-search-input');
            if (searchInput) {
                searchInput.addEventListener('input', window.DashboardUtils.debounce((e) => {
                    window.DashboardState.set('filters.logs.search', e.target.value);
                    this.renderLogs();
                }, 300));
            }

            // Status select
            const statusSelect = window.DashboardDOM.safe('#log-status-select');
            if (statusSelect) {
                statusSelect.addEventListener('change', (e) => {
                    window.DashboardState.set('filters.logs.status', e.target.value);
                    this.renderLogs();
                });
            }

            // Restore filter values from state
            this.restoreFilterValues();
        },

        // ===== Restore Filter Values =====
        restoreFilterValues: function() {
            const state = window.DashboardState;
            
            const countSelect = window.DashboardDOM.safe('#log-count-select');
            if (countSelect) {
                countSelect.value = state.get('filters.logs.maxCount') || 100;
            }

            const searchInput = window.DashboardDOM.safe('#log-search-input');
            if (searchInput) {
                searchInput.value = state.get('filters.logs.search') || '';
            }

            const statusSelect = window.DashboardDOM.safe('#log-status-select');
            if (statusSelect) {
                statusSelect.value = state.get('filters.logs.status') || 'all';
            }

            const listenBtn = window.DashboardDOM.safe('#log-listen-btn');
            if (listenBtn && state.get('filters.logs.autoRefresh')) {
                this.updateListenButton(true);
            }
        },

        // ===== Render Log Entries =====
        renderLogEntries: function(entries, container) {
            window.DashboardDOM.clear(container);

            const fragment = document.createDocumentFragment();
            const outerDiv = window.DashboardDOM.create('div', {
                style: {
                    fontFamily: 'Consolas, monospace',
                    fontSize: '13px',
                    lineHeight: '1.6'
                }
            });

            entries.forEach((entry, index) => {
                const logKey = `${entry.timestamp}|${entry.level}|${(entry.message || '').substring(0, 80)}`;
                const isExpanded = window.DashboardState.get(`ui.expandedLogs.${logKey}`) || false;
                const item = this.createLogItem(entry, logKey, isExpanded);
                outerDiv.appendChild(item);
            });

            fragment.appendChild(outerDiv);
            container.appendChild(fragment);
        },

        // ===== Create Log Item =====
        createLogItem: function(entry, logKey, isExpanded) {
            const item = window.DashboardDOM.create('div', {
                className: 'log-item'
            });

            // Clickable row
            const row = window.DashboardDOM.create('div', {
                className: 'log-row',
                style: {
                    display: 'flex',
                    gap: '8px',
                    padding: '4px 16px',
                    borderBottom: '1px solid #f1f5f9',
                    cursor: 'pointer',
                    alignItems: 'center'
                },
                events: {
                    mouseenter: function() { this.style.background = '#f8fafc'; },
                    mouseleave: function() { this.style.background = ''; },
                    click: () => this.toggleLogEntry(logKey)
                }
            });

            // Time
            const timeSpan = window.DashboardDOM.create('span', {
                textContent: window.DashboardI18n.formatTime(new Date(entry.timestamp)),
                style: {
                    color: '#94a3b8',
                    whiteSpace: 'nowrap',
                    minWidth: '80px'
                }
            });

            // Level badge
            const badge = this.createLevelBadge(entry.level);

            // Message
            const msgSpan = window.DashboardDOM.create('span', {
                textContent: entry.message || '',
                style: {
                    color: '#475569',
                    flex: '1',
                    wordBreak: 'break-all',
                    whiteSpace: 'nowrap',
                    overflow: 'hidden',
                    textOverflow: 'ellipsis'
                }
            });

            // Arrow
            const arrowSpan = window.DashboardDOM.create('span', {
                className: 'log-arrow',
                textContent: isExpanded ? '▼' : '▶',
                style: {
                    color: '#94a3b8',
                    fontSize: '10px',
                    whiteSpace: 'nowrap',
                    minWidth: '14px',
                    textAlign: 'center',
                    transition: 'transform .2s'
                }
            });

            row.appendChild(timeSpan);
            row.appendChild(badge);
            row.appendChild(msgSpan);
            row.appendChild(arrowSpan);

            // Detail section
            const detail = this.createLogDetail(entry, isExpanded);

            item.appendChild(row);
            item.appendChild(detail);

            return item;
        },

        // ===== Create Level Badge =====
        createLevelBadge: function(level) {
            const levelMap = {
                'Information': { class: 'bg-info', icon: 'ℹ️' },
                'Warning': { class: 'bg-warning', icon: '⚠️' },
                'Error': { class: 'bg-danger', icon: '❌' },
                'Critical': { class: 'bg-dark', icon: '🔥' },
                'Debug': { class: 'bg-secondary', icon: '🐛' }
            };

            const config = levelMap[level] || { class: 'bg-light', icon: '•' };
            
            return window.DashboardDOM.create('span', {
                className: `badge ${config.class} text-dark`,
                textContent: `${config.icon} ${level}`,
                style: { fontSize: '11px' }
            });
        },

        // ===== Create Log Detail =====
        createLogDetail: function(entry, isExpanded) {
            const detail = window.DashboardDOM.create('div', {
                className: 'log-detail',
                style: {
                    display: isExpanded ? 'block' : 'none',
                    padding: '8px 16px 12px',
                    background: '#f8fafc',
                    borderBottom: '1px solid #e2e8f0',
                    fontSize: '12px'
                }
            });

            const dtHtml = [];

            // Metadata row
            dtHtml.push('<div style="display:flex;flex-wrap:wrap;gap:8px 24px;margin-bottom:6px;">');
            
            if (entry.category) {
                dtHtml.push(`<span><strong>${__('index.log.category')}</strong> ${window.DashboardUtils.escapeHtml(entry.category)}</span>`);
            }
            if (entry.routeId) {
                dtHtml.push(`<span><strong>RouteId:</strong> <code>${window.DashboardUtils.escapeHtml(entry.routeId)}</code></span>`);
            }
            if (entry.traceId) {
                dtHtml.push(`<span><strong>TraceId:</strong> <code>${window.DashboardUtils.escapeHtml(entry.traceId)}</code></span>`);
            }
            if (entry.statusCode) {
                dtHtml.push(`<span><strong>Status:</strong> <span class="badge ${this.getStatusCodeBadge(entry.statusCode)}">${entry.statusCode}</span></span>`);
            }
            
            dtHtml.push('</div>');

            // Message
            dtHtml.push(`<div style="color:#334155;margin-bottom:4px;"><strong>${__('index.log.message')}</strong><br>`);
            dtHtml.push(`<span style="color:#475569;word-break:break-all;">${window.DashboardUtils.escapeHtml(entry.message || '')}</span></div>`);

            // Details (JSON)
            if (entry.details) {
                dtHtml.push(`<div style="color:#334155;margin-top:6px;"><strong>${__('index.log.details')}</strong></div>`);
                try {
                    const detailsObj = JSON.parse(entry.details);
                    dtHtml.push(this.renderJsonBlock(detailsObj, 'Details JSON'));
                } catch (err) {
                    dtHtml.push(`<pre style="background:#f1f5f9;border:1px solid #cbd5e1;border-radius:4px;padding:8px;margin:4px 0 0;overflow-x:auto;white-space:pre-wrap;word-break:break-all;font-size:12px;color:#334155;line-height:1.6;">${window.DashboardUtils.escapeHtml(entry.details)}</pre>`);
                }
            }

            // Exception
            if (entry.exception) {
                dtHtml.push(`<div style="color:#dc2626;margin-top:6px;"><strong>${__('index.log.exception')}</strong></div>`);
                dtHtml.push(`<pre style="background:#fef2f2;border:1px solid #fecaca;border-radius:4px;padding:8px;margin:4px 0 0;overflow-x:auto;white-space:pre-wrap;word-break:break-all;font-size:11px;color:#991b1b;line-height:1.5;">${window.DashboardUtils.escapeHtml(entry.exception)}</pre>`);
            }

            detail.innerHTML = dtHtml.join('');

            return detail;
        },

        // ===== Render JSON Block =====
        renderJsonBlock: function(obj, title) {
            const json = JSON.stringify(obj, null, 2);
            return `<details style="margin:4px 0 0;">
                <summary style="cursor:pointer;color:#0ea5e9;font-weight:500;">${title}</summary>
                <pre style="background:#f1f5f9;border:1px solid #cbd5e1;border-radius:4px;padding:8px;margin:4px 0 0;overflow-x:auto;white-space:pre-wrap;word-break:break-all;font-size:12px;color:#334155;line-height:1.6;">${window.DashboardUtils.escapeHtml(json)}</pre>
            </details>`;
        },

        // ===== Render Truncated Body (for large content) =====
        renderTruncatedBody: function(body, maxLength) {
            maxLength = maxLength || 500;
            
            if (!body) return '<span class="text-muted">No body</span>';
            
            if (body.length > maxLength) {
                var truncated = body.substring(0, maxLength);
                var escapedTruncated = window.DashboardUtils.escapeHtml(truncated);
                var escapedFull = window.DashboardUtils.escapeHtml(body);
                
                return '<div class="log-body-truncated">' +
                    '<pre style="max-height: 100px; overflow: hidden; position: relative;">' +
                    escapedTruncated + '...' +
                    '<button class="btn btn-sm btn-link show-full-btn" ' +
                    'onclick="this.parentElement.innerHTML=\'<pre style=\\\'white-space:pre-wrap;word-break:break-all;\\\'>"  + escapedFull + "</pre>\'">' +
                    'Show full' +
                    '</button>' +
                    '</pre>' +
                    '</div>';
            }
            
            return '<pre>' + window.DashboardUtils.escapeHtml(body) + '</pre>';
        },

        // ===== Get Status Code Badge Class =====
        getStatusCodeBadge: function(statusCode) {
            if (statusCode >= 200 && statusCode < 300) return 'bg-success';
            if (statusCode >= 300 && statusCode < 400) return 'bg-info';
            if (statusCode >= 400 && statusCode < 500) return 'bg-warning';
            if (statusCode >= 500) return 'bg-danger';
            return 'bg-secondary';
        },

        // ===== Toggle Log Entry =====
        toggleLogEntry: function(logKey) {
            const state = window.DashboardState;
            const current = state.get(`ui.expandedLogs.${logKey}`) || false;
            state.set(`ui.expandedLogs.${logKey}`, !current);
            this.renderLogs();
        },

        // ===== Update Log Counts =====
        updateLogCounts: function(entries) {
            const allLogs = window.DashboardState.get('data.logs') || [];
            
            const displayEl = window.DashboardDOM.safe('#log-display-count');
            if (displayEl) displayEl.textContent = entries.length;

            const totalEl = window.DashboardDOM.safe('#log-total-count');
            if (totalEl) totalEl.textContent = allLogs.length;

            const timeEl = window.DashboardDOM.safe('#log-refresh-time');
            if (timeEl) timeEl.textContent = __('index.log.updated') + window.DashboardI18n.formatTime(new Date());
        },

        // ===== Polling Control =====
        togglePolling: function() {
            const state = window.DashboardState;
            const isPolling = state.get('filters.logs.autoRefresh');
            
            if (isPolling) {
                this.stopPolling();
            } else {
                this.startPolling();
            }
        },

        startPolling: function() {
            const state = window.DashboardState;
            
            if (this.pollTimer) {
                clearInterval(this.pollTimer);
            }

            state.set('filters.logs.autoRefresh', true);
            this.updateListenButton(true);
            this.loadLogs();

            const interval = state.get('filters.logs.refreshInterval') || 5000;
            this.pollTimer = setInterval(() => this.loadLogs(), interval);
        },

        stopPolling: function() {
            const state = window.DashboardState;
            
            if (this.pollTimer) {
                clearInterval(this.pollTimer);
                this.pollTimer = null;
            }

            state.set('filters.logs.autoRefresh', false);
            this.updateListenButton(false);
        },

        // ===== Update Listen Button =====
        updateListenButton: function(isPolling) {
            const btn = window.DashboardDOM.safe('#log-listen-btn');
            if (!btn) return;

            if (isPolling) {
                btn.className = 'btn btn-sm btn-outline-warning';
                btn.innerHTML = `<i class="bi bi-pause-circle me-1"></i>${__('index.log.stopListen')}`;
            } else {
                btn.className = 'btn btn-sm btn-outline-success';
                btn.innerHTML = `<i class="bi bi-play-circle me-1"></i>${__('index.log.startListen')}`;
            }
        },

        // ===== Clear Logs =====
        clearLogs: async function() {
            if (!confirm(__('index.log.clearConfirm'))) return;

            try {
                await window.DashboardApi.endpoints.clearLogs();
                window.DashboardState.set('data.logs', []);
                this.renderLogs();
                
                // Show success message
                if (window.DashboardModals) {
                    window.DashboardModals.showSuccess(__('index.log.cleared'));
                }
            } catch (error) {
                console.error('[Logs] Clear failed:', error);
                if (window.DashboardModals) {
                    window.DashboardModals.showError(__('index.log.clearFailed'));
                }
            }
        },

        // ===== Setup Events =====
        setupEvents: function() {
            // Refresh shortcut
            document.addEventListener('dashboard:shortcut:refresh', async () => {
                await this.loadLogs();
            });

            // Locale change
            document.addEventListener('dashboard:localeChange', () => {
                this.renderLogs();
            });
        }
    };

    // Register module
    if (window.DashboardApp) {
        window.DashboardApp.registerModule('logs', LogsModule);
    }

    // Expose to window for backward compatibility
    window.LogsModule = LogsModule;
    window.loadLogs = LogsModule.loadLogs.bind(LogsModule);
    window.toggleListening = LogsModule.togglePolling.bind(LogsModule);
    window.clearLogs = LogsModule.clearLogs.bind(LogsModule);

})();
