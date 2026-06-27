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

        init: async function() {
            if (this.initialized) return;

            this.initVirtualScroll();
            this.setupEvents();

            this.initialized = true;
        },

        destroy: function() {
            this.stopPolling();
            this.initialized = false;
        },

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

        debounce: function(fn, delay) {
            let timeout;
            return function(...args) {
                clearTimeout(timeout);
                timeout = setTimeout(() => fn.apply(this, args), delay);
            };
        },

        loadLogs: async function() {
            try {
                const container = window.DashboardDOM.safe('#log-entries');
                if (!container) return;

                const state = window.DashboardState;
                const maxCount = state.get('filters.logs.maxCount') || 100;

                window.DashboardDOM.showLoading(container, __('index.log.loading'));

                const result = await window.DashboardApi.endpoints.getLogs(maxCount);

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
        },

        renderVirtualLogEntries: function(entries, container) {
            // Calculate total height
            this.virtualScroll.totalHeight = entries.length * this.virtualScroll.itemHeight;
            this.virtualScroll.lastEntriesLength = entries.length;

            // Pre-group entries by traceId for O(n) pairing
            const traceIdMap = new Map();
            const processed = new Set();

            entries.forEach((entry, index) => {
                if (entry.traceId) {
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

        createVirtualLogItem: function(entry, index, entries, traceIdMap, processed) {
            // Try to find paired entry
            if (entry.traceId) {
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
        },

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
                if (entry.traceId) {
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
                if (entry.traceId) {
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

        createPairedLogDetail: function(requestEntry, responseEntry, isExpanded) {
            const detail = window.DashboardDOM.create('div', {
                className: `log-detail ${isExpanded ? 'expanded' : ''}`
            });

            const dtHtml = [];
            dtHtml.push('<div class="log-flow">');

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

            dtHtml.push('<div class="log-flow-section">');
            dtHtml.push(`<div class="log-flow-title"><i class="bi bi-box-arrow-up-right"></i> ${__('index.log.downstream.request')}</div>`);
            dtHtml.push('<div class="log-flow-body">');
            const dsMethod = requestEntry.downstreamMethod || responseEntry.downstreamMethod || requestEntry.method;
            if (dsMethod) {
                const methodColors = { 'GET': 'bg-success', 'POST': 'bg-primary', 'PUT': 'bg-info', 'DELETE': 'bg-danger', 'PATCH': 'bg-warning text-dark' };
                dtHtml.push(`<div class="log-kv"><span class="log-kv-label">${__('index.log.request.method')}</span><span class="badge ${methodColors[dsMethod] || 'bg-secondary'}">${dsMethod}</span></div>`);
            }
            const dsUrl = requestEntry.downstreamUrl || responseEntry.downstreamUrl;
            if (dsUrl) {
                dtHtml.push(`<div class="log-kv"><span class="log-kv-label">${__('index.log.downstream.url')}</span><code class="log-kv-code">${window.DashboardUtils.escapeHtml(dsUrl)}</code></div>`);
            }
            const dsBody = requestEntry.downstreamBody || responseEntry.downstreamBody;
            if (dsBody) {
                dtHtml.push(`<div class="log-kv"><span class="log-kv-label">${__('index.log.downstream.body')}</span>`);
                dtHtml.push(this.renderBodyContent(dsBody, requestEntry.downstreamBodyTruncated || responseEntry.downstreamBodyTruncated));
                dtHtml.push('</div>');
            }
            dtHtml.push('</div></div>');

            // Arrow up
            dtHtml.push('<div class="log-flow-arrow"><i class="bi bi-arrow-up-circle-fill"></i></div>');

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

            // Metadata row (RouteId/ClusterId from request or response fallback)
            dtHtml.push('<div class="log-meta-row">');
            const rtId = requestEntry.routeId || responseEntry.routeId;
            if (rtId) dtHtml.push(`<span><strong>RouteId:</strong> <code>${window.DashboardUtils.escapeHtml(rtId)}</code></span>`);
            const clId = requestEntry.clusterId || responseEntry.clusterId;
            if (clId) dtHtml.push(`<span><strong>ClusterId:</strong> <code>${window.DashboardUtils.escapeHtml(clId)}</code></span>`);
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

        createLogDetail: function(entry, isExpanded) {
            const detail = window.DashboardDOM.create('div', {
                className: `log-detail ${isExpanded ? 'expanded' : ''}`
            });
                
            const dtHtml = [];

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
            else {
                // Metadata row
                dtHtml.push('<div class="log-meta-row">');
                if (entry.routeId) dtHtml.push(`<span><strong>RouteId:</strong> <code>${window.DashboardUtils.escapeHtml(entry.routeId)}</code></span>`);
                if (entry.traceId) dtHtml.push(`<span><strong>TraceId:</strong> <code>${window.DashboardUtils.escapeHtml(entry.traceId)}</code></span>`);
                dtHtml.push('</div>');

                // Message (full content)
                dtHtml.push(`<div class="mb-2"><strong>${__('index.log.message')}</strong><br>`);
                dtHtml.push(`<span style="color:#475569;word-break:break-all;">${window.DashboardUtils.escapeHtml(entry.message || '')}</span></div>`);
            }

            // Exception (for any type)
            if (entry.exception) {
                dtHtml.push(`<div style="color:#dc2626;margin-top:6px"><strong>${__('index.log.exception')}</strong></div>`);
                dtHtml.push(`<pre style="background:#fef2f2;border:1px solid #fecaca;border-radius:4px;padding:8px;margin:4px 0 0;overflow-x:auto;white-space:pre-wrap;word-break:break-all;font-size:11px;color:#991b1b;">${window.DashboardUtils.escapeHtml(entry.exception)}</pre>`);
            }
                
            detail.innerHTML = dtHtml.join('');
        
            return detail;
        },

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

        renderJsonBlock: function(obj, title) {
            return window.DashboardUtils.renderJsonBlock(obj, title);
        },

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

        getStatusCodeBadge: function(statusCode) {
            if (statusCode >= 200 && statusCode < 300) return 'bg-success';
            if (statusCode >= 300 && statusCode < 400) return 'bg-info';
            if (statusCode >= 400 && statusCode < 500) return 'bg-warning';
            if (statusCode >= 500) return 'bg-danger';
            return 'bg-secondary';
        },

        toggleLogEntryDirect: function(logKey, event) {
            const state = window.DashboardState;
            const current = state.get(`ui.expandedLogs.${logKey}`) || false;
            
            // If expanding and polling is active, stop polling
            if (!current && state.get('filters.logs.autoRefresh')) {
                this.stopPolling();
            }
            
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

        copyLogEntry: function(entry, btnElement) {
            const text = JSON.stringify(entry, null, 2);
            window.DashboardUtils.copyToClipboard(text).then((success) => {
                if (!success) {
                    console.error('[Logs] Failed to copy');
                    if (window.DashboardModals) {
                        window.DashboardModals.showError(__('index.copyFailed'));
                    }
                    return;
                }
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
            });
        },

        updateLogCounts: function(entries) {
            const allLogs = window.DashboardState.get('data.logs') || [];

            // Count actual rendered items (paired entries are merged into one row)
            const container = window.DashboardDOM.safe('#log-entries');
            let renderedCount = entries.length;
            if (container) {
                const items = container.querySelectorAll('.log-item');
                if (items.length > 0) renderedCount = items.length;
            }

            const displayEl = window.DashboardDOM.safe('#log-display-count');
            if (displayEl) displayEl.textContent = renderedCount;

            const totalEl = window.DashboardDOM.safe('#log-total-count');
            if (totalEl) {
                totalEl.textContent = allLogs.length;
            }

            const timeEl = window.DashboardDOM.safe('#log-refresh-time');
            if (timeEl) timeEl.textContent = __('index.log.updated') + window.DashboardI18n.formatTime(new Date());
        },

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

            // Reset the hidden state flag when manually stopping
            this.wasPollingBeforeHidden = false;

            state.set('filters.logs.autoRefresh', false);
            this.updateListenButton(false);
        },

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

        clearLogs: async function() {
            window.DashboardModals.showConfirm(__('index.log.clearConfirm'), async function() {
                try {
                    window.DashboardState.set('data.logs', []);
                    window.DashboardState.set('data.logMeta', {});
                    self.renderLogs();
                    window.DashboardModals.showSuccess(__('index.log.cleared'));
                } catch (e) { window.DashboardModals.showError(__('index.log.clearFailed')); }
            }, null, { danger: true });

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

        setupEvents: function() {
            // Refresh shortcut
            document.addEventListener('dashboard:shortcut:refresh', async () => {
                await this.loadLogs();
            });

            // Locale change
            document.addEventListener('dashboard:localeChange', () => {
                this.renderLogs();
            });

            // Page Visibility API - pause polling when tab is hidden to save resources
            document.addEventListener('visibilitychange', () => {
                if (document.hidden) {
                    // Page is hidden - pause polling if active
                    if (this.pollTimer) {
                        this.wasPollingBeforeHidden = true;
                        this.stopPolling();
                    }
                } else {
                    // Page is visible again - resume polling if it was active
                    if (this.wasPollingBeforeHidden) {
                        this.wasPollingBeforeHidden = false;
                        this.startPolling();
                    }
                }
            });
        }
    };

    if (window.DashboardApp) {
        window.DashboardApp.registerModule('logs', LogsModule);
    }

    // Expose to window for backward compatibility
    window.LogsModule = LogsModule;
    window.loadLogs = LogsModule.loadLogs.bind(LogsModule);
    window.toggleListening = LogsModule.togglePolling.bind(LogsModule);
    window.clearLogs = LogsModule.clearLogs.bind(LogsModule);

})();
