/**
 * Dashboard History Module - Configuration change history with rollback
 */
(function() {
    'use strict';

    const HistoryModule = {
        name: 'history',
        initialized: false,

        init: async function() {
            if (this.initialized) return;
            this.setupEvents();
            this.initialized = true;
        },

        loadHistory: async function() {
            try {
                const container = window.DashboardDOM.safe('#history-content');
                if (!container) return;

                window.DashboardDOM.showLoading(container, __('history.loading'));

                const data = await window.DashboardApi.endpoints.getHistory();

                if (!data || data.length === 0) {
                    window.DashboardDOM.clear(container);
                    container.innerHTML = `
                        <div class="text-center py-5">
                            <i class="bi bi-clock-history text-muted" style="font-size:48px;"></i>
                            <p class="text-muted mt-3">${__('history.empty')}</p>
                        </div>`;
                    return;
                }

                this.renderHistory(data, container);
            } catch (error) {
                console.error('[History] Load failed:', error);
                const container = window.DashboardDOM.safe('#history-content');
                if (container) window.DashboardDOM.showError(container, __('history.loadFailed'));
            }
        },

        renderHistory: function(entries, container) {
            window.DashboardDOM.clear(container);

            const rows = entries.map((entry, idx) => {
                const isLatest = idx === 0;
                const latestBadge = isLatest
                    ? `<span class="badge bg-success" style="font-size:10px">${__('history.latest')}</span>`
                    : '';

                return `<tr>
                    <td>
                        <code style="font-size:12px;cursor:pointer" title="${entry.versionId}" onclick="HistoryModule.copyVersionId('${entry.versionId}')">${entry.versionId}</code>
                        ${latestBadge}
                    </td>
                    <td style="font-size:12px">${window.DashboardI18n.formatDate(entry.timestamp)}</td>
                    <td style="font-size:12px">${window.DashboardUtils.escapeHtml(entry.description || '-')}</td>
                    <td style="font-size:12px">${window.DashboardUtils.escapeHtml(entry.clientIp || '-')}</td>
                    <td>${entry.routeCount != null ? entry.routeCount : '-'} / ${entry.clusterCount != null ? entry.clusterCount : '-'}</td>
                    <td>
                        ${!isLatest ? `<button class="btn btn-sm btn-outline-info me-1" onclick="HistoryModule.showDiff('${entry.versionId}')" title="${__('history.diff')}"><i class="bi bi-file-diff"></i></button>` : ''}
                        ${!isLatest ? `<button class="btn btn-sm btn-outline-warning" onclick="HistoryModule.rollback('${entry.versionId}')" title="${__('history.rollback')}"><i class="bi bi-arrow-counterclockwise"></i></button>` : ''}
                    </td>
                </tr>`;
            }).join('');

            container.innerHTML = `
                <div class="d-flex justify-content-end mb-3">
                    <button class="btn btn-sm btn-outline-primary" onclick="HistoryModule.createSnapshot()">
                        <i class="bi bi-plus-circle me-1"></i>${__('history.manualSnapshot')}
                    </button>
                </div>
                <div class="table-responsive">
                    <table class="table table-hover align-middle table-sm">
                        <thead>
                            <tr>
                                <th style="width:20%">${__('history.version')}</th>
                                <th style="width:18%">${__('history.time')}</th>
                                <th>${__('history.description')}</th>
                                <th style="width:12%">${__('history.clientIp')}</th>
                                <th style="width:12%">${__('history.routes')}/${__('history.clusters')}</th>
                                <th style="width:60px"></th>
                            </tr>
                        </thead>
                        <tbody>${rows}</tbody>
                    </table>
                </div>`;
        },

        rollback: async function(versionId) {
            if (!confirm(__('history.rollbackConfirm', { id: versionId }))) return;

            try {
                await window.DashboardApi.endpoints.rollback(versionId);
                if (window.DashboardModals) window.DashboardModals.showSuccess(__('history.rollbackSuccess'));
                setTimeout(() => this.loadHistory(), 500);
            } catch (error) {
                console.error('[History] Rollback failed:', error);
                if (window.DashboardModals) window.DashboardModals.showError(__('history.rollbackFailed'));
            }
        },

        createSnapshot: async function() {
            try {
                await window.DashboardApi.endpoints.createSnapshot(__('history.manualSnapshot') + ' - ' + new Date().toLocaleString());
                if (window.DashboardModals) window.DashboardModals.showSuccess(__('history.snapshotSuccess'));
                this.loadHistory();
            } catch (error) {
                console.error('[History] Snapshot failed:', error);
                if (window.DashboardModals) window.DashboardModals.showError(__('history.snapshotFailed'));
            }
        },

        copyVersionId: function(versionId) {
            window.DashboardUtils.copyToClipboard(versionId).then((success) => {
                if (success && window.DashboardModals) window.DashboardModals.showSuccess(__('index.copied'));
            });
        },

        showDiff: async function(versionId) {
            try {
                if (window.DashboardModals) window.DashboardModals.showLoading(__('diff.loading'));
                var data = await window.DashboardApi.endpoints.configDiff(versionId);
                if (window.DashboardModals) window.DashboardModals.hideModal();

                if (window.DashboardDiffPanel) {
                    window.DashboardDiffPanel.showStructured(data, {
                        title: __('diff.title') + ' (' + versionId + ')',
                        summary: data.summary
                    });
                } else {
                    if (window.DashboardModals) window.DashboardModals.showError(__('diff.loadFailed'));
                }
            } catch (error) {
                console.error('[History] Diff failed:', error);
                if (window.DashboardModals) {
                    window.DashboardModals.hideModal();
                    window.DashboardModals.showError(__('diff.loadFailed') + ': ' + (error.message || ''));
                }
            }
        },

        setupEvents: function() {
            document.addEventListener('dashboard:shortcut:refresh', () => this.loadHistory());
        }
    };

    if (window.DashboardApp) {
        window.DashboardApp.registerModule('history', HistoryModule);
    }
    window.HistoryModule = HistoryModule;
})();
