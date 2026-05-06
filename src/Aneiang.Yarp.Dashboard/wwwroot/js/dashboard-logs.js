/**
 * Dashboard Logs Module - Aneiang.Yarp Gateway Dashboard
 * Log data loading, polling, filtering, and UI
 * Refactored to use API layer, state management, and renderers
 */
(function() {
    'use strict';

    // ===== Load Log Data =====
    window.loadLogs = async function() {
        try {
            var state = window.dashboardState;
            state.logs.loading = true;
            
            var result = await window.dashboardApiMethods.getLogs(100);
            if (result.code !== 200) return;
            
            var snap = result.data;
            state.logs.data = snap.entries || [];
            window.renderLogs();
        } catch (e) { 
            console.error('Failed to load logs:', e);
        } finally {
            window.dashboardState.logs.loading = false;
        }
    };

    // ===== Render Logs =====
    window.renderLogs = function() {
        var state = window.dashboardState;
        var __ = window.__;
        var renderers = window.dashboardRenderers;
        
        // Get filtered logs
        var entries = state.getFilteredLogs();
        
        // Render filter toolbar
        var filterContainer = document.getElementById('log-filter-container');
        if (filterContainer && !filterContainer.innerHTML.trim()) {
            filterContainer.innerHTML = window.dashboardFilters.renderLogToolbar();
            window.dashboardFilters.initLogToolbar();
        }
        
        var container = document.getElementById('log-entries');
        var scrollEl = document.getElementById('log-scroll-container');

        if (entries.length === 0) {
            container.innerHTML = '<div class="text-center text-muted py-4">' + __('index.log.empty') + '</div>';
        } else {
            // Use DocumentFragment for batch DOM insertion
            var fragment = document.createDocumentFragment();
            var outerDiv = document.createElement('div');
            outerDiv.style.cssText = 'font-family:Consolas,monospace;font-size:13px;line-height:1.6;';

            for (var i = 0; i < entries.length; i++) {
                var e = entries[i];
                var ts = new Date(e.timestamp).toLocaleTimeString('zh-CN', { hour12: false });
                var logKey = e.timestamp + '|' + e.level + '|' + (e.message || '').substring(0, 80);
                var isExpanded = state.isLogExpanded(logKey);

                // log-item container
                var item = document.createElement('div');
                item.className = 'log-item';
                item.dataset.logKey = logKey;

                // clickable row
                var row = document.createElement('div');
                row.className = 'log-row';
                row.style.cssText = 'display:flex;gap:8px;padding:4px 16px;border-bottom:1px solid #f1f5f9;cursor:pointer;align-items:center;';
                row.onmouseenter = function(){this.style.background='#f8fafc';};
                row.onmouseleave = function(){this.style.background='';};
                row.onclick = function(){ 
                    var key = this.parentNode.dataset.logKey;
                    window.dashboardState.toggleLog(key);
                    window.renderLogs();
                };

                var timeSpan = document.createElement('span');
                timeSpan.style.cssText = 'color:#94a3b8;white-space:nowrap;min-width:80px;';
                timeSpan.textContent = ts;

                var badge = renderers.badge.logLevel(e.level);
                badge.style.fontSize = '11px';
                
                var msgSpan = document.createElement('span');
                msgSpan.style.cssText = 'color:#475569;flex:1;word-break:break-all;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;';
                msgSpan.textContent = e.message || '';

                var arrowSpan = document.createElement('span');
                arrowSpan.className = 'log-arrow';
                arrowSpan.style.cssText = 'color:#94a3b8;font-size:10px;white-space:nowrap;min-width:14px;text-align:center;transition:transform .2s;';
                arrowSpan.textContent = isExpanded ? '\u25BC' : '\u25B6';

                row.appendChild(timeSpan);
                row.appendChild(badge);
                row.appendChild(msgSpan);
                row.appendChild(arrowSpan);

                // expandable detail
                var detail = document.createElement('div');
                detail.className = 'log-detail';
                detail.style.cssText = 'display:' + (isExpanded ? 'block' : 'none') + ';padding:8px 16px 12px;background:#f8fafc;border-bottom:1px solid #e2e8f0;font-size:12px;';

                var dtHtml = '<div style="display:flex;flex-wrap:wrap;gap:8px 24px;margin-bottom:6px;">';
                dtHtml += '<span><strong>' + __('index.log.category') + '</strong> ' + window.escapeHtml(e.category || '') + '</span>';
                if (e.routeId) {
                    dtHtml += '<span><strong>RouteId:</strong> <code>' + window.escapeHtml(e.routeId) + '</code></span>';
                }
                if (e.traceId) {
                    dtHtml += '<span><strong>TraceId:</strong> <code>' + window.escapeHtml(e.traceId) + '</code></span>';
                }
                if (e.statusCode) {
                    dtHtml += '<span><strong>Status:</strong> ' + renderers.badge.statusCode(e.statusCode) + '</span>';
                }
                dtHtml += '</div>';
                
                dtHtml += '<div style="color:#334155;margin-bottom:4px;"><strong>' + __('index.log.message') + '</strong><br>';
                dtHtml += '<span style="color:#475569;word-break:break-all;">' + window.escapeHtml(e.message || '') + '</span></div>';
                
                if (e.details) {
                    dtHtml += '<div style="color:#334155;margin-top:6px;"><strong>' + __('index.log.details') + '</strong></div>';
                    // Try to parse as JSON for better display
                    try {
                        var detailsObj = JSON.parse(e.details);
                        dtHtml += renderers.json.collapsible(detailsObj, 'Details JSON', true);
                    } catch (err) {
                        dtHtml += '<pre style="background:#f1f5f9;border:1px solid #cbd5e1;border-radius:4px;padding:8px;margin:4px 0 0;overflow-x:auto;white-space:pre-wrap;word-break:break-all;font-size:12px;color:#334155;line-height:1.6;">';
                        dtHtml += window.escapeHtml(e.details) + '</pre>';
                    }
                }
                if (e.exception) {
                    dtHtml += '<div style="color:#dc2626;margin-top:6px;"><strong>' + __('index.log.exception') + '</strong></div>';
                    dtHtml += '<pre style="background:#fef2f2;border:1px solid #fecaca;border-radius:4px;padding:8px;margin:4px 0 0;overflow-x:auto;white-space:pre-wrap;word-break:break-all;font-size:11px;color:#991b1b;line-height:1.5;">';
                    dtHtml += window.escapeHtml(e.exception) + '</pre>';
                }
                detail.innerHTML = dtHtml;

                item.appendChild(row);
                item.appendChild(detail);
                outerDiv.appendChild(item);
            }

            fragment.appendChild(outerDiv);
            container.innerHTML = '';
            container.appendChild(fragment);
        }

        // Update counts
        var snap = window.dashboardState.logs.data;
        document.getElementById('log-display-count').textContent = entries.length;
        document.getElementById('log-total-count').textContent = snap.length || 0;
        document.getElementById('log-refresh-time').textContent = __('index.log.updated') + window.timeStr();

        // Auto-scroll to top when polling
        if (state.logs.polling) {
            requestAnimationFrame(function() {
                scrollEl.scrollTop = 0;
            });
        }
    };

    // ===== Log Polling Control =====
    window.toggleLogPolling = function() {
        var state = window.dashboardState;
        if (state.logs.polling) {
            window.stopLogPolling();
        } else {
            window.startLogPolling();
        }
    };

    window.startLogPolling = function() {
        var state = window.dashboardState;
        if (state.logs.pollTimer) clearInterval(state.logs.pollTimer);
        state.logs.polling = true;
        updateListeningUI();
        window.loadLogs();
        state.logs.pollTimer = setInterval(window.loadLogs, state.logs.pollInterval);
    };

    window.stopLogPolling = function() {
        var state = window.dashboardState;
        if (state.logs.pollTimer) {
            clearInterval(state.logs.pollTimer);
            state.logs.pollTimer = null;
        }
        state.logs.polling = false;
        updateListeningUI();
    };

    window.toggleListening = function() {
        window.toggleLogPolling();
    };

    window.clearLogsConfirm = function() {
        if (!confirm('Are you sure you want to clear all logs?')) return;
        window.clearLogs();
    };

    window.clearLogs = function() {
        window.dashboardApiMethods.clearLogs()
            .then(function(result) {
                window.dashboardState.logs.data = [];
                window.renderLogs();
                window.dashboardRenderers.ui.toast('Logs cleared', 'success');
            })
            .catch(function(err) {
                console.error('Clear logs failed:', err);
                window.dashboardRenderers.ui.toast('Failed to clear logs', 'error');
            });
    };

    function updateListeningUI() {
        var btn = document.getElementById('log-listen-btn');
        if (!btn) return;
        var __ = window.__;
        var state = window.dashboardState;
        if (state.logs.polling) {
            btn.className = 'btn btn-sm btn-outline-warning';
            btn.innerHTML = '<i class="bi bi-pause-circle me-1"></i>' + __('index.log.stopListen');
            var statusEl = document.getElementById('log-polling-status');
            if (statusEl) statusEl.textContent = 'Auto-Poll: ON';
        } else {
            btn.className = 'btn btn-sm btn-outline-success';
            btn.innerHTML = '<i class="bi bi-play-circle me-1"></i>' + __('index.log.startListen');
            var statusEl = document.getElementById('log-polling-status');
            if (statusEl) statusEl.textContent = 'Auto-Poll: OFF';
        }
    }
})();
