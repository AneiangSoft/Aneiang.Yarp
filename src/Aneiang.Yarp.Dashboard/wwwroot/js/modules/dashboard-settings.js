/**
 * Dashboard Settings Module
 * Provides: proxy-log settings (hot-reload) and system information.
 * Tabs: account security (2FA) · logging & persistence · system info.
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
        const esc = function(v) {
            return window.DashboardUtils && DashboardUtils.escapeHtml ? DashboardUtils.escapeHtml(v) : String(v);
        };
        const authModeLabels = {
            'None': t('settings.system.authMode.none', 'None'),
            'ApiKey': 'API Key',
            'DefaultJwt': 'JWT (Admin)',
            'CustomJwt': 'JWT (Custom)'
        };
        const modeLabels = {
            'Auto': t('settings.system.deploy.mode.auto', '自动检测'),
            'AllInOne': t('settings.system.deploy.mode.allInOne', '单端口共用'),
            'Split': t('settings.system.deploy.mode.split', '多端口分离'),
            'ProxyOnly': t('settings.system.deploy.mode.proxyOnly', '仅代理'),
            'DashboardOnly': t('settings.system.deploy.mode.dashboardOnly', '仅仪表盘')
        };
        const modeIcon = {
            'Auto': 'bi-diagram-3',
            'AllInOne': 'bi-box',
            'Split': 'bi-layout-split',
            'ProxyOnly': 'bi-arrow-left-right',
            'DashboardOnly': 'bi-speedometer2'
        };
        const roleColor = {
            'Proxy': '#6366f1',
            'Dashboard': '#10b981',
            'Admin': '#f59e0b',
            'Health': '#0ea5e9',
            'All': '#1e293b'
        };
        var html = '';
        var dep = data.deployment || {};

        // --- 部署横幅 ---
        var modeText = modeLabels[dep.mode] || dep.mode || '-';
        var modeIcn = modeIcon[dep.mode] || 'bi-diagram-3';
        html += '<div class="sys-deploy-banner">' +
            '<div class="sys-deploy-banner-mode"><i class="bi ' + modeIcn + '"></i>' + esc(modeText) + '</div>' +
            '<div class="sys-deploy-banner-flags">' +
                '<span><i class="bi ' + (dep.autoMiddleware ? 'bi-check-circle-fill' : 'bi-x-circle-fill') + '"></i>' + t('settings.system.deploy.autoMiddleware', '自动挂载中间件') + '</span>' +
                '<span><i class="bi ' + (dep.requireLoopbackAdmin ? 'bi-check-circle-fill' : 'bi-x-circle-fill') + '"></i>' + t('settings.system.deploy.requireLoopbackAdmin', 'Admin 本地绑定') + '</span>' +
                '<span><i class="bi ' + (dep.requireLoopbackDashboard ? 'bi-check-circle-fill' : 'bi-x-circle-fill') + '"></i>' + t('settings.system.deploy.requireLoopbackDashboard', 'Dashboard 本地绑定') + '</span>' +
            '</div>' +
        '</div>';

        // --- 端点卡片网格 ---
        if (dep.endpoints && dep.endpoints.length > 0) {
            html += '<div class="sys-section-title"><i class="bi bi-hdd-network text-info"></i>' + t('settings.system.deploy.endpoints', '端点列表') + '</div>';
            html += '<div class="sys-endpoint-grid">';
            dep.endpoints.forEach(function(ep) {
                var rc = roleColor[ep.role] || '#64748b';
                html += '<div class="sys-endpoint-card" style="border-left: 3px solid ' + rc + ';">' +
                    '<div class="sys-endpoint-card-header">' +
                        '<span class="sys-endpoint-card-name">' + esc(ep.name) + '</span>' +
                        '<span class="sys-endpoint-card-port">' + esc(ep.port) + '</span>' +
                    '</div>' +
                    '<div class="sys-endpoint-card-meta">' +
                        '<div><i class="bi ' + (ep.isPublic ? 'bi-globe text-warning' : 'bi-lock text-success') + '"></i><code>' + esc(ep.address) + '</code></div>' +
                        '<div><span class="badge" style="background:' + rc + ';">' + esc(ep.role) + '</span></div>' +
                    '</div>' +
                '</div>';
            });
            html += '</div>';
        }

        // --- 健康检查卡片网格 ---
        var hc = dep.healthCheck || {};
        var hcItems = [
            { label: t('settings.system.deploy.hc.enabled', '启用'), value: hc.enabled ? t('settings.system.deploy.yes', '是') : t('settings.system.deploy.no', '否'), yes: hc.enabled },
            { label: t('settings.system.deploy.hc.path', '健康路径'), value: hc.path || '-' },
            { label: t('settings.system.deploy.hc.readyPath', '就绪路径'), value: hc.readyPath || '-' },
            { label: t('settings.system.deploy.hc.livePath', '存活路径'), value: hc.livePath || '-' },
            { label: t('settings.system.deploy.hc.checkDb', '检查数据库'), value: hc.checkDatabase ? t('settings.system.deploy.yes', '是') : t('settings.system.deploy.no', '否'), yes: hc.checkDatabase },
            { label: t('settings.system.deploy.hc.checkConfig', '检查配置加载'), value: hc.checkConfigLoaded ? t('settings.system.deploy.yes', '是') : t('settings.system.deploy.no', '否'), yes: hc.checkConfigLoaded }
        ];
        html += '<div class="sys-section-title"><i class="bi bi-heart-pulse text-danger"></i>' + t('settings.system.deploy.healthCheck', '健康检查') + '</div>';
        html += '<div class="sys-hc-grid">';
        hcItems.forEach(function(item) {
            var valColor = item.yes === true ? '#10b981' : item.yes === false ? '#94a3b8' : 'inherit';
            html += '<div class="sys-hc-card">' +
                '<div class="sys-hc-card-label">' + item.label + '</div>' +
                '<div class="sys-hc-card-value" style="color:' + valColor + ';">' + esc(item.value) + '</div>' +
            '</div>';
        });
        html += '</div>';

        // --- 基础信息卡片网格 ---
        var basicItems = [
            { label: t('settings.system.version', 'Version'), value: data.version || '-', icon: 'bi-tag', bg: '#eef2ff', fg: '#4f46e5' },
            { label: t('settings.system.routePrefix', 'Route Prefix'), value: data.routePrefix || '-', icon: 'bi-signpost-split', bg: '#ecfdf5', fg: '#059669' },
            { label: t('settings.system.authMode', 'Auth Mode'), value: authModeLabels[data.authMode] || data.authMode || '-', icon: 'bi-shield-lock', bg: '#fff7ed', fg: '#d97706' },
            { label: t('settings.system.databaseFile', 'Database File'), value: data.databaseFile || '-', icon: 'bi-hdd', bg: '#f0f9ff', fg: '#0284c7' },
            { label: t('settings.system.databaseSize', 'Database Size'), value: formatBytes(data.databaseSizeBytes), icon: 'bi-database', bg: '#fef2f2', fg: '#dc2626' }
        ];
        html += '<div class="sys-section-title"><i class="bi bi-info-circle text-primary"></i>' + t('settings.system.basic', '基础信息') + '</div>';
        html += '<div class="sys-basic-grid">';
        basicItems.forEach(function(r) {
            html += '<div class="sys-basic-card">' +
                '<div class="sys-basic-card-icon" style="background:' + r.bg + ';color:' + r.fg + ';"><i class="bi ' + r.icon + '"></i></div>' +
                '<div class="sys-basic-card-body">' +
                    '<div class="sys-basic-card-label">' + r.label + '</div>' +
                    '<div class="sys-basic-card-value">' + esc(r.value) + '</div>' +
                '</div>' +
            '</div>';
        });
        html += '</div>';

        container.innerHTML = html;
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
            // Log settings (hot-reload)
            try {
                const logData = await DashboardApi.endpoints.getLogSettings();
                fillLogForm(logData);
            } catch (e) {
                DashboardModals.showError(t('settings.logging.loadFailed', 'Failed to load log settings') + ': ' + (e.message || e));
            }

            // Restart-required (read-only) options
            try {
                const rrData = await DashboardApi.endpoints.getLogRestartRequired();
                renderRestartRequired(rrData);
            } catch (e) {
                const container = $('settings-restart-required');
                if (container) container.innerHTML = '<div class="text-danger">' + (e.message || e) + '</div>';
            }

            // System info
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
