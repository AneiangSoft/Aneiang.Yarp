/**
 * Dashboard Response Cache Module - Cache statistics and management
 */
(function() {
    'use strict';

    var ResponseCacheModule = {
        name: 'responsecache',
        initialized: false,

        init: async function() {
            if (this.initialized) return;
            this.setupEvents();
            this.initialized = true;
        },

        loadCacheStats: async function() {
            try {
                var container = window.DashboardDOM.safe('#cache-content');
                if (!container) return;

                window.DashboardDOM.showLoading(container, __('cache.loading'));

                var stats = await window.DashboardApi.endpoints.getCacheStats();
                this.renderCacheStats(stats, container);
            } catch (error) {
                console.error('[ResponseCache] Load failed:', error);
                var container = window.DashboardDOM.safe('#cache-content');
                if (container) {
                    if (error.message && error.message.indexOf('404') >= 0) {
                        window.DashboardDOM.showError(container, __('cache.notEnabled'));
                    } else {
                        window.DashboardDOM.showError(container, __('cache.loadFailed'));
                    }
                }
            }
        },

        renderCacheStats: function(stats, container) {
            window.DashboardDOM.clear(container);

            if (!stats) {
                container.innerHTML =
                    '<div class="text-center py-5">' +
                        '<i class="bi bi-database text-muted" style="font-size:48px;"></i>' +
                        '<p class="text-muted mt-3">' + __('cache.noData') + '</p>' +
                    '</div>';
                return;
            }

            var hitRateColor = stats.hitRate > 50 ? '#22c55e' : stats.hitRate > 20 ? '#f59e0b' : '#ef4444';
            var usagePercent = stats.maxEntries > 0 ? Math.round(stats.entryCount / stats.maxEntries * 100) : 0;

            var html = '';

            // Summary cards
            html += '<div class="row g-3 mb-4">';
            html += '<div class="col-md-3 col-sm-6"><div class="stat-mini-card"><div class="stat-mini-value">' + stats.entryCount + '</div><div class="stat-mini-label">' + __('cache.entries') + '</div><div class="stat-mini-sub">' + __('cache.maxEntries') + ': ' + stats.maxEntries + '</div></div></div>';
            html += '<div class="col-md-3 col-sm-6"><div class="stat-mini-card"><div class="stat-mini-value" style="color:' + hitRateColor + '">' + stats.hitRate + '%</div><div class="stat-mini-label">' + __('cache.hitRate') + '</div><div class="stat-mini-sub">' + stats.hits + ' / ' + (stats.hits + stats.misses) + '</div></div></div>';
            html += '<div class="col-md-3 col-sm-6"><div class="stat-mini-card"><div class="stat-mini-value">' + (stats.estimatedSizeMB || 0) + ' MB</div><div class="stat-mini-label">' + __('cache.memoryUsage') + '</div><div class="stat-mini-sub">' + (stats.estimatedSizeBytes || 0).toLocaleString() + ' ' + __('cache.bytes') + '</div></div></div>';
            html += '<div class="col-md-3 col-sm-6"><div class="stat-mini-card"><div class="stat-mini-value">' + stats.hits + ' / ' + stats.misses + '</div><div class="stat-mini-label">' + __('cache.hitsMisses') + '</div><div class="stat-mini-sub">' + __('cache.total') + ': ' + (stats.hits + stats.misses) + '</div></div></div>';
            html += '</div>';

            // Usage bar
            html += '<div class="card-panel mb-3"><div class="card-body">';
            html += '<div class="d-flex justify-content-between align-items-center mb-2">';
            html += '<span class="fw-semibold" style="font-size:13px"><i class="bi bi-pie-chart me-1"></i>' + __('cache.usage') + '</span>';
            html += '<span class="text-muted" style="font-size:12px">' + usagePercent + '%</span>';
            html += '</div>';
            html += '<div class="stats-bar-track"><div class="stats-bar-fill" style="width:' + Math.min(usagePercent, 100) + '%;background:' + (usagePercent > 80 ? '#ef4444' : usagePercent > 50 ? '#f59e0b' : '#6366f1') + '"></div></div>';

            // Hit/Miss bar
            var totalRequests = stats.hits + stats.misses;
            var hitPct = totalRequests > 0 ? Math.round(stats.hits / totalRequests * 100) : 0;
            html += '<div class="d-flex justify-content-between align-items-center mb-2 mt-3">';
            html += '<span class="fw-semibold" style="font-size:13px"><i class="bi bi-bullseye me-1"></i>' + __('cache.hitMissRatio') + '</span>';
            html += '<span class="text-muted" style="font-size:12px">' + __('cache.hits') + ': ' + hitPct + '%</span>';
            html += '</div>';
            html += '<div style="display:flex;height:8px;border-radius:4px;overflow:hidden;background:#f1f5f9;">';
            html += '<div style="width:' + hitPct + '%;background:#22c55e;border-radius:4px 0 0 4px;"></div>';
            html += '<div style="width:' + (100 - hitPct) + '%;background:#ef4444;border-radius:0 4px 4px 0;"></div>';
            html += '</div>';
            html += '<div class="d-flex justify-content-between mt-1" style="font-size:11px;color:#64748b;">';
            html += '<span><span style="display:inline-block;width:8px;height:8px;border-radius:50%;background:#22c55e;margin-right:4px;"></span>' + __('cache.hits') + '</span>';
            html += '<span><span style="display:inline-block;width:8px;height:8px;border-radius:50%;background:#ef4444;margin-right:4px;"></span>' + __('cache.misses') + '</span>';
            html += '</div>';

            html += '</div></div>';

            // Actions
            html += '<div class="card-panel"><div class="card-body d-flex flex-wrap gap-2">';
            html += '<button class="btn btn-sm btn-outline-danger" onclick="window.ResponseCacheModule.clearAll()">';
            html += '<i class="bi bi-trash me-1"></i>' + __('cache.clearAll') + '</button>';
            html += '<button class="btn btn-sm btn-outline-secondary" onclick="window.ResponseCacheModule.loadCacheStats()">';
            html += '<i class="bi bi-arrow-clockwise me-1"></i>' + __('cache.refresh') + '</button>';
            html += '</div></div>';

            container.innerHTML = html;
        },

        clearAll: async function() {
            if (!confirm(__('cache.clearConfirm'))) return;
            try {
                await window.DashboardApi.endpoints.clearCache();
                if (window.DashboardModals) {
                    window.DashboardModals.showSuccess(__('cache.cleared'));
                }
                this.loadCacheStats();
            } catch (e) {
                console.error('[ResponseCache] Clear failed:', e);
                if (window.DashboardModals) {
                    window.DashboardModals.showError(__('cache.clearFailed'));
                }
            }
        },

        setupEvents: function() {
            document.addEventListener('dashboard:shortcut:refresh', function() { ResponseCacheModule.loadCacheStats(); });
            document.addEventListener('dashboard:localeChange', function() { ResponseCacheModule.loadCacheStats(); });
        }
    };

    if (window.DashboardApp) {
        window.DashboardApp.registerModule('responsecache', ResponseCacheModule);
    }
    window.ResponseCacheModule = ResponseCacheModule;
})();
