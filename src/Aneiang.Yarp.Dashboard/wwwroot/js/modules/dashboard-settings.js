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

            // Config tab — bind import dropzone on first show
            const configTab = document.getElementById('tab-config');
            if (configTab) {
                configTab.addEventListener('shown.bs.tab', () => this._bindImportDropzone());
            }

            // Database tab — load DB info on first show
            const dbTab = document.getElementById('tab-database');
            if (dbTab && !dbTab._bound) {
                dbTab._bound = true;
                dbTab.addEventListener('shown.bs.tab', () => this._loadDatabaseInfo());
            }
        },

        async _loadDatabaseInfo() {
            try {
                const sys = await DashboardApi.get('/api/settings/system');
                if (sys) {
                    const nameEl = $('db-info-name');
                    const sizeEl = $('db-info-size');
                    if (nameEl && sys.databaseFile) nameEl.textContent = sys.databaseFile;
                    if (sizeEl && sys.databaseSizeBytes != null) {
                        const kb = sys.databaseSizeBytes / 1024;
                        sizeEl.textContent = kb < 1024
                            ? kb.toFixed(1) + ' KB'
                            : (kb / 1024).toFixed(2) + ' MB';
                    }
                }
            } catch (e) { /* silent fail — info is non-critical */ }
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
        },

        async backupDatabase() {
            const btn = $('config-backup-btn');
            if (btn) { btn.disabled = true; btn.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>' + t('settings.config.backingUp', 'Backing up...'); }
            try {
                await DashboardApi.endpoints.backupDatabase();
                DashboardModals.showSuccess(t('settings.config.backupDone', 'Database backup downloaded'));
            } catch (e) {
                DashboardModals.showError(t('settings.config.backupFailed', 'Backup failed') + ': ' + (e.message || e));
            } finally {
                if (btn) { btn.disabled = false; btn.innerHTML = '<i class="bi bi-shield-check me-1"></i>' + t('settings.config.backupBtn', 'Backup Database'); }
            }
        },

        // ─── Config Export / Import ───

        _importData: null,
        _importDropzoneBound: false,

        _bindImportDropzone() {
            if (this._importDropzoneBound) return;
            const dz = $('config-import-dropzone');
            const fileInput = $('config-import-file');
            if (!dz || !fileInput) return;
            this._importDropzoneBound = true;

            dz.addEventListener('click', () => fileInput.click());
            fileInput.addEventListener('change', (e) => {
                if (e.target.files && e.target.files[0]) this._handleImportFile(e.target.files[0]);
            });
            dz.addEventListener('dragover', (e) => { e.preventDefault(); dz.classList.add('dragover'); });
            dz.addEventListener('dragleave', () => dz.classList.remove('dragover'));
            dz.addEventListener('drop', (e) => {
                e.preventDefault();
                dz.classList.remove('dragover');
                if (e.dataTransfer.files && e.dataTransfer.files[0]) this._handleImportFile(e.dataTransfer.files[0]);
            });
        },

        _normalizeImportData(json) {
            var data = json.data || json;
            var routes = [], clusters = [], apiPayload = null;

            // YARP native: { ReverseProxy: { Routes: {...}, Clusters: {...} } }
            var rp = data.ReverseProxy || data.reverseProxy;
            if (rp) {
                var rpRoutes = rp.Routes || rp.routes;
                var rpClusters = rp.Clusters || rp.clusters;
                if (rpRoutes) {
                    routes = Object.keys(rpRoutes).map(function(k) {
                        var r = rpRoutes[k];
                        if (!r.routeId && !r.RouteId) r.routeId = k;
                        return r;
                    });
                }
                if (rpClusters) {
                    clusters = Object.keys(rpClusters).map(function(k) {
                        var c = rpClusters[k];
                        if (!c.clusterId && !c.ClusterId) c.clusterId = k;
                        return c;
                    });
                }
                apiPayload = data;
            } else if (data.routes || data.clusters) {
                // Our custom format: { routes: [...], clusters: [...] }
                routes = data.routes || [];
                clusters = data.clusters || [];
                var routesObj = {};
                routes.forEach(function(r) { routesObj[r.routeId] = r; });
                var clustersObj = {};
                clusters.forEach(function(c) { clustersObj[c.clusterId] = c; });
                apiPayload = { ReverseProxy: { Routes: routesObj, Clusters: clustersObj } };
            }

            if (!apiPayload) return null;
            return { routes: routes, clusters: clusters, apiPayload: apiPayload };
        },

        _handleImportFile(file) {
            if (!file.name.endsWith('.json') && file.type !== 'application/json') {
                DashboardModals.showError(t('config.selectJsonFile', 'Please select a JSON file'));
                return;
            }
            const reader = new FileReader();
            reader.onload = (e) => {
                try {
                    const text = this._stripJsonComments(e.target.result);
                    const json = JSON.parse(text);
                    const normalized = this._normalizeImportData(json);
                    if (!normalized) {
                        DashboardModals.showError(t('config.importInvalid', 'Invalid config format'));
                        return;
                    }
                    this._importData = normalized;
                    this._renderManifest(normalized);
                } catch (err) {
                    DashboardModals.showError(t('config.importInvalid', 'Invalid config format') + ': ' + err.message);
                }
            };
            reader.onerror = () => DashboardModals.showError(t('config.importFailed', 'Import failed') + ': FileReader error');
            reader.readAsText(file);
        },

        _stripJsonComments(text) {
            var i = 0, result = '', len = text.length;
            var inString = false, stringChar = '';
            while (i < len) {
                var ch = text[i], next = text[i + 1];
                if (inString) {
                    result += ch;
                    if (ch === '\\' && i + 1 < len) { result += next; i += 2; continue; }
                    if (ch === stringChar) inString = false;
                    i++; continue;
                }
                if (ch === '"' || ch === "'") { inString = true; stringChar = ch; result += ch; i++; continue; }
                if (ch === '/' && next === '/') { i += 2; while (i < len && text[i] !== '\n' && text[i] !== '\r') i++; continue; }
                if (ch === '/' && next === '*') { i += 2; while (i < len && !(text[i] === '*' && text[i + 1] === '/')) i++; i += 2; continue; }
                result += ch; i++;
            }
            return result.replace(/,\s*([}\]])/g, '$1');
        },

        _renderManifest(data) {
            const routes = data.routes || [];
            const clusters = data.clusters || [];
            const esc = function(v) { return window.DashboardUtils ? DashboardUtils.escapeHtml(v) : String(v); };

            $('config-import-summary').textContent = t('config.importManifestCountSummary', '{0} routes, {1} clusters')
                .replace('{0}', routes.length).replace('{1}', clusters.length);
            $('config-manifest-routes-count').textContent = routes.length;
            $('config-manifest-clusters-count').textContent = clusters.length;

            var routeItems = routes.length === 0
                ? '<span class="text-muted small">' + t('config.importManifestNoRoutes', '(no routes)') + '</span>'
                : routes.slice(0, 50).map(function(r) {
                    var path = r.match && r.match.path ? r.match.path : '-';
                    return '<div class="config-manifest-item">' +
                        '<code>' + esc(r.routeId) + '</code>' +
                        '<span class="config-manifest-item-path">' + esc(path) + '</span>' +
                        '<span class="badge bg-light text-dark">' + esc(r.clusterId || '-') + '</span>' +
                    '</div>';
                }).join('') + (routes.length > 50 ? '<div class="text-muted small">+' + (routes.length - 50) + '...</div>' : '');
            $('config-manifest-routes-list').innerHTML = routeItems;

            var clusterItems = clusters.length === 0
                ? '<span class="text-muted small">' + t('config.importManifestNoClusters', '(no clusters)') + '</span>'
                : clusters.slice(0, 50).map(function(c) {
                    var destCount = c.destinations ? Object.keys(c.destinations).length : 0;
                    return '<div class="config-manifest-item">' +
                        '<code>' + esc(c.clusterId) + '</code>' +
                        '<span class="badge bg-light text-dark">' + destCount + ' ' + t('config.importManifestDestinations', 'destinations') + '</span>' +
                    '</div>';
                }).join('') + (clusters.length > 50 ? '<div class="text-muted small">+' + (clusters.length - 50) + '...</div>' : '');
            $('config-manifest-clusters-list').innerHTML = clusterItems;

            $('config-import-dropzone').classList.add('d-none');
            $('config-import-manifest').classList.remove('d-none');
        },

        resetImportFile() {
            this._importData = null;
            $('config-import-file').value = '';
            $('config-import-dropzone').classList.remove('d-none');
            $('config-import-manifest').classList.add('d-none');
        },

        async confirmImportConfig() {
            if (!this._importData) return;
            const btn = $('config-import-confirm');
            const backBtn = $('config-import-back');
            if (btn) { btn.disabled = true; btn.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>' + t('config.importing', 'Importing...'); }
            if (backBtn) backBtn.disabled = true;

            DashboardModals.showConfirm(
                t('history.importWarning', 'Import will replace all current routes and clusters. Please confirm before proceeding.'),
                async () => {
                    try {
                        const result = await DashboardApi.post('/api/config/import', this._importData.apiPayload);
                        DashboardModals.showSuccess(
                            result.message || t('config.imported', 'Config imported')
                        );
                        this.resetImportFile();
                        setTimeout(() => window.location.reload(), 1500);
                    } catch (e) {
                        DashboardModals.showError(t('config.importFailed', 'Import failed') + ': ' + (e.message || e));
                    } finally {
                        if (btn) { btn.disabled = false; btn.innerHTML = '<i class="bi bi-check-lg me-1"></i>' + t('config.importManifestContinue', 'Confirm Import'); }
                        if (backBtn) backBtn.disabled = false;
                    }
                },
                null,
                { danger: true }
            );
        },

        async exportConfig() {
            const btn = $('config-export-btn');
            if (btn) { btn.disabled = true; btn.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>' + t('config.exporting', 'Exporting...'); }
            try {
                const result = await DashboardApi.get('/api/config/export');
                const data = result.data || result;
                const json = JSON.stringify(data, null, 2);
                const blob = new Blob([json], { type: 'application/json' });
                const url = URL.createObjectURL(blob);
                const a = document.createElement('a');
                a.href = url;
                a.download = 'yarp-config-' + new Date().toISOString().slice(0, 10) + '.json';
                document.body.appendChild(a);
                a.click();
                document.body.removeChild(a);
                URL.revokeObjectURL(url);
                DashboardModals.showSuccess(t('config.exported', 'Config exported'));
            } catch (e) {
                DashboardModals.showError(t('config.exportFailed', 'Export failed') + ': ' + (e.message || e));
            } finally {
                if (btn) { btn.disabled = false; btn.innerHTML = '<i class="bi bi-download me-1"></i>' + t('config.exportBtn', 'Export'); }
            }
        }
    };

    window.SettingsModule = SettingsModule;
})();
