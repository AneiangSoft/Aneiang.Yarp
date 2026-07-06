/**
 * Dashboard Log Settings Module — UI for configuring log persistence, capture, sampling, and retention.
 * Load/save/reset via DashboardApi endpoints with validation.
 */
(function() {
    'use strict';

    window.DashboardLogSettings = window.DashboardLogSettings || {};

    var _state = {
        loading: false,
        saving: false,
        data: null,   // LogSettingsData from server
        dirty: false  // whether local edits differ from server data
    };

    // ── i18n helper ──
    function __(key) {
        return (window.DashboardI18n && DashboardI18n.translate) ? DashboardI18n.translate(key) : key;
    }

    // ── Init ──
    window.DashboardLogSettings.init = async function() {
        await loadSettings();
        bindEvents();
    };

    // ── Load ──
    async function loadSettings() {
        _state.loading = true;
        renderLoading();

        try {
            var data = await DashboardApi.endpoints.getLogSettings();
            _state.data = data;
            _state.dirty = false;
            renderSettings(data);
        } catch (e) {
            console.error('[LogSettings] Load failed:', e);
            if (window.DashboardModals) {
                DashboardModals.showError(__('config.logSettings.loading') + ': ' + (e.message || 'Unknown error'));
            }
            renderError();
        } finally {
            _state.loading = false;
        }
    }

    // ── Render ──
    function renderLoading() {
        var container = document.getElementById('log-settings-content');
        if (!container) return;
        container.innerHTML = '<div class="text-center py-4"><div class="spinner-border spinner-border-sm text-primary me-2"></div>' + __('config.logSettings.loading') + '</div>';
    }

    function renderError() {
        var container = document.getElementById('log-settings-content');
        if (!container) return;
        container.innerHTML = '<div class="text-danger text-center py-4"><i class="bi bi-exclamation-circle me-1"></i>' + __('config.logSettings.saveFailed') + '</div>';
    }

    function renderSettings(data) {
        var container = document.getElementById('log-settings-content');
        if (!container) return;

        var locale = (window.__dashboard && window.__dashboard.locale) || 'zh-CN';
        var levelOptions = ['Debug', 'Information', 'Warning', 'Error', 'Critical'];

        container.innerHTML = `
            <!-- Persistence Section -->
            <div class="mb-4">
                <h6 class="text-muted small mb-3"><i class="bi bi-archive me-1" style="color:#6366f1;"></i> ${__('config.logSettings.persistence')}</h6>
                <div class="row g-3">
                    <div class="col-md-6">
                        <div class="form-check form-switch mb-2">
                            <input class="form-check-input" type="checkbox" id="ls-persistence-enabled" ${data.logPersistenceEnabled ? 'checked' : ''} />
                            <label class="form-check-label" for="ls-persistence-enabled">${__('config.logSettings.persistenceEnabled')}</label>
                        </div>
                        <p class="text-muted small mb-0">${__('config.logSettings.persistenceEnabledDesc')}</p>
                    </div>
                    <div class="col-md-3">
                        <label class="form-label small">${__('config.logSettings.metaRetention')}</label>
                        <div class="input-group input-group-sm">
                            <input type="number" class="form-control" id="ls-meta-retention" value="${data.logMetaRetentionDays}" min="1" max="365" />
                            <span class="input-group-text">${__('config.logSettings.days')}</span>
                        </div>
                        <p class="text-muted small mt-1 mb-0">${__('config.logSettings.metaRetentionDesc')}</p>
                    </div>
                    <div class="col-md-3">
                        <label class="form-label small">${__('config.logSettings.bodyRetention')}</label>
                        <div class="input-group input-group-sm">
                            <input type="number" class="form-control" id="ls-body-retention" value="${data.logBodyRetentionDays}" min="1" max="${data.logMetaRetentionDays}" />
                            <span class="input-group-text">${__('config.logSettings.days')}</span>
                        </div>
                        <p class="text-muted small mt-1 mb-0">${__('config.logSettings.bodyRetentionDesc')}</p>
                    </div>
                </div>
            </div>

            <!-- Capture Section -->
            <div class="mb-4">
                <h6 class="text-muted small mb-3"><i class="bi bi-eye me-1" style="color:#f59e0b;"></i> ${__('config.logSettings.capture')}</h6>
                <div class="row g-3">
                    <div class="col-md-4">
                        <div class="form-check form-switch mb-2">
                            <input class="form-check-input" type="checkbox" id="ls-req-body-capture" ${data.enableProxyRequestBodyCapture ? 'checked' : ''} />
                            <label class="form-check-label" for="ls-req-body-capture">${__('config.logSettings.reqBodyCapture')}</label>
                        </div>
                        <p class="text-muted small mb-0">${__('config.logSettings.reqBodyCaptureDesc')}</p>
                    </div>
                    <div class="col-md-4">
                        <div class="form-check form-switch mb-2">
                            <input class="form-check-input" type="checkbox" id="ls-res-body-capture" ${data.enableProxyResponseBodyCapture ? 'checked' : ''} />
                            <label class="form-check-label" for="ls-res-body-capture">${__('config.logSettings.resBodyCapture')}</label>
                        </div>
                        <p class="text-muted small mb-0">${__('config.logSettings.resBodyCaptureDesc')}</p>
                    </div>
                    <div class="col-md-4">
                        <label class="form-label small">${__('config.logSettings.maxBodyLength')}</label>
                        <div class="input-group input-group-sm">
                            <input type="number" class="form-control" id="ls-max-body-length" value="${data.logMaxBodyLength}" min="256" max="1048576" step="256" />
                            <span class="input-group-text">${__('config.logSettings.bytes')}</span>
                        </div>
                        <p class="text-muted small mt-1 mb-0">${__('config.logSettings.maxBodyLengthDesc')}</p>
                    </div>
                </div>
            </div>

            <!-- Sampling Section -->
            <div class="mb-4">
                <h6 class="text-muted small mb-3"><i class="bi bi-percent me-1" style="color:#22c55e;"></i> ${__('config.logSettings.sampling')}</h6>
                <div class="row g-3">
                    <div class="col-md-4">
                        <div class="form-check form-switch mb-2">
                            <input class="form-check-input" type="checkbox" id="ls-sampling-enabled" ${data.enableLogSampling ? 'checked' : ''} />
                            <label class="form-check-label" for="ls-sampling-enabled">${__('config.logSettings.samplingEnabled')}</label>
                        </div>
                        <p class="text-muted small mb-0">${__('config.logSettings.samplingEnabledDesc')}</p>
                    </div>
                    <div class="col-md-4" id="ls-sampling-rate-group" style="opacity: ${data.enableLogSampling ? '1' : '0.5'};">
                        <label class="form-label small">${__('config.logSettings.samplingRate')}</label>
                        <input type="range" class="form-range" id="ls-sampling-rate" min="0" max="1" step="0.05" value="${data.logSamplingRate}" />
                        <div class="d-flex justify-content-between small text-muted">
                            <span>0%</span>
                            <span id="ls-sampling-rate-display">${Math.round(data.logSamplingRate * 100)}%</span>
                            <span>100%</span>
                        </div>
                        <p class="text-muted small mb-0">${__('config.logSettings.samplingRateDesc')}</p>
                    </div>
                    <div class="col-md-4">
                        <div class="form-check form-switch mb-2">
                            <input class="form-check-input" type="checkbox" id="ls-errors-only" ${data.logErrorsOnly ? 'checked' : ''} />
                            <label class="form-check-label" for="ls-errors-only">${__('config.logSettings.errorsOnly')}</label>
                        </div>
                        <p class="text-muted small mb-0">${__('config.logSettings.errorsOnlyDesc')}</p>
                    </div>
                </div>
            </div>

            <!-- Filter Section -->
            <div class="mb-4">
                <h6 class="text-muted small mb-3"><i class="bi bi-filter me-1" style="color:#3b82f6;"></i> 日志过滤</h6>
                <div class="row g-3">
                    <div class="col-md-4">
                        <label class="form-label small">${__('config.logSettings.minLogLevel')}</label>
                        <select class="form-select form-select-sm" id="ls-min-log-level">
                            ${levelOptions.map(l => `<option value="${l}" ${data.minLogLevel === l ? 'selected' : ''}>${l}</option>`).join('')}
                        </select>
                        <p class="text-muted small mt-1 mb-0">${__('config.logSettings.minLogLevelDesc')}</p>
                    </div>
                </div>
            </div>

            <!-- Buffer Section -->
            <div class="mb-4">
                <h6 class="text-muted small mb-3"><i class="bi bi-memory me-1" style="color:#ef4444;"></i> ${__('config.logSettings.buffer')}</h6>
                <div class="row g-3">
                    <div class="col-md-4">
                        <label class="form-label small">${__('config.logSettings.bufferCapacity')}</label>
                        <div class="input-group input-group-sm">
                            <input type="number" class="form-control" id="ls-buffer-capacity" value="${data.logBufferCapacity}" min="16" max="10000" />
                            <span class="input-group-text">${__('index.log.entries') || 'entries'}</span>
                        </div>
                        <p class="text-muted small mt-1 mb-0"><span class="badge bg-warning text-dark">⚠️</span> ${__('config.logSettings.bufferCapacityDesc')}</p>
                    </div>
                </div>
            </div>

            <!-- Actions -->
            <div class="d-flex justify-content-between align-items-center pt-3 border-top">
                <button class="btn btn-sm btn-outline-secondary" onclick="DashboardLogSettings.resetSettings()">
                    <i class="bi bi-arrow-counterclockwise me-1"></i>${__('config.logSettings.reset')}
                </button>
                <button class="btn btn-sm btn-primary" id="ls-save-btn" onclick="DashboardLogSettings.saveSettings()">
                    <i class="bi bi-check-lg me-1"></i>${__('config.logSettings.save')}
                </button>
            </div>
        `;

        // Bind dynamic UI behavior
        bindDynamicBehavior();
    }

    // ── Dynamic UI ──
    function bindDynamicBehavior() {
        // Sampling rate display + opacity toggle
        var samplingToggle = document.getElementById('ls-sampling-enabled');
        var samplingRateGroup = document.getElementById('ls-sampling-rate-group');
        var samplingRate = document.getElementById('ls-sampling-rate');
        var samplingRateDisplay = document.getElementById('ls-sampling-rate-display');

        if (samplingToggle) {
            samplingToggle.addEventListener('change', function() {
                if (samplingRateGroup) samplingRateGroup.style.opacity = this.checked ? '1' : '0.5';
            });
        }

        if (samplingRate) {
            samplingRate.addEventListener('input', function() {
                if (samplingRateDisplay) samplingRateDisplay.textContent = Math.round(this.value * 100) + '%';
            });
        }

        // Enforce body ≤ meta
        var metaRetention = document.getElementById('ls-meta-retention');
        var bodyRetention = document.getElementById('ls-body-retention');

        if (metaRetention) {
            metaRetention.addEventListener('change', function() {
                if (bodyRetention) {
                    bodyRetention.max = this.value;
                    if (parseInt(bodyRetention.value) > parseInt(this.value)) {
                        bodyRetention.value = this.value;
                    }
                }
            });
        }
    }

    // ── Events ──
    function bindEvents() {
        // Mark dirty on any input change
        var container = document.getElementById('log-settings-content');
        if (container) {
            container.addEventListener('change', markDirty);
            container.addEventListener('input', markDirty);
        }
    }

    function markDirty() {
        _state.dirty = true;
    }

    // ── Collect form data ──
    function collectFormData() {
        return {
            logPersistenceEnabled: document.getElementById('ls-persistence-enabled')?.checked,
            logMetaRetentionDays: parseInt(document.getElementById('ls-meta-retention')?.value) || undefined,
            logBodyRetentionDays: parseInt(document.getElementById('ls-body-retention')?.value) || undefined,
            enableProxyRequestBodyCapture: document.getElementById('ls-req-body-capture')?.checked,
            enableProxyResponseBodyCapture: document.getElementById('ls-res-body-capture')?.checked,
            logMaxBodyLength: parseInt(document.getElementById('ls-max-body-length')?.value) || undefined,
            enableLogSampling: document.getElementById('ls-sampling-enabled')?.checked,
            logSamplingRate: parseFloat(document.getElementById('ls-sampling-rate')?.value) || undefined,
            logErrorsOnly: document.getElementById('ls-errors-only')?.checked,
            minLogLevel: document.getElementById('ls-min-log-level')?.value || undefined,
            logBufferCapacity: parseInt(document.getElementById('ls-buffer-capacity')?.value) || undefined
        };
    }

    // ── Validate ──
    function validate(data) {
        var errors = [];

        if (data.logMetaRetentionDays != null && (data.logMetaRetentionDays < 1 || data.logMetaRetentionDays > 365))
            errors.push('LogMetaRetentionDays: 1-365');

        if (data.logBodyRetentionDays != null && data.logMetaRetentionDays != null && data.logBodyRetentionDays > data.logMetaRetentionDays)
            errors.push(__('config.logSettings.bodyExceedsMeta'));

        if (data.logSamplingRate != null && (data.logSamplingRate < 0 || data.logSamplingRate > 1))
            errors.push('LogSamplingRate: 0.0-1.0');

        if (data.logMaxBodyLength != null && (data.logMaxBodyLength < 256 || data.logMaxBodyLength > 1048576))
            errors.push('LogMaxBodyLength: 256-1048576');

        if (data.logBufferCapacity != null && data.logBufferCapacity < 16)
            errors.push('LogBufferCapacity: ≥16');

        return errors;
    }

    // ── Save ──
    window.DashboardLogSettings.saveSettings = async function() {
        var data = collectFormData();
        var errors = validate(data);

        if (errors.length > 0) {
            if (window.DashboardModals) {
                DashboardModals.showWarning(__('config.logSettings.validationError') + ': ' + errors.join(', '));
            }
            return;
        }

        _state.saving = true;
        var saveBtn = document.getElementById('ls-save-btn');
        if (saveBtn) { saveBtn.disabled = true; saveBtn.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>' + __('config.logSettings.save'); }

        try {
            var result = await DashboardApi.endpoints.updateLogSettings(data);
            _state.data = result;
            _state.dirty = false;
            renderSettings(result);
            if (window.DashboardModals) {
                DashboardModals.showSuccess(__('config.logSettings.saved'));
            }
        } catch (e) {
            console.error('[LogSettings] Save failed:', e);
            if (window.DashboardModals) {
                DashboardModals.showError(__('config.logSettings.saveFailed') + ': ' + (e.message || 'Unknown error'));
            }
        } finally {
            _state.saving = false;
            if (saveBtn) { saveBtn.disabled = false; saveBtn.innerHTML = '<i class="bi bi-check-lg me-1"></i>' + __('config.logSettings.save'); }
        }
    };

    // ── Reset ──
    window.DashboardLogSettings.resetSettings = async function() {
        if (window.DashboardModals) {
            DashboardModals.showConfirm(
                __('config.logSettings.resetConfirm'),
                async function() {
                    try {
                        var result = await DashboardApi.put('/api/logs/settings/reset', {});
                        _state.data = result;
                        _state.dirty = false;
                        renderSettings(result);
                        DashboardModals.showSuccess(__('config.logSettings.resetSuccess'));
                    } catch (e) {
                        console.error('[LogSettings] Reset failed:', e);
                        DashboardModals.showError(__('config.logSettings.resetFailed') + ': ' + (e.message || 'Unknown error'));
                    }
                }, null, { danger: true }
            );
        }
    };

})();
