/**
 * Dashboard Logs Module - Aneiang.Yarp Gateway Dashboard
 * Log data loading, polling, filtering, and UI
 */
(function() {
    'use strict';

    // ===== State =====
    var listeningEnabled = false;
    var logPollTimer = null;

    // ===== Load Log Data =====
    window.loadLogs = async function() {
        try {
            var d = window.__dashboard;
            var res = await window.authFetch(d.basePath + '/logs?count=100');
            var json = await res.json();
            if (json.code !== 200) return;
            var snap = json.data;
            var entries = snap.entries || [];
            var __ = window.__;

            var container = document.getElementById('log-entries');
            var scrollEl = document.getElementById('log-scroll-container');

            // Save expanded state before DOM rebuild
            var expandedKeys = new Set();
            container.querySelectorAll('.log-item').forEach(function(item) {
                var detail = item.querySelector('.log-detail');
                var arrow = item.querySelector('.log-arrow');
                if (detail && arrow && detail.style.display !== 'none') {
                    var k = item.dataset.logKey;
                    if (k) expandedKeys.add(k);
                }
            });

            // Apply gateway-only filter if checked
            var gatewayOnly = document.getElementById('log-gateway-only').checked;
            var renderList = gatewayOnly ? entries.filter(function(e) { return e.category === 'Gateway'; }) : entries;

            if (renderList.length === 0) {
                container.innerHTML = '<div class="text-center text-muted py-4">' + __('index.log.empty') + '</div>';
            } else {
                // Use DocumentFragment for batch DOM insertion
                var fragment = document.createDocumentFragment();
                var outerDiv = document.createElement('div');
                outerDiv.style.cssText = 'font-family:Consolas,monospace;font-size:13px;line-height:1.6;';

                for (var i = 0; i < renderList.length; i++) {
                    var e = renderList[i];
                    var ts = new Date(e.timestamp).toLocaleTimeString('zh-CN', { hour12: false });

                    // log-item container
                    var item = document.createElement('div');
                    item.className = 'log-item';
                    item.dataset.logKey = e.timestamp + '|' + e.level + '|' + (e.message || '').substring(0, 80);

                    // clickable row
                    var row = document.createElement('div');
                    row.className = 'log-row';
                    row.style.cssText = 'display:flex;gap:8px;padding:4px 16px;border-bottom:1px solid #f1f5f9;cursor:pointer;';
                    row.onmouseenter = function(){this.style.background='#f8fafc';};
                    row.onmouseleave = function(){this.style.background='';};
                    row.onclick = function(){ window.toggleLogDetail(this); };

                    var timeSpan = document.createElement('span');
                    timeSpan.style.cssText = 'color:#94a3b8;white-space:nowrap;min-width:80px;';
                    timeSpan.textContent = ts;

                    var badge = levelBadge(e.level);
                    var msgSpan = document.createElement('span');
                    msgSpan.style.cssText = 'color:#475569;flex:1;word-break:break-all;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;';
                    msgSpan.textContent = e.message || '';

                    var arrowSpan = document.createElement('span');
                    arrowSpan.className = 'log-arrow';
                    arrowSpan.style.cssText = 'color:#94a3b8;font-size:10px;white-space:nowrap;min-width:14px;text-align:center;transition:transform .2s;';
                    arrowSpan.textContent = '\u25B6';

                    row.appendChild(timeSpan);
                    row.appendChild(badge);
                    row.appendChild(msgSpan);
                    row.appendChild(arrowSpan);

                    // expandable detail
                    var detail = document.createElement('div');
                    detail.className = 'log-detail';
                    detail.style.cssText = 'display:none;padding:8px 16px 12px;background:#f8fafc;border-bottom:1px solid #e2e8f0;font-size:12px;';

                    var dtHtml = '<div style="color:#64748b;margin-bottom:4px;">';
                    dtHtml += '<strong>' + __('index.log.category') + '</strong> ' + (e.category || '') + '</div>';
                    dtHtml += '<div style="color:#334155;margin-bottom:4px;"><strong>' + __('index.log.message') + '</strong><br>';
                    dtHtml += '<span style="color:#475569;word-break:break-all;">' + window.escapeHtml(e.message || '') + '</span></div>';
                    if (e.details) {
                        dtHtml += '<div style="color:#334155;margin-top:6px;"><strong>' + __('index.log.details') + '</strong></div>';
                        dtHtml += '<pre style="background:#f1f5f9;border:1px solid #cbd5e1;border-radius:4px;padding:8px;margin:4px 0 0;overflow-x:auto;white-space:pre-wrap;word-break:break-all;font-size:12px;color:#334155;line-height:1.6;">';
                        dtHtml += window.escapeHtml(e.details) + '</pre>';
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

                // Restore expanded state
                if (expandedKeys.size > 0) {
                    container.querySelectorAll('.log-item').forEach(function(item) {
                        var k = item.dataset.logKey;
                        if (k && expandedKeys.has(k)) {
                            var detail = item.querySelector('.log-detail');
                            var arrow = item.querySelector('.log-arrow');
                            if (detail) detail.style.display = 'block';
                            if (arrow) arrow.textContent = '\u25BC';
                        }
                    });
                }
            }

            document.getElementById('log-display-count').textContent = renderList.length;
            document.getElementById('log-total-count').textContent = snap.bufferSize || 0;
            document.getElementById('log-refresh-time').textContent = __('index.log.updated') + window.timeStr();

            // Auto-scroll to top when listening
            if (listeningEnabled || document.getElementById('log-gateway-only').checked) {
                requestAnimationFrame(function() {
                    scrollEl.scrollTop = 0;
                });
            }
        } catch (e) { /* silent */ }
    };

    // ===== Toggle Log Detail =====
    window.toggleLogDetail = function(rowEl) {
        var item = rowEl.parentNode;
        var detail = item.querySelector('.log-detail');
        var arrow = rowEl.querySelector('.log-arrow');
        if (!detail || !arrow) return;
        var isOpen = detail.style.display !== 'none';
        detail.style.display = isOpen ? 'none' : 'block';
        arrow.textContent = isOpen ? '\u25B6' : '\u25BC';
        if (!isOpen) {
            stopLogPolling();
        }
    };

    // ===== Log Polling Control =====
    function startLogPolling() {
        if (logPollTimer) clearInterval(logPollTimer);
        listeningEnabled = true;
        updateListeningUI();
        window.loadLogs();
        logPollTimer = setInterval(window.loadLogs, 3000);
    }

    function stopLogPolling() {
        if (logPollTimer) {
            clearInterval(logPollTimer);
            logPollTimer = null;
        }
        listeningEnabled = false;
        updateListeningUI();
    }

    window.toggleListening = function() {
        if (listeningEnabled) {
            stopLogPolling();
        } else {
            startLogPolling();
        }
    };

    window.onGatewayOnlyChange = function() {
        window.loadLogs();
    };

    window.clearLogs = function() {
        var d = window.__dashboard;
        window.authFetch(d.basePath + '/logs', { method: 'DELETE' }).catch(function() {});
        document.getElementById('log-entries').innerHTML = '<div class="text-center text-muted py-4">' + window.__('index.log.empty') + '</div>';
        document.getElementById('log-display-count').textContent = '0';
        document.getElementById('log-total-count').textContent = '0';
    };

    function updateListeningUI() {
        var btn = document.getElementById('log-listen-btn');
        if (!btn) return;
        var __ = window.__;
        if (listeningEnabled) {
            btn.className = 'btn btn-sm btn-outline-warning';
            btn.innerHTML = '<i class="bi bi-pause-circle me-1"></i>' + __('index.log.stopListen');
        } else {
            btn.className = 'btn btn-sm btn-outline-success';
            btn.innerHTML = '<i class="bi bi-play-circle me-1"></i>' + __('index.log.startListen');
        }
    }

    // ===== Level Badge =====
    function levelBadge(level) {
        var l = (level || '').toLowerCase();
        var span = document.createElement('span');
        span.style.cssText = 'padding:1px 8px;border-radius:4px;font-size:11px;font-weight:600;white-space:nowrap;';
        span.textContent = level || '';
        if (l === 'error' || l === 'critical') {
            span.style.background = '#fef2f2';
            span.style.color = '#dc2626';
        } else if (l === 'warning') {
            span.style.background = '#fefce8';
            span.style.color = '#ca8a04';
        } else {
            span.style.background = '#f8fafc';
            span.style.color = '#64748b';
        }
        return span;
    }
})();
