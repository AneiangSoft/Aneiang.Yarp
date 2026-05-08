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
            if (!container) return;

            // Only build toolbar once; subsequent calls just restore state
            if (container.querySelector('#log-search-input')) {
                this.restoreFilterValues();
                return;
            }

            // Layout: Search(left) | Filters(middle) | Action buttons(right)
            container.innerHTML = `
                <div class="card-body py-2 border-bottom">
                    <div class="row g-2 align-items-center">
                        <div class="col">
                            <div class="input-group input-group-sm">
                                <span class="input-group-text bg-light border-end-0">
                                    <i class="bi bi-search text-muted"></i>
                                </span>
                                <input type="text" class="form-control border-start-0" id="log-search-input" 
                                       placeholder="${__('index.log.search')}...">
                            </div>
                        </div>
                        <div class="col-auto">
                            <select class="form-select form-select-sm" id="log-count-select" style="width:75px;">
                                <option value="50">50</option>
                                <option value="100" selected>100</option>
                                <option value="200">200</option>
                                <option value="500">500</option>
                            </select>
                        </div>
                        <div class="col-auto">
                            <select class="form-select form-select-sm" id="log-status-select" style="width:100px;">
                                <option value="all">${__('index.log.status.all')}</option>
                                <option value="success">${__('index.log.status.success')}</option>
                                <option value="error">${__('index.log.status.error')}</option>
                            </select>
                        </div>
                        <div class="col-auto">
                            <div class="btn-group" role="group">
                                <button class="btn btn-sm btn-outline-success" id="log-listen-btn" title="${__('index.log.startListen')}">
                                    <i class="bi bi-play-circle"></i>
                                </button>
                                <button class="btn btn-sm btn-outline-secondary" onclick="LogsModule.loadLogs()" title="${__('index.btn.refresh')}">
                                    <i class="bi bi-arrow-clockwise"></i>
                                </button>
                                <button class="btn btn-sm btn-outline-danger" onclick="LogsModule.clearLogs()" title="${__('index.log.clear')}">
                                    <i class="bi bi-trash"></i>
                                </button>
                            </div>
                        </div>
                    </div>
                </div>
            `;

            // Initialize handlers only once
            this.initFilterHandlers();
        },

        // ===== Initialize Filter Handlers =====
        initFilterHandlers: function() {
            // Listen button - use onclick attribute to avoid duplicate event listeners
            const listenBtn = window.DashboardDOM.safe('#log-listen-btn');
            if (listenBtn) {
                listenBtn.onclick = () => this.togglePolling();
            }

            // Count select
            const countSelect = window.DashboardDOM.safe('#log-count-select');
            if (countSelect) {
                countSelect.onchange = (e) => {
                    window.DashboardState.set('filters.logs.maxCount', parseInt(e.target.value));
                    this.loadLogs();
                };
            }

            // Search input (debounced)
            const searchInput = window.DashboardDOM.safe('#log-search-input');
            if (searchInput) {
                searchInput.oninput = window.DashboardUtils.debounce((e) => {
                    window.DashboardState.set('filters.logs.search', e.target.value);
                    this.renderLogs();
                }, 300);
            }

            // Status select
            const statusSelect = window.DashboardDOM.safe('#log-status-select');
            if (statusSelect) {
                statusSelect.onchange = (e) => {
                    window.DashboardState.set('filters.logs.status', e.target.value);
                    this.renderLogs();
                };
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
            container.classList.add('log-entries-container');

            const fragment = document.createDocumentFragment();

            entries.forEach((entry, index) => {
                const logKey = `${entry.timestamp}|${entry.level}|${(entry.message || '').substring(0, 80)}`;
                const isExpanded = window.DashboardState.get(`ui.expandedLogs.${logKey}`) || false;
                const item = this.createLogItem(entry, logKey, isExpanded);
                fragment.appendChild(item);
            });

            container.appendChild(fragment);
        },

        // ===== Create Log Item =====
        createLogItem: function(entry, logKey, isExpanded) {
            // Level class mapping for CSS color bar
            const levelClassMap = {
                'Information': 'level-info',
                'Warning': 'level-warning',
                'Error': 'level-error',
                'Critical': 'level-critical',
                'Debug': 'level-debug'
            };
            const levelClass = levelClassMap[entry.level] || 'level-info';

            const item = window.DashboardDOM.create('div', {
                className: `log-item ${levelClass}`,
                attributes: { 'data-log-key': logKey }
            });

            // Clickable row - simplified: only show time, level, and message
            const row = window.DashboardDOM.create('div', {
                className: 'log-row',
                events: {
                    click: (e) => this.toggleLogEntryDirect(logKey, e)
                }
            });

            // Time
            const timeSpan = window.DashboardDOM.create('span', {
                textContent: window.DashboardI18n.formatTime(new Date(entry.timestamp)),
                style: {
                    color: '#64748b',
                    whiteSpace: 'nowrap',
                    minWidth: '70px',
                    fontSize: '12px'
                }
            });

            // Level badge (more prominent)
            const badge = this.createLevelBadge(entry.level);

            // Message (main content)
            const msgSpan = window.DashboardDOM.create('span', {
                textContent: entry.message || '',
                style: {
                    color: '#1e293b',
                    flex: '1',
                    overflow: 'hidden',
                    textOverflow: 'ellipsis',
                    whiteSpace: 'nowrap',
                    fontWeight: '500'
                }
            });

            // Copy button (small icon button)
            const copyBtn = window.DashboardDOM.create('button', {
                className: 'btn btn-sm btn-link p-0',
                innerHTML: '<i class="bi bi-clipboard" style="color:#94a3b8"></i>',
                style: { marginLeft: '8px', opacity: '0.6', transition: 'all 0.2s ease' },
                events: {
                    click: (e) => {
                        e.stopPropagation();
                        this.copyLogEntry(entry, copyBtn);
                    }
                }
            });

            // Arrow indicator (uses CSS rotation)
            const arrowSpan = window.DashboardDOM.create('i', {
                className: `bi bi-chevron-right log-arrow ${isExpanded ? 'expanded' : ''}`,
                style: {
                    color: '#94a3b8',
                    fontSize: '12px',
                    transition: 'transform 0.2s ease'
                }
            });

            row.appendChild(timeSpan);
            row.appendChild(badge);
            row.appendChild(msgSpan);
            row.appendChild(copyBtn);
            row.appendChild(arrowSpan);

            // Detail section - hidden by default, uses CSS class for expanded state
            const detail = this.createLogDetail(entry, isExpanded);

            item.appendChild(row);
            item.appendChild(detail);

            return item;
        },

        // ===== Create Level Badge =====
        createLevelBadge: function(level) {
            const levelMap = {
                'Information': { css: 'info', icon: 'bi-info-circle-fill', text: 'INFO' },
                'Warning': { css: 'warning', icon: 'bi-exclamation-triangle-fill', text: 'WARN' },
                'Error': { css: 'error', icon: 'bi-x-circle-fill', text: 'ERROR' },
                'Critical': { css: 'critical', icon: 'bi-fire', text: 'FATAL' },
                'Debug': { css: 'debug', icon: 'bi-bug-fill', text: 'DEBUG' }
            };
            
            const config = levelMap[level] || { css: 'info', icon: 'bi-circle-fill', text: level };
            
            const badge = window.DashboardDOM.create('span', {
                className: `log-level-badge ${config.css}`
            });
            
            const icon = window.DashboardDOM.create('i', {
                className: `bi ${config.icon}`
            });
            badge.appendChild(icon);
            
            const text = window.DashboardDOM.create('span', {
                textContent: config.text
            });
            badge.appendChild(text);
            
            return badge;
        },

        // ===== Create Log Detail =====
        createLogDetail: function(entry, isExpanded) {
            const detail = window.DashboardDOM.create('div', {
                className: `log-detail ${isExpanded ? 'expanded' : ''}`
            });
                
            const dtHtml = [];
                
            // HTTP Request Info (if exists)
            if (entry.method || entry.statusCode || entry.path) {
                dtHtml.push('<div class="d-flex flex-wrap gap-2 mb-2 pb-2 border-bottom">');
                        
                // Method with colored badge (consistent with routes module)
                if (entry.method) {
                    const methodColors = {
                        'GET': 'bg-success',
                        'POST': 'bg-primary',
                        'PUT': 'bg-info',
                        'DELETE': 'bg-danger',
                        'PATCH': 'bg-warning text-dark'
                    }; 
                    const methodClass = methodColors[entry.method] || 'bg-secondary';
                    dtHtml.push(`<span><strong>Method:</strong> <span class="badge ${methodClass}">${entry.method}</span></span>`);
                }
                        
                // Status code
                if (entry.statusCode) {
                    dtHtml.push(`<span><strong>Status:</strong> <span class="badge ${this.getStatusCodeBadge(entry.statusCode)}">${entry.statusCode}</span></span>`);
                }
                        
                // Path
                if (entry.path) {
                    dtHtml.push(`<span style="flex:1"><strong>Path:</strong> <code>${window.DashboardUtils.escapeHtml(entry.path)}</code></span>`);
                }
                        
                dtHtml.push('</div>');
            }
                
            // Metadata row
            dtHtml.push('<div class="d-flex flex-wrap gap-2 mb-2">');
                            
            if (entry.category) {
                dtHtml.push(`<span><strong>${__('index.log.category')}</strong> ${window.DashboardUtils.escapeHtml(entry.category)}</span>`);
            }
            if (entry.routeId) {
                dtHtml.push(`<span><strong>RouteId:</strong> <code>${window.DashboardUtils.escapeHtml(entry.routeId)}</code></span>`);
            }
            if (entry.traceId) {
                dtHtml.push(`<span><strong>TraceId:</strong> <code>${window.DashboardUtils.escapeHtml(entry.traceId)}</code></span>`);
            }
                            
            dtHtml.push('</div>');
                
            // Message (full content)
            dtHtml.push(`<div class="mb-2"><strong>${__('index.log.message')}</strong><br>`);
            dtHtml.push(`<span style="color:#475569;word-break:break-all;">${window.DashboardUtils.escapeHtml(entry.message || '')}</span></div>`);
                
            // Details (JSON)
            if (entry.details) {
                dtHtml.push(`<div class="mt-2"><strong>${__('index.log.details')}</strong></div>`);
                try {
                    const detailsObj = JSON.parse(entry.details);
                    dtHtml.push(this.renderJsonBlock(detailsObj, 'Details JSON')); 
                } catch (err) {
                    dtHtml.push(`<pre style="background:#f1f5f9;border:1px solid var(--border-color);border-radius:4px;padding:8px;margin:4px 0 0;overflow-x:auto;white-space:pre-wrap;word-break:break-all;font-size:12px;color:#334155;">${window.DashboardUtils.escapeHtml(entry.details)}</pre>`);
                }
            }
                
            // Exception
            if (entry.exception) {
                dtHtml.push(`<div style="color:#dc2626;margin-top:6px"><strong>${__('index.log.exception')}</strong></div>`);
                dtHtml.push(`<pre style="background:#fef2f2;border:1px solid #fecaca;border-radius:4px;padding:8px;margin:4px 0 0;overflow-x:auto;white-space:pre-wrap;word-break:break-all;font-size:11px;color:#991b1b;">${window.DashboardUtils.escapeHtml(entry.exception)}</pre>`);
            }
                
            detail.innerHTML = dtHtml.join('');
        
            return detail;
        },

        // ===== Render JSON Block (delegates to shared utility) =====
        renderJsonBlock: function(obj, title) {
            return window.DashboardUtils.renderJsonBlock(obj, title);
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
        // ===== Toggle Log Entry (Direct DOM Manipulation - More Efficient) =====
        toggleLogEntryDirect: function(logKey, event) {
            const state = window.DashboardState;
            const current = state.get(`ui.expandedLogs.${logKey}`) || false;
            
            // If expanding and polling is active, stop polling
            if (!current && state.get('filters.logs.autoRefresh')) {
                this.stopPolling();
                console.log('[Logs] Auto-stopped polling due to log expansion');
            }
            
            // Update state
            state.set(`ui.expandedLogs.${logKey}`, !current);
            
            // Direct DOM manipulation - find the log item by data-log-key
            const logItem = document.querySelector(`.log-item[data-log-key="${CSS.escape(logKey)}"]`);
            if (logItem) {
                const arrow = logItem.querySelector('.log-arrow');
                const detail = logItem.querySelector('.log-detail');
                
                if (!current) {
                    // Expanding
                    if (arrow) arrow.classList.add('expanded');
                    if (detail) detail.classList.add('expanded');
                } else {
                    // Collapsing
                    if (arrow) arrow.classList.remove('expanded');
                    if (detail) detail.classList.remove('expanded');
                }
            }
        },

        // ===== Copy Log Entry =====
        copyLogEntry: function(entry, btnElement) {
            const text = JSON.stringify(entry, null, 2);
            navigator.clipboard.writeText(text).then(() => {
                // Visual feedback - same pattern as clusters/routes copy button
                if (btnElement) {
                    const icon = btnElement.querySelector('i');
                    if (icon) icon.className = 'bi bi-check2';
                    btnElement.style.opacity = '1';
                    btnElement.style.color = '#22c55e';
                    setTimeout(() => {
                        if (icon) icon.className = 'bi bi-clipboard';
                        btnElement.style.opacity = '0.6';
                        btnElement.style.color = '#94a3b8';
                    }, 1500);
                }
                if (window.DashboardModals) {
                    window.DashboardModals.showSuccess(__('index.copied') || '已复制');
                }
            }).catch(err => {
                console.error('[Logs] Failed to copy:', err);
                if (window.DashboardModals) {
                    window.DashboardModals.showError(__('index.copyFailed') || '复制失败');
                }
            });
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
                btn.className = 'btn btn-sm btn-outline-warning active';
                btn.innerHTML = '<i class="bi bi-pause-circle"></i>';
                btn.title = __('index.log.stopListen');
            } else {
                btn.className = 'btn btn-sm btn-outline-success';
                btn.innerHTML = '<i class="bi bi-play-circle"></i>';
                btn.title = __('index.log.startListen');
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
