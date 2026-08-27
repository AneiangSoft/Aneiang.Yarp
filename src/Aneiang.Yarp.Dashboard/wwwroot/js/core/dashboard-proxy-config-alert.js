/**
 * Dashboard Proxy Config Alert — shared helper for the cluster/route list pages.
 *
 * The overview page owns the prominent banner; the list pages use this helper to:
 *   - fetch proxy config apply-errors (read-only, scoped to cluster/route),
 *   - build id→error maps used to stamp per-row warning markers,
 *   - render a slim "N config apply error(s)" hint bar above the table.
 *
 * Data source: GET /api/config/apply-errors → { health, errors:[{routeId,clusterId,message,...}] }
 * Mirrors the overview alert; never writes config.
 */
(function () {
    'use strict';

    var ProxyConfigAlert = {};

    function tt(key, fallback) {
        try {
            return (typeof window.__ === 'function' && window.__(key)) || fallback;
        } catch (e) {
            return fallback;
        }
    }

    function buildById(errors, keyFn) {
        var map = {};
        (errors || []).forEach(function (e) {
            if (!e) return;
            var id = keyFn(e);
            // Keep the first occurrence per id so the marker shows the earliest
            // known failure for that resource.
            if (id && !map[id]) map[id] = e;
        });
        return map;
    }

    function keyFnFor(scope) {
        if (scope === 'cluster') return function (e) { return e.clusterId; };
        if (scope === 'route') return function (e) { return e.routeId; };
        return function (e) { return e.clusterId || e.routeId; };
    }

    /**
     * Load proxy config errors, optionally scoped to 'cluster' or 'route'.
     * Silent (no loading spinner) so it can run in parallel with table data loads.
     * Resolves to { health, errorsCount, errors, byId, failed }.
     */
    ProxyConfigAlert.load = async function (scope) {
        try {
            var params = scope ? { scope: scope } : undefined;
            var data = await window.DashboardApi.endpoints.getProxyConfigErrors(params, { silent: true });
            var health = (data && data.health) || { status: 'healthy', errorCount: 0 };
            var errors = (data && data.errors) || [];
            return {
                health: health,
                errorCount: (health && health.errorCount) || 0,
                errors: errors,
                byId: buildById(errors, keyFnFor(scope)),
                failed: false
            };
        } catch (err) {
            // Never let the alert helper break the list rendering.
            return { health: { status: 'healthy', errorCount: 0 }, errorCount: 0, errors: [], byId: {}, failed: true };
        }
    };

    /**
     * Render (or hide) a slim hint bar inside `containerEl` based on `health`.
     * @param {HTMLElement} containerEl - the empty placeholder element.
     * @param {object} health - { status, errorCount } from the store/snapshot.
     * @param {object} options - { onRefresh: async fn }
     */
    ProxyConfigAlert.renderHintBar = function (containerEl, health, options) {
        if (!containerEl) return;
        var opts = options || {};
        var isError = health && health.status === 'error';
        var count = (health && health.errorCount) || 0;

        if (!isError || count === 0) {
            containerEl.style.display = 'none';
            containerEl.innerHTML = '';
            return;
        }

        containerEl.className = 'config-alert-bar';
        containerEl.setAttribute('role', 'alert');
        containerEl.style.display = 'flex';
        containerEl.innerHTML =
            '<i class="bi bi-exclamation-triangle-fill"></i>' +
            '<span class="config-alert-text"><strong>' + count + '</strong> ' +
            tt('proxyConfig.hintBarSuffix', 'config apply error(s) — proxy may not be effective') +
            '</span>' +
            '<button type="button" class="config-alert-refresh"><i class="bi bi-arrow-clockwise"></i><span>' +
            tt('proxyConfig.refresh', 'Re-check') + '</span></button>' +
            '<button type="button" class="config-alert-close" aria-label="close">&times;</button>';

        var refreshBtn = containerEl.querySelector('.config-alert-refresh');
        if (refreshBtn && typeof opts.onRefresh === 'function') {
            refreshBtn.addEventListener('click', function () { opts.onRefresh(); });
        }
        var closeBtn = containerEl.querySelector('.config-alert-close');
        if (closeBtn) {
            closeBtn.addEventListener('click', function () { containerEl.style.display = 'none'; });
        }
    };

    /**
     * Create a warning-marker icon element for a row that has a config error.
     */
    ProxyConfigAlert.createMarker = function (error) {
        var icon = document.createElement('i');
        icon.className = 'bi bi-exclamation-triangle-fill config-error-marker';
        var msg = (error && error.message) ? String(error.message) : tt('proxyConfig.rowMark', 'Configuration apply failed');
        icon.title = msg;
        icon.setAttribute('aria-label', msg);
        return icon;
    };

    /**
     * Sync a marker inside a row's name cell (used by diff-rendered rows that
     * are reused across refreshes). Inserts/removes/updates in place.
     * @param {HTMLElement} nameDiv - the flex container holding the name <strong>.
     * @param {object|null} error - error object for this id, or null to clear.
     */
    ProxyConfigAlert.syncMarker = function (nameDiv, error) {
        if (!nameDiv) return;
        var existing = nameDiv.querySelector('.config-error-marker');

        if (!error) {
            if (existing) existing.remove();
            return;
        }

        var msg = (error && error.message) ? String(error.message) : tt('proxyConfig.rowMark', 'Configuration apply failed');
        if (!existing) {
            var marker = ProxyConfigAlert.createMarker(error);
            var strong = nameDiv.querySelector('strong');
            if (strong && strong.nextSibling) {
                nameDiv.insertBefore(marker, strong.nextSibling);
            } else if (strong) {
                nameDiv.appendChild(marker);
            } else {
                nameDiv.appendChild(marker);
            }
        } else {
            existing.title = msg;
            existing.setAttribute('aria-label', msg);
        }
    };

    window.DashboardProxyConfigAlert = ProxyConfigAlert;
})();
