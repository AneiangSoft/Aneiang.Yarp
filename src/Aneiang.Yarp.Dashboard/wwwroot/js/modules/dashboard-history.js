/**
 * Dashboard History Module - configuration timeline, diff and rollback.
 */
(function() {
    'use strict';

    const HistoryModule = {
        name: 'history',
        initialized: false,
        entries: [],
        filtered: [],
        selectedVersionId: null,
        filters: {
            query: '',
            type: '',
            sort: 'desc'
        },

        init: async function() {
            if (this.initialized) return;
            this.setupEvents();
            this.initialized = true;
        },

        loadHistory: async function() {
            const container = this.el('history-content');
            try {
                if (!container) return;
                container.innerHTML = this.renderLoading();

                const data = await window.DashboardApi.endpoints.getHistory();
                this.entries = Array.isArray(data) ? data.map((entry, index) => this.normalizeEntry(entry, index)) : [];
                this.applyFilters();
                this.renderStats();
                this.renderHistory();
                this.selectInitialEntry();
            } catch (error) {
                console.error('[History] Load failed:', error);
                if (container) container.innerHTML = this.renderError(error);
            }
        },

        normalizeEntry: function(entry, index) {
            const routeCount = Number(entry.routeCount ?? entry.RouteCount ?? 0);
            const clusterCount = Number(entry.clusterCount ?? entry.ClusterCount ?? 0);
            const versionId = entry.versionId || entry.VersionId || '';
            const description = entry.description || entry.Description || '';
            const clientIp = entry.clientIp || entry.ClientIp || '';
            const timestamp = entry.timestamp || entry.Timestamp || null;
            const changeType = entry.changeType || entry.ChangeType || this.inferType(description);
            return {
                versionId,
                shortId: versionId ? versionId.substring(0, 10) : '-',
                timestamp,
                timestampMs: timestamp ? new Date(timestamp).getTime() : 0,
                description,
                clientIp,
                routeCount,
                clusterCount,
                totalItems: Number(entry.totalItems ?? entry.TotalItems ?? routeCount + clusterCount),
                configSize: Number(entry.configSize ?? entry.ConfigSize ?? 0),
                isLatest: Boolean(entry.isLatest ?? entry.IsLatest ?? index === 0),
                changeType
            };
        },

        applyFilters: function() {
            const query = this.filters.query.trim().toLowerCase();
            let rows = this.entries.filter(entry => {
                const matchesType = !this.filters.type || entry.changeType === this.filters.type;
                const searchable = [entry.versionId, entry.description, entry.clientIp, entry.changeType].join(' ').toLowerCase();
                const matchesQuery = !query || searchable.includes(query);
                return matchesType && matchesQuery;
            });

            rows = rows.sort((a, b) => this.filters.sort === 'asc'
                ? a.timestampMs - b.timestampMs
                : b.timestampMs - a.timestampMs);

            this.filtered = rows;
        },

        renderStats: function() {
            const latest = this.entries.slice().sort((a, b) => b.timestampMs - a.timestampMs)[0];
            this.setText('history-stat-total', String(this.entries.length));
            this.setText('history-stat-latest', latest ? this.relativeTime(latest.timestamp) : '--');
            this.setText('history-stat-routes', latest ? String(latest.routeCount) : '--');
            this.setText('history-stat-clusters', latest ? String(latest.clusterCount) : '--');
        },

        renderHistory: function() {
            const container = this.el('history-content');
            if (!container) return;

            if (this.entries.length === 0) {
                container.innerHTML = this.renderEmpty(__('history.noSnapshot'), __('history.noSnapshotHint'));
                this.renderDetail(null);
                return;
            }

            if (this.filtered.length === 0) {
                container.innerHTML = this.renderEmpty(__('history.noMatch'), __('history.noMatchHint'));
                this.renderDetail(null);
                return;
            }

            container.innerHTML = '<div class="history-timeline">' + this.filtered.map(entry => this.renderEntry(entry)).join('') + '</div>';
        },

        renderEntry: function(entry) {
            const active = entry.versionId === this.selectedVersionId ? ' active' : '';
            return `
                <div class="history-item" data-version="${this.escape(entry.versionId)}">
                    <div class="history-dot ${this.escape(entry.changeType)}"><i class="bi ${this.iconForType(entry.changeType)}"></i></div>
                    <div class="history-entry-card${active}" data-action="select" data-version="${this.escape(entry.versionId)}">
                        <div class="d-flex justify-content-between gap-3 align-items-start mb-2">
                            <div class="min-w-0">
                                <div class="d-flex flex-wrap gap-2 align-items-center mb-1">
                                    ${this.badgeForType(entry.changeType)}
                                    ${entry.isLatest ? '<span class="badge bg-success">' + __('history.currentLatest') + '</span>' : ''}
                                </div>
                                <h6 class="mb-1">${this.escape(entry.description || __('history.unnamedSnapshot'))}</h6>
                                <div class="history-version">${this.escape(entry.versionId)}</div>
                            </div>
                            <div class="text-end text-muted small text-nowrap">
                                <div>${this.formatDate(entry.timestamp)}</div>
                                <div>${this.relativeTime(entry.timestamp)}</div>
                            </div>
                        </div>
                        <div class="history-meta mb-3">
                            <span><i class="bi bi-signpost-split me-1"></i>${entry.routeCount} ${__('history.routes')}</span>
                            <span><i class="bi bi-hdd-network me-1"></i>${entry.clusterCount} ${__('history.clusters')}</span>
                            <span><i class="bi bi-pc-display me-1"></i>${this.escape(entry.clientIp || '-')}</span>
                            <span><i class="bi bi-database me-1"></i>${this.formatBytes(entry.configSize)}</span>
                        </div>
                        <div class="history-action-bar">
                            <button class="btn btn-sm btn-outline-secondary" data-action="copy" data-version="${this.escape(entry.versionId)}"><i class="bi bi-copy me-1"></i>${__('history.copyVersionId')}</button>
                            <button class="btn btn-sm btn-outline-info" data-action="diff" data-version="${this.escape(entry.versionId)}"><i class="bi bi-file-diff me-1"></i>${__('history.diff')}</button>
                            ${entry.isLatest ? '' : '<button class="btn btn-sm btn-outline-warning" data-action="rollback" data-version="' + this.escape(entry.versionId) + '"><i class="bi bi-arrow-counterclockwise me-1"></i>' + __('history.rollbackShort') + '</button>'}
                        </div>
                    </div>
                </div>`;
        },

        renderDetail: function(entry) {
            const detail = this.el('history-detail');
            if (!detail) return;

            if (!entry) {
                detail.innerHTML = `
                    <div class="history-detail-header">
                        <div class="small opacity-75">${__('history.detail.title')}</div>
                        <h5 class="mb-0">${__('history.detail.select')}</h5>
                    </div>
                    <div class="history-detail-body text-muted">${__('history.detail.empty')}</div>`;
                return;
            }

            detail.innerHTML = `
                <div class="history-detail-header">
                    <div class="d-flex justify-content-between gap-2 align-items-start">
                        <div>
                            <div class="small opacity-75">${this.escape(this.typeLabel(entry.changeType))}</div>
                            <h5 class="mb-0">${this.escape(entry.description || __('history.unnamedSnapshot'))}</h5>
                        </div>
                        ${entry.isLatest ? '<span class="badge bg-success">' + __('history.latest') + '</span>' : ''}
                    </div>
                </div>
                <div class="history-detail-body">
                    <div class="mb-3">
                        <div class="text-muted small mb-1">${__('history.versionId')}</div>
                        <code class="d-block p-2 rounded" style="background:#f8fafc;word-break:break-all;">${this.escape(entry.versionId)}</code>
                    </div>
                    <div class="row g-2 mb-3">
                        <div class="col-6"><div class="border rounded p-2"><div class="text-muted small">${__('history.routes')}</div><div class="fw-bold fs-5">${entry.routeCount}</div></div></div>
                        <div class="col-6"><div class="border rounded p-2"><div class="text-muted small">${__('history.clusters')}</div><div class="fw-bold fs-5">${entry.clusterCount}</div></div></div>
                    </div>
                    <div class="small mb-3">
                        <div class="d-flex justify-content-between py-2 border-bottom"><span class="text-muted">${__('history.createdTime')}</span><span>${this.formatDate(entry.timestamp)}</span></div>
                        <div class="d-flex justify-content-between py-2 border-bottom"><span class="text-muted">${__('history.sourceIp')}</span><span>${this.escape(entry.clientIp || '-')}</span></div>
                        <div class="d-flex justify-content-between py-2 border-bottom"><span class="text-muted">${__('history.configSize')}</span><span>${this.formatBytes(entry.configSize)}</span></div>
                    </div>
                    <div class="d-grid gap-2">
                        <button class="btn btn-outline-secondary" data-action="copy" data-version="${this.escape(entry.versionId)}"><i class="bi bi-copy me-1"></i>${__('history.copyVersionId')}</button>
                        <button class="btn btn-outline-info" data-action="diff" data-version="${this.escape(entry.versionId)}"><i class="bi bi-file-diff me-1"></i>${__('history.viewDiff')}</button>
                        ${entry.isLatest ? '' : '<button class="btn btn-warning" data-action="rollback" data-version="' + this.escape(entry.versionId) + '"><i class="bi bi-arrow-counterclockwise me-1"></i>' + __('history.rollbackToVersion') + '</button>'}
                    </div>
                </div>`;
        },

        selectInitialEntry: function() {
            const selected = this.entries.find(e => e.versionId === this.selectedVersionId) || this.filtered[0] || this.entries[0];
            this.selectedVersionId = selected ? selected.versionId : null;
            this.renderDetail(selected || null);
            this.markActive();
        },

        selectEntry: function(versionId) {
            this.selectedVersionId = versionId;
            const entry = this.entries.find(e => e.versionId === versionId);
            this.renderDetail(entry || null);
            this.markActive();
        },

        markActive: function() {
            document.querySelectorAll('.history-entry-card').forEach(card => {
                card.classList.toggle('active', card.getAttribute('data-version') === this.selectedVersionId);
            });
        },

        rollback: async function(versionId) {
            const entry = this.entries.find(e => e.versionId === versionId);
            const label = entry ? `${entry.shortId} - ${entry.description || ''}` : versionId;
            window.DashboardModals.showConfirm(__('history.rollbackConfirm').replace('{id}', label), async function() {
                try {
                    await window.DashboardApi.endpoints.rollbackConfig(entry.versionId);
                    window.DashboardModals.showSuccess(__('history.rollbackSuccess'));
                    setTimeout(() => HistoryModule.loadHistory(), 500);
                } catch (e) { window.DashboardModals.showError(__('history.rollbackFailed')); }
            }, null, { danger: true, confirmText: __('history.rollback') });
        },

        createSnapshot: async function() {
            const description = prompt(__('history.snapshotPrompt'), __('history.snapshotPrompt') + ': ' + new Date().toLocaleString());
            if (description === null) return;

            try {
                await window.DashboardApi.endpoints.createSnapshot(description || 'Manual snapshot');
                this.toast(__('history.snapshotCreated'), 'success');
                this.loadHistory();
            } catch (error) {
                console.error('[History] Snapshot failed:', error);
                this.toast(__('history.snapshotFailed') + ': ' + (error.message || ''), 'error');
            }
        },

        clearHistory: async function() {
            if (this.entries.length === 0) {
                this.toast(__('history.noRecordsToClear'), 'info');
                return;
            }

            window.DashboardModals.showConfirm(__('history.clearAllConfirm'), async function() {
                try {
                    await window.DashboardApi.endpoints.clearConfigHistory();
                    window.DashboardModals.showSuccess(__('history.clearAllSuccess'));
                    setTimeout(() => HistoryModule.loadHistory(), 500);
                } catch (e) { window.DashboardModals.showError(__('history.clearAllFailed')); }
            }, null, { danger: true });
        },

        copyVersionId: function(versionId) {
            if (window.DashboardUtils?.copyToClipboard) {
                window.DashboardUtils.copyToClipboard(versionId).then(success => {
                    if (success) this.toast(__('history.versionIdCopied'), 'success');
                });
                return;
            }
            navigator.clipboard?.writeText(versionId);
            this.toast(__('history.versionIdCopied'), 'success');
        },

        showDiff: async function(versionId) {
            try {
                const data = await window.DashboardApi.endpoints.configDiff(versionId);
                if (window.DashboardDiffPanel) {
                    window.DashboardDiffPanel.showStructured(data, {
                        title: __('history.configDiff') + ' (' + versionId.substring(0, 10) + ')',
                        summary: data.summary
                    });
                } else {
                    this.toast(__('history.diffPanelNotLoaded'), 'error');
                }
            } catch (error) {
                console.error('[History] Diff failed:', error);
                this.toast(__('history.diffLoadFailed') + ': ' + (error.message || ''), 'error');
            }
        },

        setupEvents: function() {
            document.addEventListener('dashboard:shortcut:refresh', () => this.loadHistory());

            document.addEventListener('click', (event) => {
                const target = event.target.closest('[data-action]');
                if (!target) return;
                const action = target.getAttribute('data-action');
                const versionId = target.getAttribute('data-version');

                if (action === 'select' && versionId) this.selectEntry(versionId);
                if (action === 'copy' && versionId) this.copyVersionId(versionId);
                if (action === 'diff' && versionId) this.showDiff(versionId);
                if (action === 'rollback' && versionId) this.rollback(versionId);
            });

            this.bindInput('history-search', value => { this.filters.query = value; this.refreshView(); });
            this.bindInput('history-type-filter', value => { this.filters.type = value; this.refreshView(); });
            this.bindInput('history-sort', value => { this.filters.sort = value || 'desc'; this.refreshView(); });

            const refresh = this.el('history-refresh');
            if (refresh) refresh.addEventListener('click', () => this.loadHistory());

            const create = this.el('history-create-snapshot');
            if (create) create.addEventListener('click', () => this.createSnapshot());

            const clearAll = this.el('history-clear-all');
            if (clearAll) clearAll.addEventListener('click', () => this.clearHistory());

            const clear = this.el('history-clear-filter');
            if (clear) clear.addEventListener('click', () => {
                this.filters = { query: '', type: '', sort: 'desc' };
                this.setValue('history-search', '');
                this.setValue('history-type-filter', '');
                this.setValue('history-sort', 'desc');
                this.refreshView();
            });
        },

        refreshView: function() {
            this.applyFilters();
            this.renderHistory();
            this.selectInitialEntry();
        },

        bindInput: function(id, handler) {
            const el = this.el(id);
            if (!el) return;
            const eventName = el.tagName === 'SELECT' ? 'change' : 'input';
            el.addEventListener(eventName, () => handler(el.value));
        },

        renderLoading: function() {
            return `<div class="history-empty-state"><div class="spinner-border text-primary mb-3"></div><div class="text-muted">${__('history.loadingMsg')}</div></div>`;
        },

        renderError: function(error) {
            return `<div class="history-empty-state"><i class="bi bi-exclamation-triangle text-danger" style="font-size:42px;"></i><h5 class="mt-3">${__('loading.loadFailed')}</h5><p class="text-muted">${this.escape(error.message || 'Unknown error')}</p><button class="btn btn-outline-primary" onclick="HistoryModule.loadHistory()">${__('loading.retry')}</button></div>`;
        },

        renderEmpty: function(title, message) {
            return `<div class="history-empty-state"><i class="bi bi-clock-history text-muted" style="font-size:52px;"></i><h5 class="mt-3">${this.escape(title)}</h5><p class="text-muted mb-0">${this.escape(message)}</p></div>`;
        },

        inferType: function(description) {
            const text = String(description || '').toLowerCase();
            if (text.includes('rollback')) return 'rollback';
            if (text.includes('import')) return 'import';
            if (text.includes('deleted') || text.includes('remove')) return 'delete';
            if (text.includes('renamed')) return 'rename';
            if (text.includes('saved') || text.includes('update')) return 'update';
            return 'manual';
        },

        iconForType: function(type) {
            return ({ update: 'bi-pencil-square', delete: 'bi-trash3', rollback: 'bi-arrow-counterclockwise', import: 'bi-box-arrow-in-down', rename: 'bi-input-cursor-text', manual: 'bi-camera' })[type] || 'bi-camera';
        },

        typeLabel: function(type) {
            const labels = {
                update: __('history.type.update'),
                delete: __('history.type.delete'),
                rollback: __('history.type.rollback'),
                import: __('history.type.import'),
                rename: __('history.type.rename'),
                manual: __('history.type.manual')
            };
            return labels[type] || __('history.type.manual');
        },

        badgeForType: function(type) {
            const classes = { update: 'bg-primary', delete: 'bg-danger', rollback: 'bg-warning text-dark', import: 'bg-info text-dark', rename: 'bg-secondary', manual: 'bg-success' };
            return `<span class="badge ${classes[type] || 'bg-secondary'}">${this.escape(this.typeLabel(type))}</span>`;
        },

        formatDate: function(value) {
            if (!value) return '-';
            if (window.DashboardI18n?.formatDate) return window.DashboardI18n.formatDate(value);
            return new Date(value).toLocaleString();
        },

        relativeTime: function(value) {
            if (!value) return '--';
            const seconds = Math.max(0, Math.floor((Date.now() - new Date(value).getTime()) / 1000));
            if (seconds < 60) return seconds + __('index.time.secondsAgo');
            const minutes = Math.floor(seconds / 60);
            if (minutes < 60) return minutes + __('index.time.minutesAgo');
            const hours = Math.floor(minutes / 60);
            if (hours < 24) return hours + __('index.time.hoursAgo');
            return Math.floor(hours / 24) + __('index.time.daysAgo');
        },

        formatBytes: function(bytes) {
            if (!bytes) return '0 B';
            if (bytes < 1024) return bytes + ' B';
            if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB';
            return (bytes / 1024 / 1024).toFixed(1) + ' MB';
        },

        toast: function(message, type) {
            if (window.DashboardModals) {
                if (type === 'success') return window.DashboardModals.showSuccess(message);
                if (type === 'error') return window.DashboardModals.showError(message);
                return window.DashboardModals.showInfo(message);
            }
        },

        el: function(id) { return document.getElementById(id); },
        setText: function(id, value) { const el = this.el(id); if (el) el.textContent = value; },
        setValue: function(id, value) { const el = this.el(id); if (el) el.value = value; },
        escape: function(value) {
            if (window.DashboardUtils?.escapeHtml) return window.DashboardUtils.escapeHtml(value == null ? '' : String(value));
            return String(value == null ? '' : value).replace(/[&<>"']/g, m => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[m]));
        }
    };

    if (window.DashboardApp) {
        window.DashboardApp.registerModule('history', HistoryModule);
    }
    window.HistoryModule = HistoryModule;
})();
