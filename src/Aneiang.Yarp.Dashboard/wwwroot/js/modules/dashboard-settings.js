/**
 * Dashboard Settings Module
 * Provides: appearance preferences (locale / sidebar), proxy-log settings (hot-reload),
 * and system information.
 */
(function() {
    'use strict';

    const LOG_FIELDS = [
        { id: 'ls-persistence', key: 'persistenceEnabled', type: 'bool' },
        { id: 'ls-meta-retention', key: 'metaRetentionDays', type: 'int' },
        { id: 'ls-body-retention', key: 'bodyRetentionDays', type: 'int' },
        { id: 'ls-req-body', key: 'requestBodyCaptureEnabled', type: 'bool' },
        { id: 'ls-resp-body', key: 'responseBodyCaptureEnabled', type: 'bool' },
        { id: 'ls-max-body', key: 'maxBodyLength', type: 'int' },
        { id: 'ls-buffer', key: 'maxBodyBufferBytes', type: 'int' },
        { id: 'ls-sampling', key: 'samplingEnabled', type: 'bool' },
        { id: 'ls-sampling-rate', key: 'samplingRate', type: 'double' },
        { id: 'ls-errors-only', key: 'errorsOnly', type: 'bool' },
        { id: 'ls-min-level', key: 'minLogLevel', type: 'string' }
    ];

    function $(id) { return document.getElementById(id); }
    function t(key, fallback) { return window.__ ? window.__(key) : (fallback || key); }

    function getLocale() {
        if (window.__dashboard && window.__dashboard.locale) return window.__dashboard.locale;
        return (window.CURRENT_LOCALE) || 'zh-CN';
    }

    function setLocale(locale) {
        document.cookie = 'dashboard_locale=' + locale + ';path=/;max-age=' + (365 * 86400);
        try { localStorage.setItem('dashboard_locale', locale); } catch (_) {}
        location.reload();
    }

    function formatBytes(bytes) {
        if (bytes == null || isNaN(bytes)) return '-';
        if (bytes < 1024) return bytes + ' B';
        if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB';
        if (bytes < 1024 * 1024 * 1024) return (bytes / 1024 / 1024).toFixed(2) + ' MB';
        return (bytes / 1024 / 1024 / 1024).toFixed(2) + ' GB';
    }

    function renderSystemInfo(data) {
        const container = $('settings-system-content');
        if (!container) return;
        const authModeLabels = {
            'None': t('settings.system.authMode.none', 'None'),
            'ApiKey': 'API Key',
            'DefaultJwt': 'JWT (Admin)',
            'CustomJwt': 'JWT (Custom)'
        };
        const rows = [
            { label: t('settings.system.version', 'Version'), value: window.DashboardUtils && DashboardUtils.escapeHtml ? DashboardUtils.escapeHtml(data.version || '-') : (data.version || '-'), icon: 'bi-tag' },
            { label: t('settings.system.routePrefix', 'Route Prefix'), value: data.routePrefix || '-', icon: 'bi-signpost-split' },
            { label: t('settings.system.authMode', 'Auth Mode'), value: authModeLabels[data.authMode] || data.authMode || '-', icon: 'bi-shield-lock' },
            { label: t('settings.system.databaseFile', 'Database File'), value: data.databaseFile || '-', icon: 'bi-hdd' },
            { label: t('settings.system.databaseSize', 'Database Size'), value: formatBytes(data.databaseSizeBytes), icon: 'bi-database' }
        ];
        container.innerHTML = rows.map(function(r) {
            return '<div class="d-flex align-items-start py-2 border-bottom">' +
                '<i class="bi ' + r.icon + ' me-3 text-muted mt-1"></i>' +
                '<div class="text-muted" style="width:140px;flex-shrink:0;">' + r.label + '</div>' +
                '<div class="text-break fw-medium">' + r.value + '</div>' +
                '</div>';
        }).join('');
    }

    function renderRestartRequired(data) {
        const container = $('settings-restart-required');
        if (!container) return;
        const esc = function(v) {
            return window.DashboardUtils && DashboardUtils.escapeHtml ? DashboardUtils.escapeHtml(v) : String(v);
        };
        const listValue = function(arr) {
            if (!arr || !arr.length) return t('settings.logging.restartRequired.empty', '未配置');
            return esc(arr.join(', '));
        };
        const items = [
            { label: t('settings.logging.restartRequired.bufferCapacity', 'Buffer Capacity'), value: data.bufferCapacity == null ? '-' : String(data.bufferCapacity), icon: 'bi-stack' },
            { label: t('settings.logging.restartRequired.enableAsyncLogging', 'Async Logging'), value: data.enableAsyncLogging ? t('settings.logging.restartRequired.enabled', '启用') : t('settings.logging.restartRequired.disabled', '禁用'), icon: 'bi-lightning-charge' },
            { label: t('settings.logging.restartRequired.headerBlacklist', 'Header Blacklist'), value: listValue(data.headerBlacklist), icon: 'bi-eye-slash' },
            { label: t('settings.logging.restartRequired.queryBlacklist', 'Query Blacklist'), value: listValue(data.queryBlacklist), icon: 'bi-funnel' },
            { label: t('settings.logging.restartRequired.jsonFieldSanitizeList', 'JSON Field Sanitize List'), value: listValue(data.jsonFieldSanitizeList), icon: 'bi-mask' }
        ];
        container.innerHTML = '<div class="row g-2">' + items.map(function(item) {
            return '<div class="col-md-6">' +
                '<div class="border rounded p-2 h-100 bg-light bg-opacity-50">' +
                '<div class="d-flex align-items-center gap-2 text-muted small mb-1">' +
                '<i class="bi ' + item.icon + '"></i><span>' + item.label + '</span>' +
                '</div>' +
                '<div class="text-break fw-medium">' + item.value + '</div>' +
                '</div>' +
                '</div>';
        }).join('') + '</div>';
    }

    function fillLogForm(data) {
        LOG_FIELDS.forEach(function(f) {
            const el = $(f.id);
            if (!el) return;
            if (f.type === 'bool') el.checked = !!data[f.key];
            else el.value = data[f.key];
        });
        const badge = $('settings-customized-badge');
        if (badge) badge.classList.toggle('d-none', !data.isCustomized);
    }

    function collectLogForm() {
        const payload = {};
        LOG_FIELDS.forEach(function(f) {
            const el = $(f.id);
            if (!el) return;
            if (f.type === 'bool') payload[f.key] = !!el.checked;
            else if (f.type === 'int') payload[f.key] = parseInt(el.value, 10) || 0;
            else if (f.type === 'double') payload[f.key] = parseFloat(el.value) || 0;
            else payload[f.key] = el.value;
        });
        return payload;
    }

    function setButtonsDisabled(disabled) {
        const saveBtn = $('settings-save-btn');
        const resetBtn = $('settings-reset-btn');
        if (saveBtn) saveBtn.disabled = disabled;
        if (resetBtn) resetBtn.disabled = disabled;
    }

    const SettingsModule = {
        async load() {
            // A: appearance
            const localeSelect = $('settings-locale');
            if (localeSelect) {
                localeSelect.value = getLocale();
                localeSelect.addEventListener('change', function() { setLocale(this.value); });
            }
            const sidebarToggle = $('settings-sidebar-collapsed');
            if (sidebarToggle) {
                sidebarToggle.checked = (localStorage.getItem('sidebar_collapsed') === '1');
                sidebarToggle.addEventListener('change', function() {
                    const sidebar = $('sidebar');
                    const isCollapsed = sidebar && sidebar.classList.contains('collapsed');
                    if (this.checked !== isCollapsed && typeof window.toggleSidebarCollapse === 'function') {
                        window.toggleSidebarCollapse();
                    } else {
                        try { localStorage.setItem('sidebar_collapsed', this.checked ? '1' : '0'); } catch (_) {}
                    }
                });
            }

            // B: log settings
            try {
                const logData = await DashboardApi.endpoints.getLogSettings();
                fillLogForm(logData);
            } catch (e) {
                DashboardModals.showError(t('settings.logging.loadFailed', 'Failed to load log settings') + ': ' + (e.message || e));
            }

            // B2: restart-required (read-only) options
            try {
                const rrData = await DashboardApi.endpoints.getLogRestartRequired();
                renderRestartRequired(rrData);
            } catch (e) {
                const container = $('settings-restart-required');
                if (container) container.innerHTML = '<div class="text-danger">' + (e.message || e) + '</div>';
            }

            // E: system info
            try {
                const sysData = await DashboardApi.get('/api/settings/system');
                renderSystemInfo(sysData);
            } catch (e) {
                const container = $('settings-system-content');
                if (container) container.innerHTML = '<div class="text-danger">' + (e.message || e) + '</div>';
            }
        },

        async save() {
            const payload = collectLogForm();
            setButtonsDisabled(true);
            try {
                const data = await DashboardApi.endpoints.updateLogSettings(payload);
                fillLogForm(data);
                DashboardModals.showSuccess(t('settings.logging.saved', 'Log settings saved'));
            } catch (e) {
                DashboardModals.showError(t('settings.logging.saveFailed', 'Failed to save log settings') + ': ' + (e.message || e));
            } finally {
                setButtonsDisabled(false);
            }
        },

        reset() {
            DashboardModals.showConfirm(
                t('settings.logging.resetConfirm', 'Reset all log settings to appsettings defaults?'),
                async function() {
                    setButtonsDisabled(true);
                    try {
                        const data = await DashboardApi.endpoints.resetLogSettings();
                        fillLogForm(data);
                        DashboardModals.showSuccess(t('settings.logging.resetDone', 'Log settings reset to defaults'));
                    } catch (e) {
                        DashboardModals.showError(t('settings.logging.resetFailed', 'Failed to reset log settings') + ': ' + (e.message || e));
                    } finally {
                        setButtonsDisabled(false);
                    }
                },
                null,
                { danger: true }
            );
        },

        downloadDatabase() {
            DashboardApi.endpoints.downloadDatabase().catch(function(e) {
                DashboardModals.showError(t('settings.logging.downloadFailed', 'Failed to download database') + ': ' + (e.message || e));
            });
        }
    };

    window.SettingsModule = SettingsModule;
})();
