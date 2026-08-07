/**
 * Settings Module - System configuration viewer with hot-reload ProxyLog editing
 */
(function() {
    'use strict';

    var SettingsModule = {
        initialized: false,
        currentSettings: null,
        editForm: null,

        init: function() { if (this.initialized) return; this.initialized = true; },
        destroy: function() { this.initialized = false; },

        load: async function() {
            var container = document.getElementById('settings-content');
            if (!container) return;
            try {
                container.innerHTML = '<div class="loading-state"><div class="loading-spinner"></div><div class="loading-text">' + __('pluginPage.loading') + '</div></div>';
                var data = await window.DashboardApi.get('/api/settings');
                this.currentSettings = data;
                this.editForm = this.extractProxyLogForm(data.proxyLog);
                this.render(data, container);
            } catch (error) {
                container.innerHTML = '<div class="alert alert-danger"><i class="bi bi-exclamation-triangle me-2"></i>' + this.esc(__('pluginPage.loadFailed', { error: error.message || error })) + '</div>';
            }
        },

        render: function(data, container) {
            var html = '';
            var self = this;

            // ─── General section (read-only + locale switch) ───
            html += '<h6 class="mb-3"><i class="bi bi-sliders me-1"></i>' + __('settings.general') + '</h6>';
            html += '<div class="row mb-3">';
            html += this.card(__('settings.language'), this.getLocaleDisplay(data.locale), 'bi-translate', 'blue', true);
            html += this.card(__('settings.routePrefix'), '/' + data.routePrefix, 'bi-signpost', 'green', false, '<i class="bi bi-arrow-repeat me-1 text-muted"></i><span class="text-muted small">' + __('settings.restartRequired') + '</span>');
            html += this.card(__('settings.storageProvider'), data.storage.provider, 'bi-database', 'amber', false, '<i class="bi bi-arrow-repeat me-1 text-muted"></i><span class="text-muted small">' + __('settings.restartRequired') + '</span>');
            html += '</div>';

            // Locale switcher
            html += '<div class="mb-4">';
            html += '<label class="form-label">' + __('settings.switchLanguage') + '</label><br>';
            html += '<div class="btn-group" role="group">';
            html += '<button class="btn btn-sm ' + (data.locale === 'zh-CN' ? 'btn-primary' : 'btn-outline-primary') + '" onclick="SettingsModule.setLocale(\'zh-CN\')">' + __('settings.locale.zhCN') + '</button>';
            html += '<button class="btn btn-sm ' + (data.locale === 'en-US' ? 'btn-primary' : 'btn-outline-primary') + '" onclick="SettingsModule.setLocale(\'en-US\')">' + __('settings.locale.enUS') + '</button>';
            html += '</div></div>';

            // ─── Auth section (read-only, requires restart) ───
            html += '<h6 class="mb-3"><i class="bi bi-shield-lock me-1"></i>' + __('settings.authentication') + '</h6>';
            html += '<div class="alert alert-light border py-2 mb-3"><i class="bi bi-info-circle me-1 text-muted"></i><span class="small text-muted">' + __('settings.authRestartHint') + '</span></div>';
            html += '<div class="row mb-4">';
            html += this.card(__('settings.authMode'), this.getAuthModeDisplay(data.auth.authMode), 'bi-key', 'purple');
            html += this.card(__('settings.apiKeyHeader'), data.auth.apiKeyHeaderName || '-', 'bi-input-cursor-text', 'blue');
            html += this.card(__('settings.jwtUsername'), data.auth.jwtUsername || '-', 'bi-person-badge', 'green');
            html += this.card(__('settings.minPasswordLength'), data.auth.minPasswordLength, 'bi-lock', 'amber');
            html += '</div>';

            // ─── ProxyLog section (EDITABLE - hot-reload) ───
            html += '<h6 class="mb-3"><i class="bi bi-journal-text me-1"></i>' + __('settings.proxyLog') + '</h6>';
            html += '<div class="alert alert-success border py-2 mb-3"><i class="bi bi-lightning-charge-fill me-1"></i><span class="small">' + __('settings.hotReloadHint') + '</span></div>';

            html += '<div class="row mb-4">';

            // Toggle: Log persistence
            html += this.editToggle('logPersistenceEnabled', __('settings.persistence'), this.editForm.logPersistenceEnabled, 'bi-hdd-stack');
            // Number: Buffer capacity (read-only, requires restart)
            html += this.card(__('settings.bufferCapacity'), data.proxyLog.logBufferCapacity, 'bi-memory', 'blue', false, '<i class="bi bi-arrow-repeat me-1 text-muted"></i><span class="text-muted small">' + __('settings.restartRequired') + '</span>');

            // Number: Meta retention days
            html += this.editNumber('logMetaRetentionDays', __('settings.metaRetention'), this.editForm.logMetaRetentionDays, 'bi-calendar-week', __('settings.unit.days'), 1, 365);
            // Number: Body retention days
            html += this.editNumber('logBodyRetentionDays', __('settings.bodyRetention'), this.editForm.logBodyRetentionDays, 'bi-calendar-x', __('settings.unit.days'), 1, 365);

            // Toggle: Request body capture
            html += this.editToggle('enableProxyRequestBodyCapture', __('settings.requestBodyCapture'), this.editForm.enableProxyRequestBodyCapture, 'bi-box-arrow-in-down');
            // Toggle: Response body capture
            html += this.editToggle('enableProxyResponseBodyCapture', __('settings.responseBodyCapture'), this.editForm.enableProxyResponseBodyCapture, 'bi-box-arrow-up-right');

            // Number: Max body length
            html += this.editNumber('logMaxBodyLength', __('settings.maxBodyLength'), this.editForm.logMaxBodyLength, 'bi-rulers', 'bytes', 0, 1048576);

            // Toggle: Log sampling
            html += this.editToggle('enableLogSampling', __('settings.logSampling'), this.editForm.enableLogSampling, 'bi-pie-chart');
            // Range: Sampling rate
            html += this.editRange('logSamplingRate', __('settings.logSampling') + ' ' + __('settings.samplingRate'), this.editForm.logSamplingRate, 0, 1, 0.05, function(val) { return (val * 100).toFixed(0) + '%'; });

            // Toggle: Errors only
            html += this.editToggle('logErrorsOnly', __('settings.errorsOnly'), this.editForm.logErrorsOnly, 'bi-exclamation-triangle');
            // Select: Min log level
            html += this.editSelect('minLogLevel', __('settings.minLogLevel'), this.editForm.minLogLevel, ['Debug', 'Information', 'Warning', 'Error', 'Critical'], 'bi-funnel');

            html += '</div>';

            // Save button
            html += '<div class="d-flex justify-content-end gap-2 mb-4">';
            html += '<button class="btn btn-outline-secondary btn-sm" onclick="SettingsModule.resetForm()"><i class="bi bi-arrow-counterclockwise me-1"></i>' + __('settings.reset') + '</button>';
            html += '<button class="btn btn-success btn-sm" id="settings-save-btn" onclick="SettingsModule.saveProxyLog()"><i class="bi bi-check-lg me-1"></i>' + __('settings.saveAndApply') + '</button>';
            html += '</div>';

            // ─── Storage section (read-only) ───
            html += '<h6 class="mb-3"><i class="bi bi-database me-1"></i>' + __('settings.storage') + '</h6>';
            html += '<div class="row mb-4">';
            html += this.card(__('settings.database'), data.storage.sqliteConnectionString, 'bi-hdd-network', 'blue', false, '<i class="bi bi-arrow-repeat me-1 text-muted"></i><span class="text-muted small">' + __('settings.restartRequired') + '</span>');
            html += '</div>';

            container.innerHTML = html;

            // Attach range slider listeners
            this.attachRangeListeners();
        },

        // ─── Editable control renderers ───

        editToggle: function(field, label, value, icon) {
            var checked = value ? ' checked' : '';
            return '<div class="col-xl-3 col-md-4 col-sm-6 mb-3">' +
                '<div class="settings-card h-100 p-3">' +
                '<div class="d-flex align-items-center mb-2">' +
                '<i class="bi ' + icon + ' me-2 text-muted"></i>' +
                '<small class="text-muted">' + this.esc(label) + '</small>' +
                '</div>' +
                '<div class="form-check form-switch">' +
                '<input class="form-check-input" type="checkbox" role="switch" data-field="' + field + '"' + checked + ' style="cursor:pointer;">' +
                '</div></div></div>';
        },

        editNumber: function(field, label, value, icon, unit, min, max) {
            return '<div class="col-xl-3 col-md-4 col-sm-6 mb-3">' +
                '<div class="settings-card h-100 p-3">' +
                '<div class="d-flex align-items-center mb-2">' +
                '<i class="bi ' + icon + ' me-2 text-muted"></i>' +
                '<small class="text-muted">' + this.esc(label) + '</small>' +
                '</div>' +
                '<div class="input-group input-group-sm">' +
                '<input type="number" class="form-control" data-field="' + field + '" value="' + this.esc(String(value)) + '" min="' + min + '" max="' + max + '">' +
                (unit ? '<span class="input-group-text">' + unit + '</span>' : '') +
                '</div></div></div>';
        },

        editSelect: function(field, label, value, options, icon) {
            var opts = options.map(function(o) {
                return '<option value="' + o + '"' + (o === value ? ' selected' : '') + '>' + o + '</option>';
            }).join('');
            return '<div class="col-xl-3 col-md-4 col-sm-6 mb-3">' +
                '<div class="settings-card h-100 p-3">' +
                '<div class="d-flex align-items-center mb-2">' +
                '<i class="bi ' + icon + ' me-2 text-muted"></i>' +
                '<small class="text-muted">' + this.esc(label) + '</small>' +
                '</div>' +
                '<select class="form-select form-select-sm" data-field="' + field + '">' + opts + '</select>' +
                '</div></div>';
        },

        editRange: function(field, label, value, min, max, step, formatter) {
            var display = formatter ? formatter(value) : String(value);
            return '<div class="col-xl-3 col-md-4 col-sm-6 mb-3">' +
                '<div class="settings-card h-100 p-3">' +
                '<div class="d-flex align-items-center mb-2">' +
                '<small class="text-muted">' + this.esc(label) + '</small>' +
                '<span class="badge bg-primary ms-auto" data-display="' + field + '">' + display + '</span>' +
                '</div>' +
                '<input type="range" class="form-range" data-field="' + field + '" value="' + value + '" min="' + min + '" max="' + max + '" step="' + step + '" style="cursor:pointer;">' +
                '</div></div>';
        },

        attachRangeListeners: function() {
            var self = this;
            document.querySelectorAll('[data-field="logSamplingRate"][type="range"]').forEach(function(input) {
                input.addEventListener('input', function() {
                    var display = document.querySelector('[data-display="logSamplingRate"]');
                    if (display) display.textContent = (parseFloat(input.value) * 100).toFixed(0) + '%';
                });
            });
        },

        // ─── Form helpers ───

        extractProxyLogForm: function(proxyLog) {
            return {
                logPersistenceEnabled: proxyLog.logPersistenceEnabled,
                logMetaRetentionDays: proxyLog.logMetaRetentionDays,
                logBodyRetentionDays: proxyLog.logBodyRetentionDays,
                enableProxyRequestBodyCapture: proxyLog.enableProxyRequestBodyCapture,
                enableProxyResponseBodyCapture: proxyLog.enableProxyResponseBodyCapture,
                logMaxBodyLength: proxyLog.logMaxBodyLength,
                logMaxBodyBufferBytes: proxyLog.logMaxBodyBufferBytes || 65536,
                enableLogSampling: proxyLog.enableLogSampling,
                logSamplingRate: proxyLog.logSamplingRate,
                logErrorsOnly: proxyLog.logErrorsOnly,
                minLogLevel: proxyLog.minLogLevel
            };
        },

        readForm: function() {
            var form = {};
            document.querySelectorAll('[data-field]').forEach(function(el) {
                var field = el.dataset.field;
                if (el.type === 'checkbox') {
                    form[field] = el.checked;
                } else if (el.type === 'number' || el.type === 'range') {
                    form[field] = parseFloat(el.value);
                } else {
                    form[field] = el.value;
                }
            });
            return form;
        },

        resetForm: function() {
            if (!this.currentSettings) return;
            this.editForm = this.extractProxyLogForm(this.currentSettings.proxyLog);
            this.render(this.currentSettings, document.getElementById('settings-content'));
        },

        saveProxyLog: async function() {
            var btn = document.getElementById('settings-save-btn');
            var form = this.readForm();

            // Build request - only send changed fields
            var pl = this.currentSettings.proxyLog;
            var payload = {};
            if (form.logPersistenceEnabled !== pl.logPersistenceEnabled) payload.logPersistenceEnabled = form.logPersistenceEnabled;
            if (form.logMetaRetentionDays !== pl.logMetaRetentionDays) payload.logMetaRetentionDays = form.logMetaRetentionDays;
            if (form.logBodyRetentionDays !== pl.logBodyRetentionDays) payload.logBodyRetentionDays = form.logBodyRetentionDays;
            if (form.enableProxyRequestBodyCapture !== pl.enableProxyRequestBodyCapture) payload.enableProxyRequestBodyCapture = form.enableProxyRequestBodyCapture;
            if (form.enableProxyResponseBodyCapture !== pl.enableProxyResponseBodyCapture) payload.enableProxyResponseBodyCapture = form.enableProxyResponseBodyCapture;
            if (form.logMaxBodyLength !== pl.logMaxBodyLength) payload.logMaxBodyLength = form.logMaxBodyLength;
            if (form.enableLogSampling !== pl.enableLogSampling) payload.enableLogSampling = form.enableLogSampling;
            if (form.logSamplingRate !== pl.logSamplingRate) payload.logSamplingRate = form.logSamplingRate;
            if (form.logErrorsOnly !== pl.logErrorsOnly) payload.logErrorsOnly = form.logErrorsOnly;
            if (form.minLogLevel !== pl.minLogLevel) payload.minLogLevel = form.minLogLevel;

            if (Object.keys(payload).length === 0) {
                window.DashboardModals.showInfo(__('settings.noChanges'));
                return;
            }

            try {
                btn.disabled = true;
                btn.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>' + __('settings.saving');
                var result = await window.DashboardApi.put('/api/settings/proxy-log', payload);
                // Update current settings with returned values
                if (result && result.settings) {
                    Object.assign(this.currentSettings.proxyLog, result.settings);
                    this.editForm = this.extractProxyLogForm(this.currentSettings.proxyLog);
                }
                window.DashboardModals.showSuccess(__('settings.saveSuccess'));
                this.render(this.currentSettings, document.getElementById('settings-content'));
            } catch (error) {
                window.DashboardModals.showError(__('settings.saveFailed') + ': ' + (error.message || error));
                btn.disabled = false;
                btn.innerHTML = '<i class="bi bi-check-lg me-1"></i>' + __('settings.saveAndApply');
            }
        },

        // ─── Read-only card renderer ───

        card: function(title, value, icon, color, valueIsHtml, footer) {
            var colorClass = color ? 'settings-card--' + color : '';
            var safeValue = valueIsHtml ? String(value) : this.esc(String(value));
            var valueTitle = valueIsHtml ? '' : ' title="' + this.esc(String(value)) + '"';
            var footerHtml = footer ? '<div class="mt-2">' + footer + '</div>' : '';
            return '<div class="col-xl-3 col-md-4 col-sm-6 mb-3">' +
                '<div class="settings-card ' + colorClass + ' h-100 p-3">' +
                '<div class="d-flex align-items-center mb-2">' +
                '<i class="bi ' + icon + ' me-2 text-muted"></i>' +
                '<small class="text-muted">' + this.esc(title) + '</small>' +
                '</div>' +
                '<div class="fw-semibold text-truncate"' + valueTitle + '>' + safeValue + '</div>' +
                footerHtml +
                '</div></div>';
        },

        statusBadge: function(enabled) {
            return '<i class="bi ' + (enabled ? 'bi-check-circle-fill text-success' : 'bi-x-circle-fill text-danger') + ' me-1"></i>' +
                (enabled ? __('settings.enabled') : __('settings.disabled'));
        },

        setLocale: async function(locale) {
            try {
                await window.DashboardApi.post('/api/settings/locale', { locale: locale });
                window.location.reload();
            } catch (error) {
                alert(__('settings.changeFailed', { error: error.message || error }));
            }
        },

        getLocaleDisplay: function(locale) {
            if (locale === 'zh-CN') return __('settings.locale.zhCN');
            if (locale === 'en-US') return __('settings.locale.enUS');
            return locale || '-';
        },

        getAuthModeDisplay: function(mode) {
            var map = {
                'None': __('settings.auth.none'),
                'ApiKey': 'API Key',
                'CustomJwt': __('settings.auth.customJwt'),
                'DefaultJwt': __('settings.auth.defaultJwt')
            };
            return map[mode] || mode;
        },

        // ─── Two-Factor Authentication management ───

        twoFactorState: { enabled: false, loading: false, setupData: null },

        loadTwoFactor: async function() {
            var container = document.getElementById('twofa-content');
            var badge = document.getElementById('twofa-status-badge');
            if (!container) return;
            try {
                var resp = await window.DashboardApi.get('/api/2fa/status');
                this.twoFactorState.enabled = resp.enabled;
                this.twoFactorState.setupData = null;
                this.renderTwoFactor(container, badge);
            } catch (error) {
                container.innerHTML = '<div class="alert alert-danger py-2">' + __('twofa.loadFailed') + ': ' + this.esc(error.message || String(error)) + '</div>';
                if (badge) { badge.className = 'badge bg-danger'; badge.textContent = __('common.loadFailed'); }
            }
        },

        renderTwoFactor: function(container, badge) {
            var enabled = this.twoFactorState.enabled;
            var setupData = this.twoFactorState.setupData;

            // Update badge
            if (badge) {
                badge.className = 'badge ' + (enabled ? 'bg-success' : 'bg-secondary');
                badge.textContent = enabled ? __('twofa.enabled') : __('twofa.disabled');
            }

            if (setupData) {
                // Setup mode: show QR + verification
                container.innerHTML = this.renderTwoFactorSetup(setupData);
                // Generate QR code
                this.generateQRCode(setupData.qrUrl);
                return;
            }

            if (enabled) {
                container.innerHTML =
                    '<div class="alert alert-success border py-3">' +
                        '<div class="d-flex align-items-center">' +
                            '<i class="bi bi-shield-check-fill text-success me-3 fs-3"></i>' +
                            '<div>' +
                                '<div class="fw-semibold text-success">' + __('twofa.enabledTitle') + '</div>' +
                                '<div class="small text-muted mt-1">' + __('twofa.enabledDesc') + '</div>' +
                            '</div>' +
                        '</div>' +
                    '</div>' +
                    '<div class="mt-3">' +
                        '<button class="btn btn-outline-danger btn-sm" onclick="SettingsModule.disableTwoFactor()">' +
                            '<i class="bi bi-shield-x me-1"></i>' + __('twofa.disable') +
                        '</button>' +
                    '</div>';
            } else {
                container.innerHTML =
                    '<div class="alert alert-light border py-3">' +
                        '<div class="d-flex align-items-center">' +
                            '<i class="bi bi-shield-lock text-muted me-3 fs-3"></i>' +
                            '<div>' +
                                '<div class="fw-semibold">' + __('twofa.disabledTitle') + '</div>' +
                                '<div class="small text-muted mt-1">' + __('twofa.disabledDesc') + '</div>' +
                            '</div>' +
                        '</div>' +
                    '</div>' +
                    '<div class="mt-3">' +
                        '<button class="btn btn-primary btn-sm" onclick="SettingsModule.startTwoFactorSetup()">' +
                            '<i class="bi bi-shield-lock-fill me-1"></i>' + __('twofa.enable') +
                        '</button>' +
                    '</div>';
            }
        },

        renderTwoFactorSetup: function(data) {
            return '' +
                '<div class="row align-items-center">' +
                    '<div class="col-md-5 text-center">' +
                        '<div class="d-inline-block p-3 border rounded bg-white" id="twofa-qr-container" style="width:220px;height:220px;"></div>' +
                        '<div class="small text-muted mt-2">' + __('twofa.scanQR') + '</div>' +
                    '</div>' +
                    '<div class="col-md-7">' +
                        '<h6 class="fw-bold mb-2"><i class="bi bi-1-circle me-1"></i>' + __('twofa.step1') + '</h6>' +
                        '<div class="mb-3">' +
                            '<label class="form-label small text-muted">' + __('twofa.secret') + '</label>' +
                            '<div class="input-group input-group-sm">' +
                                '<input type="text" class="form-control font-monospace" id="twofa-secret" value="' + this.esc(data.secret) + '" readonly>' +
                                '<button class="btn btn-outline-secondary" onclick="SettingsModule.copySecret()"><i class="bi bi-clipboard"></i></button>' +
                            '</div>' +
                            '<div class="form-text">' + __('twofa.manualEntryHint') + '</div>' +
                        '</div>' +
                        '<hr>' +
                        '<h6 class="fw-bold mb-2"><i class="bi bi-2-circle me-1"></i>' + __('twofa.step2') + '</h6>' +
                        '<div class="mb-3">' +
                            '<input type="text" class="form-control form-control-lg text-center font-monospace" id="twofa-verify-code" ' +
                                'maxlength="6" pattern="[0-9]{6}" inputmode="numeric" placeholder="------" style="letter-spacing:0.5em;font-size:1.5rem;" ' +
                                'oninput="this.value=this.value.replace(/[^0-9]/g,\'\')" ' +
                                'onkeypress="if(event.key===\'Enter\')SettingsModule.verifyTwoFactor()">' +
                            '<div class="form-text">' + __('twofa.codeHint') + '</div>' +
                        '</div>' +
                        '<div class="d-flex gap-2">' +
                            '<button class="btn btn-success btn-sm" id="twofa-verify-btn" onclick="SettingsModule.verifyTwoFactor()">' +
                                '<i class="bi bi-check-lg me-1"></i>' + __('twofa.verifyAndEnable') +
                            '</button>' +
                            '<button class="btn btn-outline-secondary btn-sm" onclick="SettingsModule.cancelTwoFactorSetup()">' +
                                '<i class="bi bi-x-lg me-1"></i>' + __('common.cancel') +
                            '</button>' +
                        '</div>' +
                    '</div>' +
                '</div>';
        },

        generateQRCode: function(text) {
            var container = document.getElementById('twofa-qr-container');
            if (!container) return;
            container.innerHTML = '';
            if (typeof QRCode === 'undefined') {
                // Fallback: show otpauth URI as link if QR library not loaded
                container.innerHTML = '<div class="d-flex align-items-center justify-content-center h-100 text-center p-2">' +
                    '<a href="' + this.esc(text) + '" class="small text-break">' + __('twofa.qrNotLoaded') + '</a></div>';
                return;
            }
            try {
                new QRCode(container, { text: text, width: 200, height: 200, correctLevel: QRCode.CorrectLevel.M });
            } catch (_) {
                container.innerHTML = '<div class="d-flex align-items-center justify-content-center h-100 text-center p-2">' +
                    '<span class="small text-muted">' + __('twofa.qrFailed') + '</span></div>';
            }
        },

        copySecret: function() {
            var input = document.getElementById('twofa-secret');
            if (!input) return;
            input.select();
            input.setSelectionRange(0, 99999);
            try { document.execCommand('copy'); window.DashboardModals.showInfo(__('twofa.secretCopied')); }
            catch (_) { window.DashboardModals.showWarning(__('twofa.copyFailed')); }
        },

        startTwoFactorSetup: async function() {
            var container = document.getElementById('twofa-content');
            container.innerHTML = '<div class="loading-state"><div class="loading-spinner"></div><div class="loading-text">' + __('twofa.generating') + '</div></div>';
            try {
                var data = await window.DashboardApi.get('/api/2fa/setup');
                this.twoFactorState.setupData = data;
                this.renderTwoFactor(container, document.getElementById('twofa-status-badge'));
            } catch (error) {
                container.innerHTML = '<div class="alert alert-danger py-2">' + __('twofa.generateFailed') + ': ' + this.esc(error.message || String(error)) + '</div>';
            }
        },

        cancelTwoFactorSetup: function() {
            this.twoFactorState.setupData = null;
            this.renderTwoFactor(document.getElementById('twofa-content'), document.getElementById('twofa-status-badge'));
        },

        verifyTwoFactor: async function() {
            var codeInput = document.getElementById('twofa-verify-code');
            var btn = document.getElementById('twofa-verify-btn');
            if (!codeInput || !this.twoFactorState.setupData) return;

            var code = codeInput.value.trim();
            if (code.length !== 6 || !/^\d{6}$/.test(code)) {
                window.DashboardModals.showWarning(__('pluginPage.validationError'));
                codeInput.focus();
                return;
            }

            try {
                btn.disabled = true;
                btn.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>' + __('settings.saving');
                await window.DashboardApi.post('/api/2fa/verify', {
                    code: code,
                    secret: this.twoFactorState.setupData.secret
                });
                this.twoFactorState.enabled = true;
                this.twoFactorState.setupData = null;
                window.DashboardModals.showSuccess(__('twofa.enabledSuccess'));
                this.renderTwoFactor(document.getElementById('twofa-content'), document.getElementById('twofa-status-badge'));
            } catch (error) {
                window.DashboardModals.showError(__('twofa.verifyFailed') + ': ' + (error.message || error));
                btn.disabled = false;
                btn.innerHTML = '<i class="bi bi-check-lg me-1"></i>' + __('twofa.verifyAndEnable');
            }
        },

        disableTwoFactor: function() {
            var self = this;
            window.DashboardModals.showConfirm(
                __('twofa.disableConfirm'),
                async function() {
                    try {
                        await window.DashboardApi.post('/api/2fa/disable', {});
                        self.twoFactorState.enabled = false;
                        self.twoFactorState.setupData = null;
                        window.DashboardModals.showSuccess(__('twofa.disabledSuccess'));
                        self.renderTwoFactor(document.getElementById('twofa-content'), document.getElementById('twofa-status-badge'));
                    } catch (error) {
                        window.DashboardModals.showError(__('twofa.disableFailed') + ': ' + (error.message || error));
                    }
                },
                null,
                { title: __('twofa.disableTitle'), danger: true }
            );
        },

        // ─── Utilities ───

        esc: function(str) {
            if (!str) return '';
            return String(str).replace(/[&<>"']/g, function(c) {
                return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
            });
        }
    };

    window.SettingsModule = SettingsModule;
})();
