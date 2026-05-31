/**
 * Dashboard Logs Module - Log viewer with filtering, polling and virtual scrolling
 * Optimized with virtual scrolling for handling large log volumes
 */
(function() {
    'use strict';

    const LogsModule = {
        name: 'logs',
        initialized: false,
        pollTimer: null,
        wasPollingBeforeHidden: false,

        // SSE streaming state
        sseConnection: null,
        sseEnabled: true, // Enable SSE by default, fallback to polling
        pendingLogEntries: [], // Buffer for entries received during rendering
        isRendering: false,

        // Virtual scrolling state
        virtualScroll: {
            enabled: true,
            itemHeight: 48, // Estimated row height in pixels
            overscan: 5, // Number of extra items to render outside viewport
            containerHeight: 0, // Will be measured
            visibleCount: 0, // Calculated based on container height
            startIndex: 0, // First visible item index
            endIndex: 0, // Last visible item index
            scrollTop: 0, // Current scroll position
            totalHeight: 0, // Total scrollable height
            lastEntriesLength: 0 // Track for resize recalculation
        },

        // DOM element pool for virtual scrolling
        domPool: [],
        poolSize: 50, // Maximum pooled elements

        // ===== Initialization =====
        init: async function() {
            if (this.initialized) return;

            console.log('[Logs] Initializing with virtual scrolling...');

            try {
                // Initialize virtual scrolling measurements
                this.initVirtualScroll();

                // Load initial logs
                await this.loadLogs();

                // Setup event listeners
                this.setupEvents();

                this.initialized = true;
                console.log('[Logs] Initialized with virtual scrolling');
            } catch (error) {
                console.error('[Logs] Init failed:', error);
                throw error;
            }
        },

        // ===== Virtual Scrolling Setup =====
        initVirtualScroll: function() {
            const scrollEl = window.DashboardDOM.safe('#log-scroll-container');
            const container = window.DashboardDOM.safe('#log-entries');
            if (!scrollEl || !container) return;

            // Measure container height
            this.virtualScroll.containerHeight = scrollEl.clientHeight;
            this.virtualScroll.visibleCount = Math.ceil(
                this.virtualScroll.containerHeight / this.virtualScroll.itemHeight
            ) + this.virtualScroll.overscan * 2;

            // Setup scroll handler with RAF throttling
            let ticking = false;
            scrollEl.addEventListener('scroll', () => {
                this.virtualScroll.scrollTop = scrollEl.scrollTop;
                if (!ticking) {
                    requestAnimationFrame(() => {
                        this.updateVisibleRange();
                        ticking = false;
                    });
                    ticking = true;
                }
            }, { passive: true });

            // Handle window resize
            window.addEventListener('resize', this.debounce(() => {
                this.virtualScroll.containerHeight = scrollEl.clientHeight;
                this.virtualScroll.visibleCount = Math.ceil(
                    this.virtualScroll.containerHeight / this.virtualScroll.itemHeight
                ) + this.virtualScroll.overscan * 2;
                this.updateVisibleRange();
            }, 150));
        },

        // ===== Debounce Helper =====
        debounce: function(fn, delay) {
            let timeout;
            return function(...args) {
                clearTimeout(timeout);
                timeout = setTimeout(() => fn.apply(this, args), delay);
            };
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
                state.set('data.logMeta', { evictedCount: result.evictedCount || 0, bufferSize: result.bufferSize || 0, bufferCapacity: result.bufferCapacity || 0 });

                // Render logs with virtual scrolling
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
            this.isRendering = true;

            const entries = state.getFilteredLogs ? state.getFilteredLogs() : (state.get('data.logs') || []);

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
                // Reset virtual scroll state
                this.virtualScroll.totalHeight = 0;
                this.virtualScroll.lastEntriesLength = 0;
            } else {
                // Check if we should use virtual scrolling
                if (entries.length > 100 && this.virtualScroll.enabled) {
                    this.renderVirtualLogEntries(entries, container);
                } else {
                    // Fall back to regular rendering for small lists
                    this.renderLogEntries(entries, container);
                }
            }

            // Update counts
            this.updateLogCounts(entries);

            // Auto-scroll if polling (only if at top)
            if (state.get('filters.logs.autoRefresh') && scrollEl && scrollEl.scrollTop < 50) {
                requestAnimationFrame(() => {
                    scrollEl.scrollTop = 0;
                    this.virtualScroll.scrollTop = 0;
                    this.updateVisibleRange();
                });
            }

            // Mark rendering complete and flush any pending SSE entries
            this.isRendering = false;
            this.flushPendingEntries();
        },

        // ===== Virtual Scrolling Render =====
        renderVirtualLogEntries: function(entries, container) {
            // Calculate total height
            this.virtualScroll.totalHeight = entries.length * this.virtualScroll.itemHeight;
            this.virtualScroll.lastEntriesLength = entries.length;

            // Pre-group entries by traceId for O(n) pairing
            const traceIdMap = new Map();
            const processed = new Set();

            entries.forEach((entry, index) => {
                if (entry.traceId && entry.eventType !== 'YarpEvent') {
                    if (!traceIdMap.has(entry.traceId)) {
                        traceIdMap.set(entry.traceId, []);
                    }
                    traceIdMap.get(entry.traceId).push({ entry, index });
                }
            });

            // Create wrapper with proper height for scrolling
            container.innerHTML = `
                <div class="virtual-scroll-wrapper" style="position: relative; height: ${this.virtualScroll.totalHeight}px;">
                    <div class="virtual-scroll-content" style="position: absolute; top: 0; left: 0; right: 0;"></div>
                </div>
            `;

            const contentEl = container.querySelector('.virtual-scroll-content');

            // Store references for updates
            this.virtualScroll.entries = entries;
            this.virtualScroll.traceIdMap = traceIdMap;
            this.virtualScroll.processed = processed;
            this.virtualScroll.contentEl = contentEl;

            // Initial render of visible range
            this.updateVisibleRange();
        },

        // ===== Update Visible Range (Virtual Scrolling) =====
        updateVisibleRange: function() {
            if (!this.virtualScroll.contentEl || !this.virtualScroll.entries) return;

            const entries = this.virtualScroll.entries;
            const scrollTop = this.virtualScroll.scrollTop;
            const itemHeight = this.virtualScroll.itemHeight;
            const overscan = this.virtualScroll.overscan;
            const containerHeight = this.virtualScroll.containerHeight || 600;

            // Calculate visible range
            const startIndex = Math.max(0, Math.floor(scrollTop / itemHeight) - overscan);
            const visibleCount = Math.ceil(containerHeight / itemHeight) + overscan * 2;
            const endIndex = Math.min(entries.length, startIndex + visibleCount);

            // Only update if range changed significantly
            if (Math.abs(startIndex - this.virtualScroll.startIndex) < overscan / 2 &&
                Math.abs(endIndex - this.virtualScroll.endIndex) < overscan / 2) {
                return;
            }

            this.virtualScroll.startIndex = startIndex;
            this.virtualScroll.endIndex = endIndex;

            // Update content offset
            const offsetTop = startIndex * itemHeight;
            this.virtualScroll.contentEl.style.transform = `translateY(${offsetTop}px)`;

            // Render visible items
            this.renderVisibleItems(startIndex, endIndex, entries);
        },

        // ===== Render Visible Items =====
        renderVisibleItems: function(startIndex, endIndex, entries) {
            const contentEl = this.virtualScroll.contentEl;
            const traceIdMap = this.virtualScroll.traceIdMap;
            const processed = new Set();

            // Create document fragment for batch DOM update
            const fragment = document.createDocumentFragment();

            for (let i = startIndex; i < endIndex && i < entries.length; i++) {
                if (processed.has(i)) continue;

                const entry = entries[i];
                const item = this.createVirtualLogItem(entry, i, entries, traceIdMap, processed);
                if (item) {
                    fragment.appendChild(item);
                }
            }

            // Batch DOM update
            contentEl.innerHTML = '';
            contentEl.appendChild(fragment);
        },

        // ===== Create Virtual Log Item =====
        createVirtualLogItem: function(entry, index, entries, traceIdMap, processed) {
            // Try to find paired entry
            if (entry.traceId && entry.eventType !== 'YarpEvent') {
                const group = traceIdMap.get(entry.traceId);
                if (group && group.length > 1) {
                    const pairIndex = group.findIndex(x => x.index === index);
                    const pairInfo = group.find((x, i) => i !== pairIndex && !processed.has(x.index));

                    if (pairInfo && !processed.has(pairInfo.index)) {
                        const pairedEntry = pairInfo.entry;
                        const requestEntry = entry.eventType === 'ProxyRequest' ? entry : pairedEntry;
                        const responseEntry = entry.eventType === 'ProxyResponse' ? entry : pairedEntry;

                        processed.add(index);
                        processed.add(pairInfo.index);

                        const logKey = `paired:${requestEntry.timestamp}|${responseEntry.timestamp}|${requestEntry.traceId}`;
                        const isExpanded = window.DashboardState.get(`ui.expandedLogs.${logKey}`) || false;
                        return this.createPairedLogItem(requestEntry, responseEntry, logKey, isExpanded);
                    }
                }
            }

            // Single entry
            if (!processed.has(index)) {
                processed.add(index);
                const logKey = `${entry.timestamp}|${entry.level}|${(entry.message || '').substring(0, 80)}`;
                const isExpanded = window.DashboardState.get(`ui.expandedLogs.${logKey}`) || false;
                return this.createLogItem(entry, logKey, isExpanded);
            }
            return null;
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
                            <div class="btn-group btn-group-sm" role="group">
                                <button type="button" class="btn btn-outline-primary" id="log-type-all-btn" title="${__('index.log.type.all')}">${__('index.log.type.all')}</button>
                                <button type="button" class="btn btn-outline-primary" id="log-type-gateway-btn" title="${__('index.log.gatewayOnly')}"><i class="bi bi-globe"></i> ${__('index.log.gatewayOnly')}</button>
                            </div>
                        </div>
                        <div class="col-auto">
                            <select class="form-select form-select-sm" id="log-count-select" style="width:75px;">
                                <option value="50">50</option>
                                <option value="100" selected>100</option>
                                <option value="200">200</option>
                                <option value="500">500</option>
                                <option value="1000">1K</option>
                                <option value="2000">2K</option>
                                <option value="5000">5K</option>
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

            // Gateway-only toggle buttons
            const allBtn = window.DashboardDOM.safe('#log-type-all-btn');
            const gatewayBtn = window.DashboardDOM.safe('#log-type-gateway-btn');
            if (allBtn && gatewayBtn) {
                allBtn.onclick = () => {
                    window.DashboardState.set('filters.logs.gatewayOnly', false);
                    this.updateGatewayOnlyButtons(false);
                    this.renderLogs();
                };
                gatewayBtn.onclick = () => {
                    window.DashboardState.set('filters.logs.gatewayOnly', true);
                    this.updateGatewayOnlyButtons(true);
                    this.renderLogs();
                };
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

            // Restore gateway-only toggle state
            const gatewayOnly = state.get('filters.logs.gatewayOnly') || false;
            this.updateGatewayOnlyButtons(gatewayOnly);
        },

        // ===== Update Gateway-Only Toggle Buttons =====
        updateGatewayOnlyButtons: function(gatewayOnly) {
            const allBtn = window.DashboardDOM.safe('#log-type-all-btn');
            const gatewayBtn = window.DashboardDOM.safe('#log-type-gateway-btn');
            if (allBtn && gatewayBtn) {
                if (gatewayOnly) {
                    allBtn.classList.remove('active');
                    gatewayBtn.classList.add('active');
                } else {
                    allBtn.classList.add('active');
                    gatewayBtn.classList.remove('active');
                }
            }
        },

        // ===== Render Log Entries =====
        renderLogEntries: function(entries, container) {
            window.DashboardDOM.clear(container);
            container.classList.add('log-entries-container');

            const fragment = document.createDocumentFragment();

            // Pre-group entries by traceId for O(n) pairing instead of O(n^2)
            const traceIdMap = new Map();
            const processed = new Set();
            const self = this;

            // Build traceId index: O(n)
            entries.forEach((entry, index) => {
                if (entry.traceId && entry.eventType !== 'YarpEvent') {
                    if (!traceIdMap.has(entry.traceId)) {
                        traceIdMap.set(entry.traceId, []);
                    }
                    traceIdMap.get(entry.traceId).push({ entry, index });
                }
            });

            // Process entries: O(n)
            entries.forEach((entry, index) => {
                if (processed.has(index)) return;

                // Try to find paired entry by traceId using pre-built map
                if (entry.traceId && entry.eventType !== 'YarpEvent') {
                    const group = traceIdMap.get(entry.traceId);
                    if (group && group.length > 1) {
                        // Find the pair that hasn't been processed
                        const pairIndex = group.findIndex(x => x.index === index);
                        const pairInfo = group.length === 2
                            ? group[pairIndex === 0 ? 1 : 0]
                            : group.find((x, i) => i !== pairIndex && !processed.has(x.index));

                        if (pairInfo && !processed.has(pairInfo.index)) {
                            // Found a pair - create grouped item
                            const pairedEntry = pairInfo.entry;
                            const requestEntry = entry.eventType === 'ProxyRequest' ? entry : pairedEntry;
                            const responseEntry = entry.eventType === 'ProxyResponse' ? entry : pairedEntry;

                            const logKey = `paired:${requestEntry.timestamp}|${responseEntry.timestamp}|${requestEntry.traceId}`;
                            const isExpanded = window.DashboardState.get(`ui.expandedLogs.${logKey}`) || false;
                            const item = self.createPairedLogItem(requestEntry, responseEntry, logKey, isExpanded);
                            fragment.appendChild(item);

                            processed.add(index);
                            processed.add(pairInfo.index);
                            return;
                        }
                    }
                }

                // No pair found - render as single entry
                const logKey = `${entry.timestamp}|${entry.level}|${(entry.message || '').substring(0, 80)}`;
                const isExpanded = window.DashboardState.get(`ui.expandedLogs.${logKey}`) || false;
                const item = self.createLogItem(entry, logKey, isExpanded);
                fragment.appendChild(item);
            });

            container.appendChild(fragment);
        },

        // ===== Create Paired Log Item (Request + Response grouped) =====
        createPairedLogItem: function(requestEntry, responseEntry, logKey, isExpanded) {
            // Determine level from response (error if status >= 500)
            const hasError = (responseEntry.statusCode || 0) >= 500;
            const levelClass = hasError ? 'level-error' : 'level-info';

            const item = window.DashboardDOM.create('div', {
                className: `log-item ${levelClass} log-paired-item`,
                attributes: { 'data-log-key': logKey }
            });

            // Clickable row
            const row = window.DashboardDOM.create('div', {
                className: 'log-row',
                events: {
                    click: (e) => this.toggleLogEntryDirect(logKey, e)
                }
            });

            // Time (use response time as it's later)
            const timeSpan = window.DashboardDOM.create('span', {
                textContent: window.DashboardI18n.formatTime(new Date(responseEntry.timestamp)),
                style: { color: '#64748b', whiteSpace: 'nowrap', minWidth: '70px', fontSize: '12px' }
            });

            // Status badge
            const statusCode = responseEntry.statusCode || 0;
            const statusBadge = window.DashboardDOM.create('span', {
                className: `badge ${this.getStatusCodeBadge(statusCode)}`,
                textContent: statusCode,
                style: { minWidth: '40px', textAlign: 'center', fontSize: '11px' }
            });

            // Method badge
            const method = requestEntry.method || '-';
            const methodColors = {
                'GET': 'bg-success', 'POST': 'bg-primary', 'PUT': 'bg-info',
                'DELETE': 'bg-danger', 'PATCH': 'bg-warning text-dark'
            };
            const methodClass = methodColors[method] || 'bg-secondary';
            const methodBadge = window.DashboardDOM.create('span', {
                className: `badge ${methodClass}`,
                textContent: method,
                style: { minWidth: '45px', textAlign: 'center', fontSize: '10px', fontWeight: '700' }
            });

            // Path
            const path = requestEntry.upstreamPath || responseEntry.upstreamPath || '-';
            const pathSpan = window.DashboardDOM.create('code', {
                textContent: path,
                style: {
                    background: '#f1f5f9', padding: '1px 6px', borderRadius: '3px',
                    fontSize: '11px', color: '#0f172a', flex: '1',
                    overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap'
                }
            });

            // Duration
            let durationSpan = null;
            const elapsed = responseEntry.elapsedMs;
            if (elapsed != null) {
                const elapsedClass = elapsed < 200 ? 'text-success' : elapsed < 1000 ? 'text-warning' : 'text-danger';
                durationSpan = window.DashboardDOM.create('span', {
                    className: elapsedClass,
                    textContent: `${elapsed.toFixed(0)}ms`,
                    style: { fontSize: '11px', fontWeight: '600', minWidth: '50px', textAlign: 'right', whiteSpace: 'nowrap' }
                });
            }

            // Copy button
            const copyBtn = window.DashboardDOM.create('button', {
                className: 'btn btn-sm btn-link p-0',
                innerHTML: '<i class="bi bi-clipboard" style="color:#94a3b8"></i>',
                style: { marginLeft: '4px', opacity: '0.6', transition: 'all 0.2s ease' },
                events: {
                    click: (e) => {
                        e.stopPropagation();
                        const combined = { request: requestEntry, response: responseEntry };
                        this.copyLogEntry(combined, copyBtn);
                    }
                }
            });

            // Arrow
            const arrowSpan = window.DashboardDOM.create('i', {
                className: `bi bi-chevron-right log-arrow ${isExpanded ? 'expanded' : ''}`,
                style: { color: '#94a3b8', fontSize: '12px', transition: 'transform 0.2s ease' }
            });

            // Paired tag
            const pairedTag = document.createElement('span');
            pairedTag.innerHTML = `<span class="log-event-tag log-event-paired"><i class="bi bi-link-45deg"></i> ${__('index.log.pairedTag')}</span>`;

            row.appendChild(timeSpan);
            row.appendChild(statusBadge);
            row.appendChild(methodBadge);
            if (pairedTag.firstChild) row.appendChild(pairedTag.firstChild);
            row.appendChild(pathSpan);
            if (durationSpan) row.appendChild(durationSpan);
            row.appendChild(copyBtn);
            row.appendChild(arrowSpan);

            // Detail section
            const detail = this.createPairedLogDetail(requestEntry, responseEntry, isExpanded);

            item.appendChild(row);
            item.appendChild(detail);

            return item;
        },

        // ===== Create Paired Log Detail (Request → Response flow) =====
        createPairedLogDetail: function(requestEntry, responseEntry, isExpanded) {
            const detail = window.DashboardDOM.create('div', {
                className: `log-detail ${isExpanded ? 'expanded' : ''}`
            });

            const dtHtml = [];
            dtHtml.push('<div class="log-flow">');

            // ─── Upstream Request ───
            dtHtml.push('<div class="log-flow-section">');
            dtHtml.push(`<div class="log-flow-title"><i class="bi bi-box-arrow-in-down"></i> ${__('index.log.upstream.request')}</div>`);
            dtHtml.push('<div class="log-flow-body">');
            if (requestEntry.method) {
                const methodColors = { 'GET': 'bg-success', 'POST': 'bg-primary', 'PUT': 'bg-info', 'DELETE': 'bg-danger', 'PATCH': 'bg-warning text-dark' };
                dtHtml.push(`<div class="log-kv"><span class="log-kv-label">${__('index.log.request.method')}</span><span class="badge ${methodColors[requestEntry.method] || 'bg-secondary'}">${requestEntry.method}</span></div>`);
            }
            if (requestEntry.upstreamPath) {
                dtHtml.push(`<div class="log-kv"><span class="log-kv-label">${__('index.log.request.path')}</span><code class="log-kv-code">${window.DashboardUtils.escapeHtml(requestEntry.upstreamPath)}</code></div>`);
            }
            if (requestEntry.requestBody) {
                dtHtml.push(`<div class="log-kv"><span class="log-kv-label">${__('index.log.request.body')}</span>`);
                dtHtml.push(this.renderBodyContent(requestEntry.requestBody, requestEntry.requestBodyTruncated));
                dtHtml.push('</div>');
            }
            if (requestEntry.requestHeaders && Object.keys(requestEntry.requestHeaders).length > 0) {
                dtHtml.push(`<div class="log-kv"><span class="log-kv-label">${__('index.log.request.headers')}</span>`);
                dtHtml.push(this.renderHeadersInline(requestEntry.requestHeaders));
                dtHtml.push('</div>');
            }
            dtHtml.push('</div></div>');

            // Arrow down
            dtHtml.push('<div class="log-flow-arrow"><i class="bi bi-arrow-down-circle-fill"></i></div>');

            // ─── Downstream Request ───
            dtHtml.push('<div class="log-flow-section">');
            dtHtml.push(`<div class="log-flow-title"><i class="bi bi-box-arrow-up-right"></i> ${__('index.log.downstream.request')}</div>`);
            dtHtml.push('<div class="log-flow-body">');
            const dsMethod = requestEntry.downstreamMethod || requestEntry.method;
            if (dsMethod) {
                const methodColors = { 'GET': 'bg-success', 'POST': 'bg-primary', 'PUT': 'bg-info', 'DELETE': 'bg-danger', 'PATCH': 'bg-warning text-dark' };
                dtHtml.push(`<div class="log-kv"><span class="log-kv-label">${__('index.log.request.method')}</span><span class="badge ${methodColors[dsMethod] || 'bg-secondary'}">${dsMethod}</span></div>`);
            }
            if (requestEntry.downstreamUrl) {
                dtHtml.push(`<div class="log-kv"><span class="log-kv-label">${__('index.log.downstream.url')}</span><code class="log-kv-code">${window.DashboardUtils.escapeHtml(requestEntry.downstreamUrl)}</code></div>`);
            }
            if (requestEntry.downstreamBody) {
                dtHtml.push(`<div class="log-kv"><span class="log-kv-label">${__('index.log.downstream.body')}</span>`);
                dtHtml.push(this.renderBodyContent(requestEntry.downstreamBody, requestEntry.downstreamBodyTruncated));
                dtHtml.push('</div>');
            }
            dtHtml.push('</div></div>');

            // Arrow up
            dtHtml.push('<div class="log-flow-arrow"><i class="bi bi-arrow-up-circle-fill"></i></div>');

            // ─── Response (combined from downstream + upstream) ───
            dtHtml.push('<div class="log-flow-section">');
            dtHtml.push(`<div class="log-flow-title"><i class="bi bi-reply-all"></i> ${__('index.log.pairedResponse')}</div>`);
            dtHtml.push('<div class="log-flow-body">');
            if (responseEntry.statusCode != null) {
                const elapsed = responseEntry.elapsedMs;
                const elapsedClass = elapsed != null ? (elapsed < 200 ? 'text-success' : elapsed < 1000 ? 'text-warning' : 'text-danger') : '';
                const elapsedText = elapsed != null ? ` <strong class="${elapsedClass}">(${elapsed.toFixed(1)}ms)</strong>` : '';
                dtHtml.push(`<div class="log-kv"><span class="log-kv-label">${__('index.log.response.status')}</span><span class="badge ${this.getStatusCodeBadge(responseEntry.statusCode)}">${responseEntry.statusCode}</span>${elapsedText}</div>`);
            }
            if (responseEntry.responseBody) {
                dtHtml.push(`<div class="log-kv"><span class="log-kv-label">${__('index.log.response.body')}</span>`);
                dtHtml.push(this.renderBodyContent(responseEntry.responseBody, responseEntry.responseBodyTruncated));
                dtHtml.push('</div>');
            }
            if (responseEntry.responseHeaders && Object.keys(responseEntry.responseHeaders).length > 0) {
                dtHtml.push(`<div class="log-kv"><span class="log-kv-label">${__('index.log.response.headers')}</span>`);
                dtHtml.push(this.renderHeadersInline(responseEntry.responseHeaders));
                dtHtml.push('</div>');
            }
            dtHtml.push('</div></div>');

            dtHtml.push('</div>'); // end log-flow

            // Metadata row
            dtHtml.push('<div class="log-meta-row">');
            if (requestEntry.routeId) dtHtml.push(`<span><strong>RouteId:</strong> <code>${window.DashboardUtils.escapeHtml(requestEntry.routeId)}</code></span>`);
            if (requestEntry.clusterId) dtHtml.push(`<span><strong>ClusterId:</strong> <code>${window.DashboardUtils.escapeHtml(requestEntry.clusterId)}</code></span>`);
            if (requestEntry.traceId) dtHtml.push(`<span><strong>TraceId:</strong> <code class="text-muted">${window.DashboardUtils.escapeHtml(requestEntry.traceId)}</code></span>`);
            dtHtml.push('</div>');

            // Exception
            if (responseEntry.exception) {
                dtHtml.push(`<div style="color:#dc2626;margin-top:6px"><strong>${__('index.log.exception')}</strong></div>`);
                dtHtml.push(`<pre style="background:#fef2f2;border:1px solid #fecaca;border-radius:4px;padding:8px;margin:4px 0 0;overflow-x:auto;white-space:pre-wrap;word-break:break-all;font-size:11px;color:#991b1b;">${window.DashboardUtils.escapeHtml(responseEntry.exception)}</pre>`);
            }

            detail.innerHTML = dtHtml.join('');
            return detail;
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

            // Event type tag (for proxy request/response)
            let eventTypeTag = '';
            if (entry.eventType === 'ProxyRequest') {
                eventTypeTag = `<span class="log-event-tag log-event-request">${__('index.log.eventType.request')}</span>`;
            } else if (entry.eventType === 'ProxyResponse') {
                eventTypeTag = `<span class="log-event-tag log-event-response">${__('index.log.eventType.response')}</span>`;
            } else {
                eventTypeTag = `<span class="log-event-tag log-event-yarp">YARP</span>`;
            }

            // Message (main content) - enrich for proxy events
            let displayMessage = entry.message || '';
            if (entry.eventType === 'ProxyResponse' && entry.elapsedMs != null) {
                displayMessage += ` (${entry.elapsedMs.toFixed(0)}ms)`;
            }
            const msgSpan = window.DashboardDOM.create('span', {
                textContent: displayMessage,
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
            
            // Add event type tag via innerHTML helper
            const eventTypeSpan = document.createElement('span');
            eventTypeSpan.innerHTML = eventTypeTag;
            if (eventTypeSpan.firstChild) {
                row.appendChild(eventTypeSpan.firstChild);
            }
            
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

            // ─── ProxyRequest Event Type ───
            if (entry.eventType === 'ProxyRequest') {
                dtHtml.push('<div class="log-flow">');

                // Upstream Section
                dtHtml.push('<div class="log-flow-section">');
                dtHtml.push(`<div class="log-flow-title"><i class="bi bi-box-arrow-in-down"></i> ${__('index.log.upstream')}</div>`);
                dtHtml.push('<div class="log-flow-body">');
                if (entry.method) {
                    const methodColors = {
                        'GET': 'bg-success', 'POST': 'bg-primary', 'PUT': 'bg-info',
                        'DELETE': 'bg-danger', 'PATCH': 'bg-warning text-dark'
                    };
                    const methodClass = methodColors[entry.method] || 'bg-secondary';
                    dtHtml.push(`<div class="log-kv"><span class="log-kv-label">${__('index.log.request.method')}</span><span class="badge ${methodClass}">${entry.method}</span></div>`);
                }
                if (entry.upstreamPath) {
                    dtHtml.push(`<div class="log-kv"><span class="log-kv-label">${__('index.log.request.path')}</span><code class="log-kv-code">${window.DashboardUtils.escapeHtml(entry.upstreamPath)}</code></div>`);
                }
                if (entry.requestBody) {
                    dtHtml.push(`<div class="log-kv"><span class="log-kv-label">${__('index.log.request.body')}</span>`);
                    dtHtml.push(this.renderBodyContent(entry.requestBody, entry.requestBodyTruncated));
                    dtHtml.push('</div>');
                }
                if (entry.requestHeaders && Object.keys(entry.requestHeaders).length > 0) {
                    dtHtml.push(`<div class="log-kv"><span class="log-kv-label">${__('index.log.request.headers')}</span>`);
                    dtHtml.push(this.renderHeadersInline(entry.requestHeaders));
                    dtHtml.push('</div>');
                }
                dtHtml.push('</div></div>'); // end upstream

                // Arrow
                dtHtml.push('<div class="log-flow-arrow"><i class="bi bi-arrow-down-circle-fill"></i></div>');

                // Downstream Section
                dtHtml.push('<div class="log-flow-section">');
                dtHtml.push(`<div class="log-flow-title"><i class="bi bi-box-arrow-up-right"></i> ${__('index.log.downstream')}</div>`);
                dtHtml.push('<div class="log-flow-body">');
                const dsMethod = entry.downstreamMethod || entry.method;
                if (dsMethod) {
                    const methodColors = {
                        'GET': 'bg-success', 'POST': 'bg-primary', 'PUT': 'bg-info',
                        'DELETE': 'bg-danger', 'PATCH': 'bg-warning text-dark'
                    };
                    const methodClass = methodColors[dsMethod] || 'bg-secondary';
                    dtHtml.push(`<div class="log-kv"><span class="log-kv-label">${__('index.log.request.method')}</span><span class="badge ${methodClass}">${dsMethod}</span></div>`);
                }
                if (entry.downstreamUrl) {
                    dtHtml.push(`<div class="log-kv"><span class="log-kv-label">${__('index.log.downstream.url')}</span><code class="log-kv-code">${window.DashboardUtils.escapeHtml(entry.downstreamUrl)}</code></div>`);
                }
                if (entry.downstreamBody) {
                    dtHtml.push(`<div class="log-kv"><span class="log-kv-label">${__('index.log.downstream.body')}</span>`);
                    dtHtml.push(this.renderBodyContent(entry.downstreamBody, entry.downstreamBodyTruncated));
                    dtHtml.push('</div>');
                }
                dtHtml.push('</div></div>'); // end downstream

                dtHtml.push('</div>'); // end log-flow

                // Metadata
                dtHtml.push('<div class="log-meta-row">');
                if (entry.routeId) dtHtml.push(`<span><strong>RouteId:</strong> <code>${window.DashboardUtils.escapeHtml(entry.routeId)}</code></span>`);
                if (entry.clusterId) dtHtml.push(`<span><strong>ClusterId:</strong> <code>${window.DashboardUtils.escapeHtml(entry.clusterId)}</code></span>`);
                if (entry.traceId) dtHtml.push(`<span><strong>TraceId:</strong> <code class="text-muted">${window.DashboardUtils.escapeHtml(entry.traceId)}</code></span>`);
                dtHtml.push('</div>');
            }
            // ─── ProxyResponse Event Type ───
            else if (entry.eventType === 'ProxyResponse') {
                dtHtml.push('<div class="log-flow">');

                // Downstream Response Section (from destination server)
                dtHtml.push('<div class="log-flow-section">');
                dtHtml.push(`<div class="log-flow-title"><i class="bi bi-box-arrow-up-right"></i> ${__('index.log.downstream.response')}</div>`);
                dtHtml.push('<div class="log-flow-body">');
                if (entry.downstreamUrl) {
                    dtHtml.push(`<div class="log-kv"><span class="log-kv-label">${__('index.log.downstream.url')}</span><code class="log-kv-code">${window.DashboardUtils.escapeHtml(entry.downstreamUrl)}</code></div>`);
                }
                if (entry.statusCode != null) {
                    dtHtml.push(`<div class="log-kv"><span class="log-kv-label">${__('index.log.response.status')}</span><span class="badge ${this.getStatusCodeBadge(entry.statusCode)}">${entry.statusCode}</span></div>`);
                }
                if (entry.elapsedMs != null) {
                    const elapsedClass = entry.elapsedMs < 200 ? 'text-success' : entry.elapsedMs < 1000 ? 'text-warning' : 'text-danger';
                    dtHtml.push(`<div class="log-kv"><span class="log-kv-label">${__('index.log.response.duration')}</span><strong class="${elapsedClass}">${entry.elapsedMs.toFixed(1)} ms</strong></div>`);
                }
                if (entry.responseBody) {
                    dtHtml.push(`<div class="log-kv"><span class="log-kv-label">${__('index.log.response.body')}</span>`);
                    dtHtml.push(this.renderBodyContent(entry.responseBody, entry.responseBodyTruncated));
                    dtHtml.push('</div>');
                }
                if (entry.responseHeaders && Object.keys(entry.responseHeaders).length > 0) {
                    dtHtml.push(`<div class="log-kv"><span class="log-kv-label">${__('index.log.response.headers')}</span>`);
                    dtHtml.push(this.renderHeadersInline(entry.responseHeaders));
                    dtHtml.push('</div>');
                }
                dtHtml.push('</div></div>'); // end downstream response

                // Arrow (upward: downstream → upstream)
                dtHtml.push('<div class="log-flow-arrow"><i class="bi bi-arrow-up-circle-fill"></i></div>');

                // Upstream Response Section (returned to client)
                dtHtml.push('<div class="log-flow-section">');
                dtHtml.push(`<div class="log-flow-title"><i class="bi bi-box-arrow-in-down"></i> ${__('index.log.upstream.response')}</div>`);
                dtHtml.push('<div class="log-flow-body">');
                if (entry.upstreamPath) {
                    dtHtml.push(`<div class="log-kv"><span class="log-kv-label">${__('index.log.request.path')}</span><code class="log-kv-code">${window.DashboardUtils.escapeHtml(entry.upstreamPath)}</code></div>`);
                }
                if (entry.statusCode != null) {
                    dtHtml.push(`<div class="log-kv"><span class="log-kv-label">${__('index.log.response.status')}</span><span class="badge ${this.getStatusCodeBadge(entry.statusCode)}">${entry.statusCode}</span></div>`);
                }
                dtHtml.push('</div></div>'); // end upstream response

                dtHtml.push('</div>'); // end log-flow

                // Metadata
                dtHtml.push('<div class="log-meta-row">');
                if (entry.routeId) dtHtml.push(`<span><strong>RouteId:</strong> <code>${window.DashboardUtils.escapeHtml(entry.routeId)}</code></span>`);
                if (entry.clusterId) dtHtml.push(`<span><strong>ClusterId:</strong> <code>${window.DashboardUtils.escapeHtml(entry.clusterId)}</code></span>`);
                if (entry.traceId) dtHtml.push(`<span><strong>TraceId:</strong> <code class="text-muted">${window.DashboardUtils.escapeHtml(entry.traceId)}</code></span>`);
                dtHtml.push('</div>');
            }
            // ─── YarpEvent Type (fallback) ───
            else {
                // Metadata row
                dtHtml.push('<div class="log-meta-row">');
                if (entry.category) dtHtml.push(`<span><strong>${__('index.log.category')}</strong> ${window.DashboardUtils.escapeHtml(entry.category)}</span>`);
                if (entry.routeId) dtHtml.push(`<span><strong>RouteId:</strong> <code>${window.DashboardUtils.escapeHtml(entry.routeId)}</code></span>`);
                if (entry.traceId) dtHtml.push(`<span><strong>TraceId:</strong> <code>${window.DashboardUtils.escapeHtml(entry.traceId)}</code></span>`);
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
            }

            // Exception (for any type)
            if (entry.exception) {
                dtHtml.push(`<div style="color:#dc2626;margin-top:6px"><strong>${__('index.log.exception')}</strong></div>`);
                dtHtml.push(`<pre style="background:#fef2f2;border:1px solid #fecaca;border-radius:4px;padding:8px;margin:4px 0 0;overflow-x:auto;white-space:pre-wrap;word-break:break-all;font-size:11px;color:#991b1b;">${window.DashboardUtils.escapeHtml(entry.exception)}</pre>`);
            }
                
            detail.innerHTML = dtHtml.join('');
        
            return detail;
        },

        // ===== Render Body Content =====
        renderBodyContent: function(body, truncated) {
            if (!body) return '<span class="text-muted">-</span>';
            const escaped = window.DashboardUtils.escapeHtml(body);
            const truncatedNotice = truncated ? `<span class="text-muted small">(${__('index.log.truncated')})</span>` : '';
            
            // Try to format as JSON
            try {
                const obj = JSON.parse(body);
                const formatted = JSON.stringify(obj, null, 2);
                const escapedFormatted = window.DashboardUtils.escapeHtml(formatted);
                return `<pre class="log-body-pre">${escapedFormatted}</pre>${truncatedNotice}`;
            } catch (e) {
                // Not JSON, display as-is
                return `<pre class="log-body-pre">${escaped}</pre>${truncatedNotice}`;
            }
        },

        // ===== Render Headers Inline =====
        renderHeadersInline: function(headers) {
            if (!headers || typeof headers !== 'object') return '<span class="text-muted">-</span>';
            const keys = Object.keys(headers);
            if (keys.length === 0) return '<span class="text-muted">-</span>';
            
            const rows = keys.map(k => {
                const val = headers[k];
                const displayVal = val === '***REDACTED***' 
                    ? '<span class="log-redacted">***REDACTED***</span>' 
                    : window.DashboardUtils.escapeHtml(val);
                return `<div class="log-header-row"><span class="log-header-key">${window.DashboardUtils.escapeHtml(k)}</span><span class="log-header-val">${displayVal}</span></div>`;
            });
            return `<div class="log-headers-block">${rows.join('')}</div>`;
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
                    window.DashboardModals.showSuccess(__('index.copied'));
                }
            }).catch(err => {
                console.error('[Logs] Failed to copy:', err);
                if (window.DashboardModals) {
                    window.DashboardModals.showError(__('index.copyFailed'));
                }
            });
        },

        // ===== Update Log Counts =====
        updateLogCounts: function(entries) {
            const allLogs = window.DashboardState.get('data.logs') || [];
            const meta = window.DashboardState.get('data.logMeta') || {};
            
            const displayEl = window.DashboardDOM.safe('#log-display-count');
            if (displayEl) displayEl.textContent = entries.length;

            const totalEl = window.DashboardDOM.safe('#log-total-count');
            if (totalEl) {
                let text = `${allLogs.length}/${meta.bufferCapacity || '?'}`;
                if (meta.evictedCount > 0) text += ` (${meta.evictedCount} evicted)`;
                totalEl.textContent = text;
            }

            const timeEl = window.DashboardDOM.safe('#log-refresh-time');
            if (timeEl) timeEl.textContent = __('index.log.updated') + window.DashboardI18n.formatTime(new Date());
        },

        // ===== Polling/SSE Control =====
        togglePolling: function() {
            const state = window.DashboardState;
            const isPolling = state.get('filters.logs.autoRefresh');
            
            if (isPolling) {
                this.stopStreaming();
            } else {
                this.startStreaming();
            }
        },

        startStreaming: function() {
            const state = window.DashboardState;
            state.set('filters.logs.autoRefresh', true);
            this.updateListenButton(true);

            // Try SSE first, fallback to polling
            if (this.sseEnabled && typeof EventSource !== 'undefined') {
                this.startSSE();
            } else {
                this.startPolling();
            }
        },

        stopStreaming: function() {
            const state = window.DashboardState;

            // Stop SSE
            if (this.sseConnection) {
                this.sseConnection.close();
                this.sseConnection = null;
            }

            // Stop polling
            if (this.pollTimer) {
                clearInterval(this.pollTimer);
                this.pollTimer = null;
            }

            // Reset the hidden state flag when manually stopping
            this.wasPollingBeforeHidden = false;

            state.set('filters.logs.autoRefresh', false);
            this.updateListenButton(false);
        },

        startSSE: function() {
            const basePath = window.__dashboard?.basePath || '';
            const sseUrl = `${basePath}/logstream/logs`;

            try {
                this.sseConnection = new EventSource(sseUrl);
                console.log('[Logs] SSE connection established');

                this.sseConnection.onopen = () => {
                    console.log('[Logs] SSE connection opened');
                };

                this.sseConnection.onmessage = (event) => {
                    if (event.data.startsWith(':')) {
                        // Keepalive or comment, ignore
                        return;
                    }

                    try {
                        const entry = JSON.parse(event.data);
                        if (entry.connected) {
                            console.log('[Logs] SSE connected successfully');
                            // Load initial logs
                            this.loadLogs();
                            return;
                        }
                        if (entry.error) {
                            console.warn('[Logs] SSE error:', entry.error);
                            this.fallbackToPolling();
                            return;
                        }

                        // Add entry to pending queue
                        this.pendingLogEntries.push(entry);
                        this.flushPendingEntries();
                    } catch (err) {
                        console.error('[Logs] Failed to parse SSE message:', err);
                    }
                };

                this.sseConnection.onerror = (error) => {
                    console.error('[Logs] SSE error:', error);
                    this.fallbackToPolling();
                };
            } catch (err) {
                console.error('[Logs] Failed to create SSE connection:', err);
                this.fallbackToPolling();
            }
        },

        fallbackToPolling: function() {
            console.log('[Logs] Falling back to polling mode');
            if (this.sseConnection) {
                this.sseConnection.close();
                this.sseConnection = null;
            }
            this.sseEnabled = false;
            this.startPolling();
        },

        startPolling: function() {
            if (this.pollTimer) {
                clearInterval(this.pollTimer);
            }

            this.loadLogs();
            const interval = window.DashboardState.get('filters.logs.refreshInterval') || 5000;
            this.pollTimer = setInterval(() => this.loadLogs(), interval);
        },

        flushPendingEntries: function() {
            if (this.isRendering || this.pendingLogEntries.length === 0) return;

            const state = window.DashboardState;
            const logs = state.get('data.logs') || [];

            // Add new entries to the front (newest first)
            while (this.pendingLogEntries.length > 0) {
                const entry = this.pendingLogEntries.shift();
                logs.unshift(entry);
            }

            // Trim to max count
            const maxCount = state.get('filters.logs.maxCount') || 100;
            while (logs.length > maxCount) {
                logs.pop();
            }

            state.set('data.logs', logs);
            this.renderLogs();
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

            // Page Visibility API - pause streaming when tab is hidden to save resources
            document.addEventListener('visibilitychange', () => {
                if (document.hidden) {
                    // Page is hidden - pause streaming if active
                    const state = window.DashboardState;
                    if (state.get('filters.logs.autoRefresh')) {
                        this.wasPollingBeforeHidden = true;
                        if (this.sseConnection) {
                            this.sseConnection.close();
                            this.sseConnection = null;
                            console.log('[Logs] Paused SSE (page hidden)');
                        } else if (this.pollTimer) {
                            clearInterval(this.pollTimer);
                            this.pollTimer = null;
                            console.log('[Logs] Paused polling (page hidden)');
                        }
                    }
                } else {
                    // Page is visible again - resume streaming if it was active
                    if (this.wasPollingBeforeHidden) {
                        this.wasPollingBeforeHidden = false;
                        this.startStreaming();
                        console.log('[Logs] Resumed streaming (page visible)');
                    }
                }
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

    // ===== Performance Optimizations Integration =====
    // Auto-initialize enhanced virtual scrolling if DashboardPerformance is available
    if (window.DashboardPerformance) {
        LogsModule.enhancedVirtualScroll = null;
        LogsModule.workerFilterCache = new Map();

        // Override renderLogs to use enhanced virtual scrolling
        const originalRenderLogs = LogsModule.renderLogs.bind(LogsModule);
        LogsModule.renderLogs = async function() {
            const state = window.DashboardState;
            this.isRendering = true;

            const entries = state.getFilteredLogs ? state.getFilteredLogs() : (state.get('data.logs') || []);

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
                // Reset virtual scroll state
                this.virtualScroll.totalHeight = 0;
                this.virtualScroll.lastEntriesLength = 0;
                if (this.enhancedVirtualScroll) {
                    this.enhancedVirtualScroll.destroy();
                    this.enhancedVirtualScroll = null;
                }
            } else {
                // Use enhanced virtual scrolling for large lists
                if (entries.length > 100 && this.virtualScroll.enabled && window.DashboardPerformance.VirtualScroller) {
                    this.renderEnhancedVirtualLogEntries(entries, container);
                } else {
                    // Fall back to regular rendering for small lists
                    if (this.enhancedVirtualScroll) {
                        this.enhancedVirtualScroll.destroy();
                        this.enhancedVirtualScroll = null;
                    }
                    this.renderLogEntries(entries, container);
                }
            }

            // Update counts
            this.updateLogCounts(entries);

            // Auto-scroll if polling (only if at top)
            if (state.get('filters.logs.autoRefresh') && scrollEl && scrollEl.scrollTop < 50) {
                requestAnimationFrame(() => {
                    scrollEl.scrollTop = 0;
                    this.virtualScroll.scrollTop = 0;
                    if (this.enhancedVirtualScroll) {
                        this.enhancedVirtualScroll.scrollToIndex(0, 'auto');
                    }
                });
            }

            // Mark rendering complete and flush any pending SSE entries
            this.isRendering = false;
            this.flushPendingEntries();
        };

        // Enhanced virtual scrolling using DashboardPerformance.VirtualScroller
        LogsModule.renderEnhancedVirtualLogEntries = function(entries, container) {
            // Initialize enhanced scroller if not exists
            if (!this.enhancedVirtualScroll) {
                const scrollContainer = window.DashboardDOM.safe('#log-scroll-container');

                this.enhancedVirtualScroll = new window.DashboardPerformance.VirtualScroller(
                    container,
                    {
                        scrollContainer: scrollContainer,
                        itemHeight: this.virtualScroll.itemHeight,
                        overscan: this.virtualScroll.overscan,
                        poolSize: 100,
                        renderFn: (el, entry, index) => {
                            this.renderLogEntryToElement(el, entry, index);
                        }
                    }
                );
            }

            // Pre-process entries for trace pairing
            const traceIdMap = new Map();
            entries.forEach((entry, index) => {
                if (entry.traceId && entry.eventType !== 'YarpEvent') {
                    if (!traceIdMap.has(entry.traceId)) {
                        traceIdMap.set(entry.traceId, []);
                    }
                    traceIdMap.get(entry.traceId).push({ entry, index });
                }
            });

            // Store processed data
            this.enhancedVirtualScroll.traceIdMap = traceIdMap;

            // Set data
            this.enhancedVirtualScroll.setData(entries);
        };

        // Render a single log entry to a DOM element
        LogsModule.renderLogEntryToElement = function(el, entry, index) {
            // Try to find paired entry
            if (entry.traceId && entry.eventType !== 'YarpEvent') {
                const traceIdMap = this.enhancedVirtualScroll?.traceIdMap || this.virtualScroll.traceIdMap;
                const group = traceIdMap?.get(entry.traceId);

                if (group && group.length > 1) {
                    const pairIndex = group.findIndex(x => x.index === index);
                    const pairInfo = group.find((x, i) => i !== pairIndex);

                    if (pairInfo) {
                        const pairedEntry = pairInfo.entry;
                        const requestEntry = entry.eventType === 'ProxyRequest' ? entry : pairedEntry;
                        const responseEntry = entry.eventType === 'ProxyResponse' ? entry : pairedEntry;

                        const logKey = `paired:${requestEntry.timestamp}|${responseEntry.timestamp}|${requestEntry.traceId}`;
                        const isExpanded = window.DashboardState.get(`ui.expandedLogs.${logKey}`) || false;

                        // Create paired item HTML
                        el.innerHTML = this.createPairedLogItemHTML(requestEntry, responseEntry, logKey, isExpanded);
                        return;
                    }
                }
            }

            // Single entry
            const logKey = `${entry.timestamp}|${entry.level}|${(entry.message || '').substring(0, 80)}`;
            const isExpanded = window.DashboardState.get(`ui.expandedLogs.${logKey}`) || false;

            el.innerHTML = this.createLogItemHTML(entry, logKey, isExpanded);
        };

        // Create paired log item HTML (returns HTML string for enhanced scroller)
        LogsModule.createPairedLogItemHTML = function(requestEntry, responseEntry, logKey, isExpanded) {
            const statusCode = responseEntry.statusCode || '-';
            const statusClass = this.getStatusClass(statusCode);
            const elapsedMs = responseEntry.elapsedMs?.toFixed(2) || '-';

            return `
                <div class="log-item log-item-paired" data-log-key="${this.escapeHtml(logKey)}" style="height: auto; min-height: ${this.virtualScroll.itemHeight}px;">
                    <div class="log-item-header d-flex align-items-center gap-2 cursor-pointer" onclick="LogsModule.toggleLogExpand('${this.escapeHtml(logKey)}')">
                        <i class="bi bi-chevron-${isExpanded ? 'down' : 'right'} text-muted"></i>
                        <span class="log-badge method-badge method-${(requestEntry.method || 'GET').toLowerCase()}">${requestEntry.method || 'GET'}</span>
                        <span class="log-status ${statusClass}">${statusCode}</span>
                        <span class="log-trace-id text-muted">${this.escapeHtml(requestEntry.traceId?.substring(0, 8) || '')}...</span>
                        <span class="log-path flex-1 text-truncate">${this.escapeHtml(requestEntry.upstreamPath || '')}</span>
                        <span class="log-time text-muted">${elapsedMs}ms</span>
                        <span class="log-timestamp text-muted">${this.formatTime(requestEntry.timestamp)}</span>
                    </div>
                    ${isExpanded ? this.createExpandedPairedContentHTML(requestEntry, responseEntry) : ''}
                </div>
            `;
        };

        // Create single log item HTML
        LogsModule.createLogItemHTML = function(entry, logKey, isExpanded) {
            const levelClass = this.getLevelClass(entry.level);
            const icon = this.getEventIcon(entry.eventType);

            return `
                <div class="log-item" data-log-key="${this.escapeHtml(logKey)}" style="height: ${this.virtualScroll.itemHeight}px;">
                    <div class="log-item-header d-flex align-items-center gap-2 cursor-pointer" onclick="LogsModule.toggleLogExpand('${this.escapeHtml(logKey)}')">
                        <i class="bi ${icon} text-muted"></i>
                        <span class="log-badge level-badge ${levelClass}">${entry.level || 'INFO'}</span>
                        <span class="log-category text-truncate">${this.escapeHtml(entry.category || '')}</span>
                        <span class="log-message flex-1 text-truncate">${this.escapeHtml(entry.message || '')}</span>
                        <span class="log-timestamp text-muted">${this.formatTime(entry.timestamp)}</span>
                    </div>
                    ${isExpanded ? this.createExpandedContentHTML(entry) : ''}
                </div>
            `;
        };

        // Helper methods for HTML creation
        LogsModule.escapeHtml = function(text) {
            if (!text) return '';
            const div = document.createElement('div');
            div.textContent = text;
            return div.innerHTML;
        };

        LogsModule.getStatusClass = function(statusCode) {
            if (!statusCode || statusCode === '-') return 'text-muted';
            if (statusCode < 300) return 'text-success';
            if (statusCode < 400) return 'text-warning';
            return 'text-danger';
        };

        LogsModule.getLevelClass = function(level) {
            const classes = {
                'Debug': 'bg-secondary',
                'Information': 'bg-info',
                'Warning': 'bg-warning',
                'Error': 'bg-danger',
                'Critical': 'bg-dark'
            };
            return classes[level] || 'bg-secondary';
        };

        LogsModule.getEventIcon = function(eventType) {
            const icons = {
                'ProxyRequest': 'bi-arrow-right-circle',
                'ProxyResponse': 'bi-arrow-left-circle',
                'YarpEvent': 'bi-gear'
            };
            return icons[eventType] || 'bi-journal';
        };

        LogsModule.createExpandedPairedContentHTML = function(requestEntry, responseEntry) {
            return `
                <div class="log-expanded-content p-3 bg-light border-top">
                    <div class="row g-3">
                        <div class="col-md-6">
                            <h6>Request</h6>
                            <pre class="small bg-white p-2 rounded border">${this.escapeHtml(JSON.stringify(requestEntry, null, 2))}</pre>
                        </div>
                        <div class="col-md-6">
                            <h6>Response</h6>
                            <pre class="small bg-white p-2 rounded border">${this.escapeHtml(JSON.stringify(responseEntry, null, 2))}</pre>
                        </div>
                    </div>
                </div>
            `;
        };

        LogsModule.createExpandedContentHTML = function(entry) {
            return `
                <div class="log-expanded-content p-3 bg-light border-top">
                    <pre class="small bg-white p-2 rounded border">${this.escapeHtml(JSON.stringify(entry, null, 2))}</pre>
                </div>
            `;
        };

        LogsModule.formatTime = function(timestamp) {
            if (!timestamp) return '';
            const date = new Date(timestamp);
            return date.toLocaleTimeString();
        };

        // Use Worker for filtering if available
        const originalLoadLogs = LogsModule.loadLogs.bind(LogsModule);
        LogsModule.loadLogs = async function() {
            // Try to get cached logs first
            if (window.DashboardIndexedDB) {
                try {
                    const cached = await window.DashboardIndexedDB.getRecentLogs(30);
                    if (cached && cached.length > 0) {
                        console.log('[Logs] Loaded', cached.length, 'entries from IndexedDB cache');
                        window.DashboardState.set('data.logs', cached);
                        this.renderLogs();
                    }
                } catch (err) {
                    console.warn('[Logs] Failed to load from cache:', err);
                }
            }

            // Load from API
            await originalLoadLogs();

            // Save to cache
            if (window.DashboardIndexedDB) {
                const logs = window.DashboardState.get('data.logs') || [];
                try {
                    await window.DashboardIndexedDB.saveLogs(logs);
                } catch (err) {
                    console.warn('[Logs] Failed to save to cache:', err);
                }
            }
        };

        // Enhanced filtering using Worker
        LogsModule.filterLogsWithWorker = async function(logs, filters) {
            if (!window.DashboardWorker) {
                // Fallback to original filtering
                return logs;
            }

            try {
                const filtered = await window.DashboardWorker.filterLogs(logs, filters);
                return filtered;
            } catch (err) {
                console.warn('[Logs] Worker filtering failed, using fallback:', err);
                return logs;
            }
        };
    }

    // ===== Service Worker Update Notification =====
    window.addEventListener('sw-update-available', (event) => {
        if (window.DashboardModals) {
            window.DashboardModals.showConfirm(
                'Update Available',
                'A new version is available. Would you like to update now?',
                () => event.detail.apply()
            );
        }
    });

})();
