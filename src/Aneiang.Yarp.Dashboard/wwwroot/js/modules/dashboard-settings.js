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

            // AI tab — load config on first show
            const aiTab = document.getElementById('tab-ai');
            if (aiTab && !aiTab._bound) {
                aiTab._bound = true;
                aiTab.addEventListener('shown.bs.tab', () => this.loadAIConfig());
            }

            // AI settings event listeners
            const aiSave = $('ai-btn-save');
            if (aiSave) aiSave.addEventListener('click', () => this.saveAIConfig());
            const aiTest = $('ai-btn-test');
            if (aiTest) aiTest.addEventListener('click', () => this.testAIConnection());
            // Provider card selection
            document.querySelectorAll('.ai-provider-card').forEach(card => {
                card.addEventListener('click', () => this.selectProvider(card));
            });
            // Enabled toggle → update status badge
            const aiEnabled = $('ai-enabled');
            if (aiEnabled) aiEnabled.addEventListener('change', () => this.updateStatusBadge());
            // API Key show/hide toggle
            const aiKeyToggle = $('ai-api-key-toggle');
            if (aiKeyToggle) aiKeyToggle.addEventListener('click', () => this._toggleApiKeyVisibility());
            // API Key input → re-evaluate test button availability
            const aiKeyInput = $('ai-api-key');
            if (aiKeyInput) aiKeyInput.addEventListener('input', () => this._updateTestButtonState());
            const aiMaxTokens = $('ai-max-tokens');
            if (aiMaxTokens) aiMaxTokens.addEventListener('input', () => { $('ai-max-tokens-val').textContent = aiMaxTokens.value; });
            const aiTemp = $('ai-temperature');
            if (aiTemp) aiTemp.addEventListener('input', () => { $('ai-temperature-val').textContent = aiTemp.value; });
            // MCP Server toggle -> show/hide port field + examples + sync URL
            const aiMcpEnabled = $('ai-mcp-enabled');
            if (aiMcpEnabled) aiMcpEnabled.addEventListener('change', () => this._toggleMcpSection());
            // Port change -> sync endpoint URL
            const aiMcpPort = $('ai-mcp-port');
            if (aiMcpPort) aiMcpPort.addEventListener('input', () => this._updateMcpUrl());
            // Copy endpoint URL button
            const aiMcpCopy = $('ai-mcp-copy-url');
            if (aiMcpCopy) aiMcpCopy.addEventListener('click', () => this._copyMcpUrl());
            // Fallback toggle → show/hide config section
            const aiFallbackEnabled = $('ai-fallback-enabled');
            if (aiFallbackEnabled) aiFallbackEnabled.addEventListener('change', () => this._toggleFallbackConfig());
            // Fallback API Key show/hide toggle
            const aiFallbackKeyToggle = $('ai-fallback-key-toggle');
            if (aiFallbackKeyToggle) aiFallbackKeyToggle.addEventListener('click', () => this._toggleFallbackKeyVisibility());
        },

        _toggleMcpSection() {
            const enabled = $('ai-mcp-enabled').checked;
            const config = $('ai-mcp-config');
            const examples = $('ai-mcp-examples');
            if (config) config.style.display = enabled ? '' : 'none';
            if (examples) examples.style.display = enabled ? '' : 'none';
            this._updateMcpUrl();
        },

        _updateMcpUrl() {
            const urlInput = $('ai-mcp-url');
            const portInput = $('ai-mcp-port');
            if (!urlInput || !portInput) return;
            const port = parseInt(portInput.value) || 8090;
            const host = window.location.hostname || 'localhost';
            urlInput.value = 'http://' + host + ':' + port + '/mcp';
        },

        _copyMcpUrl() {
            const urlInput = $('ai-mcp-url');
            if (!urlInput || !urlInput.value) return;
            const done = () => {
                const btn = $('ai-mcp-copy-url');
                if (!btn) return;
                const icon = btn.querySelector('i');
                if (!icon) return;
                icon.className = 'bi bi-check';
                btn.classList.add('text-success');
                setTimeout(() => {
                    icon.className = 'bi bi-clipboard';
                    btn.classList.remove('text-success');
                }, 1500);
            };
            if (navigator.clipboard && navigator.clipboard.writeText) {
                navigator.clipboard.writeText(urlInput.value).then(done).catch(done);
            } else {
                urlInput.select();
                try { document.execCommand('copy'); } catch (e) { /* noop */ }
                done();
            }
        },

        _toggleFallbackConfig() {
            const config = $('ai-fallback-config');
            if (config) config.style.display = $('ai-fallback-enabled').checked ? '' : 'none';
            this._updateFallbackStatus();
        },

        _toggleFallbackKeyVisibility() {
            const input = $('ai-fallback-api-key');
            const icon = $('ai-fallback-key-toggle').querySelector('i');
            if (!input || !icon) return;
            if (input.type === 'password') {
                input.type = 'text';
                icon.className = 'bi bi-eye-slash';
            } else {
                input.type = 'password';
                icon.className = 'bi bi-eye';
            }
        },

        _updateFallbackStatus() {
            const badge = $('ai-fallback-status');
            if (!badge) return;
            const enabled = $('ai-fallback-enabled').checked;
            const hasKey = !!$('ai-fallback-api-key').value || this._fallbackHasKey;
            if (!enabled) {
                badge.textContent = t('ai.statusDisabled', 'Disabled');
                badge.className = 'settings-status-badge badge bg-secondary';
            } else if (hasKey) {
                badge.textContent = t('ai.statusReady', 'Ready');
                badge.className = 'settings-status-badge badge bg-success';
            } else {
                badge.textContent = t('ai.statusNoKey', 'No API Key');
                badge.className = 'settings-status-badge badge bg-warning';
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
        },

        // ─── AI Assistant ───

        async loadAIConfig() {
            try {
                const config = await DashboardApi.get('/api/ai/config');
                if (!config) return;
                $('ai-enabled').checked = config.enabled;
                $('ai-provider').value = config.provider || 'deepseek';
                // Highlight active provider card
                document.querySelectorAll('.ai-provider-card').forEach(c => {
                    c.classList.toggle('active', c.dataset.provider === (config.provider || 'deepseek'));
                });
                $('ai-api-key').value = '';
                $('ai-api-key').placeholder = config.hasApiKey ? '••••••••（已配置，留空不修改）' : 'sk-...';
                $('ai-base-url').value = config.baseUrl || '';
                $('ai-chat-model').value = config.chatModel || '';
                $('ai-max-tokens').value = config.maxTokens || 4096;
                $('ai-max-tokens-val').textContent = config.maxTokens || 4096;
                $('ai-temperature').value = config.temperature || 0.7;
                $('ai-temperature-val').textContent = config.temperature || 0.7;
                $('ai-max-history').value = config.maxHistory || 20;
                $('ai-reasoning-effort').value = config.reasoningEffort || 'auto';
                $('ai-use-cache').checked = config.useCache !== false;
                $('ai-enhance-notif').checked = config.enhanceNotif || false;
                // MCP Server
                const mcp = config.mcpServer || {};
                $('ai-mcp-enabled').checked = !!mcp.enabled;
                $('ai-mcp-port').value = mcp.port || 8090;
                this._toggleMcpSection();
                // Fallback
                const fb = config.fallback || {};
                $('ai-fallback-enabled').checked = !!fb.enabled;
                $('ai-fallback-provider').value = fb.provider || 'openai';
                $('ai-fallback-api-key').value = '';
                $('ai-fallback-api-key').placeholder = fb.hasApiKey ? '••••••••（已配置，留空不修改）' : 'sk-...';
                $('ai-fallback-base-url').value = fb.baseUrl || '';
                $('ai-fallback-model').value = fb.model || '';
                this._fallbackHasKey = !!fb.hasApiKey;
                this._toggleFallbackConfig();
                this._hasApiKey = !!config.hasApiKey;
                this.updateStatusBadge(config);
                this._updateTestButtonState();
            } catch (e) {
                console.error('[AI] Failed to load config:', e);
            }
        },

        updateStatusBadge(config) {
            const badge = $('ai-status-badge');
            if (!badge) return;
            if (!config) {
                config = {
                    enabled: $('ai-enabled').checked,
                    hasApiKey: this._hasApiKey || (!!$('ai-api-key').value)
                };
            }
            if (!config.enabled) {
                badge.textContent = t('ai.statusDisabled', 'Disabled');
                badge.className = 'settings-status-badge badge bg-secondary';
            } else if (config.hasApiKey) {
                badge.textContent = t('ai.statusReady', 'Ready');
                badge.className = 'settings-status-badge badge bg-success';
            } else {
                badge.textContent = t('ai.statusNoKey', 'No API Key');
                badge.className = 'settings-status-badge badge bg-warning';
            }
        },

        _updateTestButtonState() {
            const btn = $('ai-btn-test');
            if (!btn) return;
            const hasKey = this._hasApiKey || (!!$('ai-api-key').value);
            btn.disabled = !hasKey;
            if (!hasKey) {
                btn.title = t('ai.testNoKey', 'Please configure API Key before testing');
            } else {
                btn.title = '';
            }
        },

        _toggleApiKeyVisibility() {
            const input = $('ai-api-key');
            const icon = $('ai-api-key-toggle').querySelector('i');
            if (!input || !icon) return;
            if (input.type === 'password') {
                input.type = 'text';
                icon.className = 'bi bi-eye-slash';
                $('ai-api-key-toggle').setAttribute('title', t('ai.hideKey', 'Hide key'));
            } else {
                input.type = 'password';
                icon.className = 'bi bi-eye';
                $('ai-api-key-toggle').setAttribute('title', t('ai.showKey', 'Show key'));
            }
        },

        selectProvider(card) {
            document.querySelectorAll('.ai-provider-card').forEach(c => c.classList.remove('active'));
            card.classList.add('active');
            $('ai-provider').value = card.dataset.provider;
            const base = card.dataset.base;
            const model = card.dataset.model;
            if (base) $('ai-base-url').value = base;
            if (model) {
                $('ai-chat-model').value = model;
            }
        },

        async saveAIConfig() {
            const saveBtn = $('ai-btn-save');
            const payload = {
                enabled: $('ai-enabled').checked,
                provider: $('ai-provider').value,
                apiKey: $('ai-api-key').value,
                baseUrl: $('ai-base-url').value,
                chatModel: $('ai-chat-model').value,
                maxTokens: parseInt($('ai-max-tokens').value) || 4096,
                temperature: parseFloat($('ai-temperature').value) || 0.7,
                maxHistory: parseInt($('ai-max-history').value) || 20,
                reasoningEffort: $('ai-reasoning-effort').value,
                useCache: $('ai-use-cache').checked,
                enhanceNotif: $('ai-enhance-notif').checked,
                fallback: {
                    enabled: $('ai-fallback-enabled').checked,
                    provider: $('ai-fallback-provider').value,
                    apiKey: $('ai-fallback-api-key').value,
                    baseUrl: $('ai-fallback-base-url').value,
                    model: $('ai-fallback-model').value
                },
                mcpServer: {
                    enabled: $('ai-mcp-enabled').checked,
                    port: parseInt($('ai-mcp-port').value) || 8090
                }
            };
            if (window.DashboardLoading && saveBtn) DashboardLoading.setButton(saveBtn, true, t('ai.saving', 'Saving...'));
            try {
                await DashboardApi.post('/api/ai/config', payload);
                DashboardModals.showSuccess(t('ai.saveSuccess', 'AI settings saved'));
                // Reload config from server to get accurate hasApiKey state
                await this.loadAIConfig();
                if (window.DashboardAI) DashboardAI.init();
            } catch (e) {
                DashboardModals.showError(t('ai.saveFailed', 'Save failed') + ': ' + (e.message || e));
            } finally {
                if (window.DashboardLoading && saveBtn) DashboardLoading.setButton(saveBtn, false);
            }
        },

        async testAIConnection() {
            const btn = $('ai-btn-test');
            const resultBox = $('ai-test-result');
            const hasKey = this._hasApiKey || (!!$('ai-api-key').value);
            if (!hasKey) {
                DashboardModals.showError(t('ai.testNoKey', 'Please configure API Key before testing'));
                return;
            }
            if (btn) { btn.disabled = true; btn.innerHTML = '<i class="bi bi-arrow-clockwise spin"></i> ' + t('ai.testing', 'Testing...'); }
            const provider = $('ai-provider').value;
            const model = $('ai-chat-model').value;
            const start = Date.now();
            try {
                const result = await DashboardApi.post('/api/ai/test', {});
                const latency = Date.now() - start;
                if (resultBox) {
                    if (result.ok) {
                        resultBox.className = 'ai-test-result ai-test-result--ok';
                        resultBox.innerHTML =
                            '<i class="bi bi-check-circle-fill ai-test-result-icon"></i>' +
                            '<span>' + t('ai.testSuccessInline', 'Connection OK') + '</span>' +
                            '<span class="ai-test-result-meta">' + t('ai.testLatency', 'Latency') + ': ' + latency + 'ms</span>' +
                            '<span class="ai-test-result-meta">' + t('ai.testProvider', 'Provider') + ': ' + provider + '</span>' +
                            '<span class="ai-test-result-meta">' + t('ai.testModel', 'Model') + ': ' + model + '</span>';
                    } else {
                        resultBox.className = 'ai-test-result ai-test-result--fail';
                        resultBox.innerHTML =
                            '<i class="bi bi-x-circle-fill ai-test-result-icon"></i>' +
                            '<span>' + t('ai.testFailedInline', 'Connection Failed') + '</span>' +
                            '<span class="ai-test-result-msg">' + (result.message || '') + '</span>';
                    }
                    resultBox.style.display = 'flex';
                }
            } catch (e) {
                if (resultBox) {
                    resultBox.className = 'ai-test-result ai-test-result--fail';
                    resultBox.innerHTML =
                        '<i class="bi bi-x-circle-fill ai-test-result-icon"></i>' +
                        '<span>' + t('ai.testFailedInline', 'Connection Failed') + '</span>' +
                        '<span class="ai-test-result-msg">' + (e.message || e) + '</span>';
                    resultBox.style.display = 'flex';
                }
            } finally {
                if (btn) { btn.disabled = false; btn.innerHTML = '<i class="bi bi-lightning"></i> ' + t('ai.test', 'Test Connection'); }
            }
        }
    };

    window.SettingsModule = SettingsModule;
})();
