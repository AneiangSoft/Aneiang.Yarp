/**
 * DashboardLoading - Global loading indicator.
 * Provides a top progress bar + corner badge that activates whenever any API request is in flight.
 * Activates only after a short threshold (default 300ms) to avoid flashing for fast responses.
 * Used by DashboardApi.request automatically and by buttons via setButtonLoading().
 */
(function() {
    'use strict';

    window.DashboardLoading = window.DashboardLoading || {};

    // Configuration
    const THRESHOLD_MS = 300;          // delay before showing indicator (avoid flash)
    const SLOW_WARNING_MS = 3000;     // "this is taking longer than usual" warning (3s)
    const VERY_SLOW_WARNING_MS = 10000; // "network may be slow" with retry button (10s)
    const MIN_VISIBLE_MS = 400;       // minimum time the indicator stays visible (avoid flicker)

    // State
    let activeRequests = 0;
    let showTimer = null;
    let hideTimer = null;
    let slowTimer = null;
    let verySlowTimer = null;
    let isVisible = false;
    let barEl = null;
    let badgeEl = null;
    let slowBannerEl = null;
    let networkBannerEl = null;
    let tableProgressEls = new Map();    // container → progress element
    let isOffline = false;

    function createUi() {
        if (barEl) return;

        // Top progress bar (3px, fixed)
        barEl = document.createElement('div');
        barEl.id = 'dashboard-global-loading-bar';
        barEl.setAttribute('role', 'progressbar');
        barEl.setAttribute('aria-label', 'Loading');
        barEl.style.cssText = [
            'position:fixed', 'top:0', 'left:0', 'right:0', 'height:3px', 'z-index:99999',
            'background:transparent', 'pointer-events:none', 'overflow:hidden',
            'transition:opacity 200ms ease', 'opacity:0'
        ].join(';');

        const fill = document.createElement('div');
        fill.style.cssText = [
            'height:100%', 'width:100%', 'background:linear-gradient(90deg,#6366f1 0%,#8b5cf6 50%,#6366f1 100%)',
            'background-size:200% 100%', 'transform:translateX(-100%)',
            'animation:dashboard-loading-bar 1.4s ease-in-out infinite',
            'box-shadow:0 0 6px rgba(99,102,241,0.6)'
        ].join(';');
        barEl.appendChild(fill);

        // Corner badge (spinner + count) - top right
        badgeEl = document.createElement('div');
        badgeEl.id = 'dashboard-global-loading-badge';
        badgeEl.setAttribute('aria-live', 'polite');
        badgeEl.style.cssText = [
            'position:fixed', 'top:14px', 'right:14px', 'z-index:99998',
            'display:flex', 'align-items:center', 'gap:8px',
            'padding:6px 12px', 'border-radius:999px',
            'background:rgba(99,102,241,0.95)', 'color:#fff',
            'font-size:12px', 'font-weight:500',
            'box-shadow:0 4px 12px rgba(0,0,0,0.15)',
            'transition:opacity 200ms ease, transform 200ms ease',
            'opacity:0', 'transform:translateY(-8px)', 'pointer-events:none'
        ].join(';');
        badgeEl.innerHTML = '<span class="spinner-border spinner-border-sm" role="status" style="width:12px;height:12px;border-width:2px;"></span>' +
            '<span class="dashboard-loading-text">Loading</span>';

        // Slow request warning banner (top center)
        slowBannerEl = document.createElement('div');
        slowBannerEl.id = 'dashboard-global-loading-slow';
        slowBannerEl.style.cssText = [
            'position:fixed', 'top:14px', 'left:50%', 'transform:translateX(-50%)', 'z-index:99997',
            'padding:8px 16px', 'border-radius:8px',
            'background:rgba(245,158,11,0.95)', 'color:#fff',
            'font-size:13px', 'font-weight:500',
            'box-shadow:0 4px 12px rgba(0,0,0,0.15)',
            'display:none', 'max-width:80%', 'text-align:center'
        ].join(';');
        slowBannerEl.textContent = '请求耗时较长，请耐心等待...';

        document.body.appendChild(barEl);
        document.body.appendChild(badgeEl);
        document.body.appendChild(slowBannerEl);

        // Inject keyframe animation once
        if (!document.getElementById('dashboard-loading-keyframes')) {
            const style = document.createElement('style');
            style.id = 'dashboard-loading-keyframes';
            style.textContent = '@keyframes dashboard-loading-bar{0%{transform:translateX(-100%)}100%{transform:translateX(100%)}}';
            document.head.appendChild(style);
        }
    }

    function show() {
        if (isVisible) return;
        isVisible = true;
        createUi();
        barEl.style.opacity = '1';
        badgeEl.style.opacity = '1';
        badgeEl.style.transform = 'translateY(0)';
        // Show "slow" warning after 3s
        clearTimeout(slowTimer);
        clearTimeout(verySlowTimer);
        slowTimer = setTimeout(function() {
            if (!isVisible) return;
            slowBannerEl.style.display = 'block';
            slowBannerEl.innerHTML = '<i class="bi bi-clock-history me-1"></i> 请求耗时较长，请耐心等待...';
            slowBannerEl.style.background = 'rgba(245,158,11,0.95)';
            slowBannerEl.style.cursor = 'default';
            // Escalate to "very slow" with retry at 10s
            verySlowTimer = setTimeout(function() {
                if (!isVisible) return;
                slowBannerEl.innerHTML =
                    '<i class="bi bi-wifi-off me-1"></i> 网络可能有问题，仍在等待响应... ' +
                    '<button id="dashboard-network-retry-btn" type="button" ' +
                    'style="margin-left:10px;padding:2px 10px;border:1px solid #fff;border-radius:4px;background:transparent;color:#fff;font-size:12px;cursor:pointer;">' +
                    '检查网络</button>';
                slowBannerEl.style.background = 'rgba(220,38,38,0.95)';
                var retryBtn = document.getElementById('dashboard-network-retry-btn');
                if (retryBtn) {
                    retryBtn.addEventListener('click', function() {
                        if (window.DashboardModals) {
                            window.DashboardModals.showInfo
                                ? window.DashboardModals.showInfo('请检查浏览器网络连接；如网络正常，可稍候片刻等待响应或刷新页面。')
                                : alert('请检查浏览器网络连接；如网络正常，可稍候片刻等待响应或刷新页面。');
                        }
                    });
                }
            }, VERY_SLOW_WARNING_MS - SLOW_WARNING_MS);
        }, SLOW_WARNING_MS);
    }

    function hide() {
        isVisible = false;
        if (barEl) barEl.style.opacity = '0';
        if (badgeEl) {
            badgeEl.style.opacity = '0';
            badgeEl.style.transform = 'translateY(-8px)';
        }
        if (slowBannerEl) {
            slowBannerEl.style.display = 'none';
            slowBannerEl.style.cursor = 'default';
        }
        clearTimeout(slowTimer);
        clearTimeout(verySlowTimer);
    }

    function updateBadge() {
        if (!badgeEl) return;
        var textEl = badgeEl.querySelector('.dashboard-loading-text');
        if (activeRequests <= 0) {
            textEl.textContent = '';
        } else if (activeRequests === 1) {
            textEl.textContent = 'Loading';
        } else {
            textEl.textContent = 'Loading × ' + activeRequests;
        }
    }

    /**
     * Called automatically by DashboardApi. Increments/decrements active request count,
     * and after THRESHOLD_MS shows the indicator; hides it once all requests complete
     * (kept visible at least MIN_VISIBLE_MS to avoid flicker).
     */
    window.DashboardLoading.begin = function() {
        activeRequests++;
        updateBadge();
        clearTimeout(hideTimer);
        clearTimeout(showTimer);
        if (activeRequests === 1) {
            showTimer = setTimeout(show, THRESHOLD_MS);
        } else {
            // Already shown or about to show
            show();
        }
    };

    window.DashboardLoading.end = function() {
        activeRequests = Math.max(0, activeRequests - 1);
        updateBadge();
        if (activeRequests === 0) {
            clearTimeout(showTimer);
            clearTimeout(hideTimer);
            if (isVisible) {
                // Keep visible briefly to avoid flicker
                hideTimer = setTimeout(hide, MIN_VISIBLE_MS);
            } else {
                hide();
            }
        }
    };

    /**
     * Wraps an async operation to ensure begin/end pairing even on error.
     */
    window.DashboardLoading.wrap = async function(fn) {
        this.begin();
        try {
            return await fn();
        } finally {
            this.end();
        }
    };

    /**
     * Manually show a page-blocking overlay with a message (for non-API operations like
     * local computation). Pass null to hide.
     */
    let overlayEl = null;
    window.DashboardLoading.overlay = function(message) {
        if (message === null || message === undefined) {
            if (overlayEl) { overlayEl.remove(); overlayEl = null; }
            return;
        }
        if (overlayEl) {
            overlayEl.querySelector('.dashboard-loading-overlay-text').textContent = message;
            return;
        }
        overlayEl = document.createElement('div');
        overlayEl.style.cssText = [
            'position:fixed', 'inset:0', 'z-index:100000',
            'background:rgba(15,23,42,0.55)', 'backdrop-filter:blur(2px)',
            'display:flex', 'align-items:center', 'justify-content:center',
            'animation:dashboard-loading-fade 200ms ease'
        ].join(';');
        overlayEl.innerHTML = '<div style="background:#fff;padding:24px 32px;border-radius:12px;box-shadow:0 20px 50px rgba(0,0,0,0.3);display:flex;align-items:center;gap:14px;max-width:min(90vw,420px);">' +
            '<div class="spinner-border text-primary" role="status" style="width:28px;height:28px;flex-shrink:0;"></div>' +
            '<div class="dashboard-loading-overlay-text" style="color:#334155;font-size:14px;font-weight:500;line-height:1.5;"></div>' +
            '</div>';
        overlayEl.querySelector('.dashboard-loading-overlay-text').textContent = message;
        document.body.appendChild(overlayEl);

        if (!document.getElementById('dashboard-loading-fade-keyframe')) {
            const s = document.createElement('style');
            s.id = 'dashboard-loading-fade-keyframe';
            s.textContent = '@keyframes dashboard-loading-fade{from{opacity:0}to{opacity:1}}';
            document.head.appendChild(s);
        }
    };

    // ── Button-level loading helper ─────────────────────────────────────

    /**
     * Sets a button into a loading state (spinner + text) and disables it.
     * Returns a function that restores the original state.
     *
     * @param {HTMLElement|string} btn - Button element or selector
     * @param {boolean} loading - true to show loading, false to restore
     * @param {string} [text] - Optional loading text. Defaults to "Loading..."
     */
    window.DashboardLoading.setButton = function(btn, loading, text) {
        btn = typeof btn === 'string' ? document.querySelector(btn) : btn;
        if (!btn) return function() {};

        if (!btn._dashboardOriginalState) {
            btn._dashboardOriginalState = {
                html: btn.innerHTML,
                disabled: btn.disabled,
                width: btn.getBoundingClientRect().width
            };
        }

        if (loading) {
            // Lock width to prevent layout shift when replacing content
            if (btn._dashboardOriginalState.width > 0) {
                btn.style.minWidth = btn._dashboardOriginalState.width + 'px';
            }
            btn.disabled = true;
            btn.setAttribute('aria-busy', 'true');
            const loadingText = text || 'Loading...';
            btn.innerHTML = '<span class="spinner-border spinner-border-sm me-2" role="status" aria-hidden="true" style="width:14px;height:14px;border-width:2px;vertical-align:-2px;"></span>' + loadingText;
        } else {
            btn.disabled = btn._dashboardOriginalState.disabled;
            btn.removeAttribute('aria-busy');
            btn.style.minWidth = '';
            btn.innerHTML = btn._dashboardOriginalState.html;
        }
    };

    /**
     * Convenience: runs an async operation while the button shows a loading state.
     * Restores the button even if the operation throws.
     */
    window.DashboardLoading.withButton = async function(btn, text, fn) {
        this.setButton(btn, true, text);
        try {
            return await fn();
        } finally {
            this.setButton(btn, false);
        }
    };

    // ── Table-level progress bar (#1) ──────────────────────────────────

    /**
     * Shows a 2px progress bar overlaid on the top edge of a container (e.g. a table).
     * Pass loading=false to hide. Multiple containers can have their own progress bar simultaneously.
     */
    window.DashboardLoading.tableProgress = function(container, loading) {
        container = typeof container === 'string' ? document.querySelector(container) : container;
        if (!container) return;

        // Make container position:relative if not already, so the bar can position itself
        var computedPosition = window.getComputedStyle(container).position;
        if (computedPosition === 'static') container.style.position = 'relative';

        if (!loading) {
            var existing = tableProgressEls.get(container);
            if (existing) {
                existing.style.opacity = '0';
                setTimeout(function() { if (existing.parentNode) existing.remove(); }, 250);
                tableProgressEls.delete(container);
            }
            return;
        }

        var el = tableProgressEls.get(container);
        if (!el) {
            el = document.createElement('div');
            el.style.cssText = [
                'position:absolute', 'top:0', 'left:0', 'right:0', 'height:2px',
                'background:transparent', 'overflow:hidden', 'z-index:5', 'pointer-events:none',
                'opacity:0', 'transition:opacity 200ms ease'
            ].join(';');
            var fill = document.createElement('div');
            fill.style.cssText = [
                'height:100%', 'width:100%', 'background:linear-gradient(90deg,#6366f1 0%,#8b5cf6 50%,#6366f1 100%)',
                'background-size:200% 100%', 'transform:translateX(-100%)',
                'animation:dashboard-loading-bar 1.4s ease-in-out infinite',
                'box-shadow:0 0 4px rgba(99,102,241,0.5)'
            ].join(';');
            el.appendChild(fill);
            container.appendChild(el);
            tableProgressEls.set(container, el);
        }
        // Trigger transition
        requestAnimationFrame(function() { el.style.opacity = '1'; });
    };

    // ── Retryable error display (#4) ────────────────────────────────────

    /**
     * Renders a styled error block with a retry button inside the given container.
     * Use this instead of DashboardDOM.showError when the failure is recoverable.
     */
    window.DashboardLoading.retryableError = function(container, message, onRetry) {
        container = typeof container === 'string' ? document.querySelector(container) : container;
        if (!container) return;
        if (window.DashboardDOM) window.DashboardDOM.clear(container);

        var wrap = document.createElement('div');
        wrap.style.cssText = 'padding:32px 24px;text-align:center;color:#64748b;';
        wrap.innerHTML =
            '<div style="display:inline-flex;align-items:center;justify-content:center;width:48px;height:48px;border-radius:50%;background:#fee2e2;color:#dc2626;font-size:24px;margin-bottom:12px;">' +
            '<i class="bi bi-exclamation-triangle-fill"></i></div>' +
            '<div style="font-size:14px;font-weight:500;color:#334155;margin-bottom:6px;">加载失败</div>' +
            '<div style="font-size:13px;color:#64748b;margin-bottom:16px;max-width:480px;margin-left:auto;margin-right:auto;"></div>';
        wrap.children[2].textContent = message || '请求失败，请稍后重试。';

        if (typeof onRetry === 'function') {
            var btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'btn btn-sm btn-primary';
            btn.innerHTML = '<i class="bi bi-arrow-clockwise me-1"></i>重试';
            btn.addEventListener('click', function() {
                if (window.DashboardLoading) window.DashboardLoading.setButton(btn, true, '重试中...');
                Promise.resolve()
                    .then(onRetry)
                    .catch(function(e) {
                        if (window.DashboardModals) window.DashboardModals.showError('重试失败：' + (e && e.message ? e.message : e));
                    })
                    .then(function() {
                        if (window.DashboardLoading) window.DashboardLoading.setButton(btn, false);
                    });
            });
            wrap.appendChild(btn);
        }

        container.appendChild(wrap);
    };

    // ── Network status (#3) ─────────────────────────────────────────────

    /**
     * Sets up online/offline event listeners. When the browser goes offline, a persistent
     * banner is shown until connectivity is restored. Safe to call multiple times.
     */
    window.DashboardLoading.initNetworkStatus = function() {
        if (window.DashboardLoading._networkInitialized) return;
        window.DashboardLoading._networkInitialized = true;

        var updateOffline = function() {
            isOffline = !navigator.onLine;
            if (!networkBannerEl) {
                networkBannerEl = document.createElement('div');
                networkBannerEl.style.cssText = [
                    'position:fixed', 'top:0', 'left:0', 'right:0', 'z-index:100001',
                    'background:#dc2626', 'color:#fff', 'text-align:center',
                    'padding:8px 16px', 'font-size:13px', 'font-weight:500',
                    'box-shadow:0 2px 8px rgba(0,0,0,0.15)',
                    'display:none', 'align-items:center', 'justify-content:center', 'gap:8px'
                ].join(';');
                networkBannerEl.innerHTML = '<i class="bi bi-wifi-off"></i><span>网络连接已断开，部分功能将不可用</span>';
                document.body.appendChild(networkBannerEl);
            }
            networkBannerEl.style.display = isOffline ? 'flex' : 'none';
        };

        window.addEventListener('online', updateOffline);
        window.addEventListener('offline', updateOffline);
        updateOffline();
    };

    // ── Import/batch progress (#6) ──────────────────────────────────────

    /**
     * Shows a determinate progress bar with a label (e.g. "导入中... 3 / 10").
     * Reuses the global loading UI but adds a progress strip in the corner badge.
     * @param {number} current - 0..total (0 hides the bar but keeps badge)
     * @param {number} total - total steps
     * @param {string} [label] - custom label, e.g. "导入中"
     */
    window.DashboardLoading.setProgress = function(current, total, label) {
        createUi();
        if (!badgeEl) return;
        var textEl = badgeEl.querySelector('.dashboard-loading-text');
        if (!textEl) return;
        if (current <= 0) {
            textEl.textContent = label || 'Loading';
            return;
        }
        textEl.textContent = (label || '处理中') + ' ' + current + ' / ' + total + ' (' + Math.round(current / total * 100) + '%)';
    };

    /**
     * Wraps a batch operation, showing X/Y progress as it processes each item.
     * @param {Array} items - the items to process
     * @param {string} label - e.g. "导入中"
     * @param {function} processFn - async (item, index) => result
     * @param {number} [concurrency=1] - how many to process in parallel
     * @returns {Promise<Array>} results in input order
     */
    window.DashboardLoading.batchProcess = async function(items, label, processFn, concurrency) {
        concurrency = Math.max(1, concurrency || 1);
        this.begin();
        var results = new Array(items.length);
        var completed = 0;
        var total = items.length;
        try {
            // Simple sequential or limited parallel execution
            var cursor = 0;
            var workers = new Array(Math.min(concurrency, total)).fill(0).map(function worker() {
                return (async function() {
                    while (true) {
                        var idx = cursor++;
                        if (idx >= total) return;
                        results[idx] = await processFn(items[idx], idx);
                        completed++;
                        this.setProgress(completed, total, label);
                    }
                }).call(this);
            });
            await Promise.all(workers);
            return results;
        } finally {
            this.end();
        }
    };
})();
