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
        if (window.DashboardI18n && DashboardI18n.t) return DashboardI18n.t(key);
        if (window.I18N && I18N[key]) return I18N[key];
        return key;
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
        var samplingPct = Math.round(data.logSamplingRate * 100);

        container.innerHTML = `
            <style>
                /* ── Log Settings Layout ── */
                .ls-card {
                    border-left: 3px solid #cbd5e1;
                    border-radius: 6px;
                    background: #f8fafc;
                    padding: 14px 18px 16px;
                    margin-bottom: 14px;
                }
                .ls-card-head {
                    display: flex;
                    align-items: center;
                    gap: 7px;
                    font-size: 12.5px;
                    font-weight: 600;
                    color: #475569;
                    letter-spacing: .02em;
                    text-transform: uppercase;
                    margin-bottom: 12px;
                    padding-bottom: 8px;
                    border-bottom: 1px solid #e2e8f0;
                }
                .ls-card-head .ls-icon {
                    width: 22px; height: 22px;
                    border-radius: 5px;
                    display: inline-flex;
                    align-items: center;
                    justify-content: center;
                    font-size: 13px;
                    color: #fff;
                    flex-shrink: 0;
                }
                .ls-row {
                    display: flex;
                    gap: 16px;
                    flex-wrap: wrap;
                }
                .ls-cell {
                    flex: 1 1 0;
                    min-width: 180px;
                }
                .ls-cell-2 { flex: 2 1 0; }

                /* Label */
                .ls-lbl {
                    display: block;
                    font-size: 12px;
                    font-weight: 600;
                    color: #334155;
                    margin-bottom: 5px;
                    line-height: 1.3;
                    min-height: 16px;
                }

                /* Control area — fixed height so all controls align */
                .ls-ctrl {
                    min-height: 31px;
                    display: flex;
                    align-items: center;
                }

                /* Description */
                .ls-desc {
                    font-size: 11px;
                    color: #94a3b8;
                    line-height: 1.45;
                    margin-top: 5px;
                }

                /* Switch as control */
                .ls-sw {
                    display: flex;
                    align-items: center;
                    gap: 8px;
                }
                .ls-sw .form-check-input {
                    width: 36px; height: 18px;
                    cursor: pointer;
                    flex-shrink: 0;
                    margin: 0;
                }
                .ls-sw .form-check-label {
                    font-size: 12px;
                    color: #475569;
                    cursor: pointer;
                    line-height: 1.3;
                }

                /* Input group unit suffix */
                .ls-unit {
                    font-size: 11px;
                    color: #64748b;
                    min-width: 38px;
                    text-align: center;
                }

                /* Range / slider */
                .ls-range-head {
                    display: flex;
                    justify-content: space-between;
                    align-items: center;
                    margin-bottom: 5px;
                }
                .ls-range-val {
                    font-size: 13px;
                    font-weight: 700;
                    color: #6366f1;
                }
                .ls-range-ticks {
                    display: flex;
                    justify-content: space-between;
                    font-size: 10px;
                    color: #94a3b8;
                    margin-top: 1px;
                }

                /* Warn badge */
                .ls-warn {
                    display: inline-flex;
                    align-items: center;
                    gap: 3px;
                    font-size: 10.5px;
                    color: #92400e;
                    background: #fef3c7;
                    border-radius: 3px;
                    padding: 1px 6px;
                    margin-right: 4px;
                    line-height: 1.4;
                }

                /* Action bar */
                .ls-bar {
                    display: flex;
                    justify-content: space-between;
                    align-items: center;
                    padding-top: 14px;
                    margin-top: 2px;
                    border-top: 1px solid #e2e8f0;
                }
            </style>

            <!-- ── Persistence ── -->
            <div class="ls-card" style="border-left-color:#6366f1">
                <div class="ls-card-head">
                    <span class="ls-icon" style="background:#6366f1"><i class="bi bi-archive"></i></span>
                    ${__('config.logSettings.persistence')}
                </div>
                <div class="ls-row">
                    <div class="ls-cell ls-cell-2">
                        <div class="ls-lbl">${__('config.logSettings.persistenceEnabled')}</div>
                        <div class="ls-ctrl">
                            <div class="ls-sw">
                                <input class="form-check-input" type="checkbox" role="switch" id="ls-persistence-enabled" ${data.logPersistenceEnabled ? 'checked' : ''} />
                                <label class="form-check-label" for="ls-persistence-enabled">${data.logPersistenceEnabled ? 'ON' : 'OFF'}</label>
                            </div>
                        </div>
                        <div class="ls-desc">${__('config.logSettings.persistenceEnabledDesc')}</div>
                    </div>
                    <div class="ls-cell">
                        <label class="ls-lbl" for="ls-meta-retention">${__('config.logSettings.metaRetention')}</label>
                        <div class="ls-ctrl">
                            <div class="input-group input-group-sm">
                                <input type="number" class="form-control" id="ls-meta-retention" value="${data.logMetaRetentionDays}" min="1" max="365" />
                                <span class="input-group-text ls-unit">${__('config.logSettings.days')}</span>
                            </div>
                        </div>
                        <div class="ls-desc">${__('config.logSettings.metaRetentionDesc')}</div>
                    </div>
                    <div class="ls-cell">
                        <label class="ls-lbl" for="ls-body-retention">${__('config.logSettings.bodyRetention')}</label>
                        <div class="ls-ctrl">
                            <div class="input-group input-group-sm">
                                <input type="number" class="form-control" id="ls-body-retention" value="${data.logBodyRetentionDays}" min="1" max="${data.logMetaRetentionDays}" />
                                <span class="input-group-text ls-unit">${__('config.logSettings.days')}</span>
                            </div>
                        </div>
                        <div class="ls-desc">${__('config.logSettings.bodyRetentionDesc')}</div>
                    </div>
                </div>
            </div>

            <!-- ── Capture ── -->
            <div class="ls-card" style="border-left-color:#f59e0b">
                <div class="ls-card-head">
                    <span class="ls-icon" style="background:#f59e0b"><i class="bi bi-eye"></i></span>
                    ${__('config.logSettings.capture')}
                </div>
                <div class="ls-row">
                    <div class="ls-cell">
                        <div class="ls-lbl">${__('config.logSettings.reqBodyCapture')}</div>
                        <div class="ls-ctrl">
                            <div class="ls-sw">
                                <input class="form-check-input" type="checkbox" role="switch" id="ls-req-body-capture" ${data.enableProxyRequestBodyCapture ? 'checked' : ''} />
                                <label class="form-check-label" for="ls-req-body-capture">${data.enableProxyRequestBodyCapture ? 'ON' : 'OFF'}</label>
                            </div>
                        </div>
                        <div class="ls-desc">${__('config.logSettings.reqBodyCaptureDesc')}</div>
                    </div>
                    <div class="ls-cell">
                        <div class="ls-lbl">${__('config.logSettings.resBodyCapture')}</div>
                        <div class="ls-ctrl">
                            <div class="ls-sw">
                                <input class="form-check-input" type="checkbox" role="switch" id="ls-res-body-capture" ${data.enableProxyResponseBodyCapture ? 'checked' : ''} />
                                <label class="form-check-label" for="ls-res-body-capture">${data.enableProxyResponseBodyCapture ? 'ON' : 'OFF'}</label>
                            </div>
                        </div>
                        <div class="ls-desc">${__('config.logSettings.resBodyCaptureDesc')}</div>
                    </div>
                    <div class="ls-cell">
                        <label class="ls-lbl" for="ls-max-body-length">${__('config.logSettings.maxBodyLength')}</label>
                        <div class="ls-ctrl">
                            <div class="input-group input-group-sm">
                                <input type="number" class="form-control" id="ls-max-body-length" value="${data.logMaxBodyLength}" min="256" max="1048576" step="256" />
                                <span class="input-group-text ls-unit">${__('config.logSettings.bytes')}</span>
                            </div>
                        </div>
                        <div class="ls-desc">${__('config.logSettings.maxBodyLengthDesc')}</div>
                    </div>
                </div>
            </div>

            <!-- ── Sampling ── -->
            <div class="ls-card" style="border-left-color:#22c55e">
                <div class="ls-card-head">
                    <span class="ls-icon" style="background:#22c55e"><i class="bi bi-percent"></i></span>
                    ${__('config.logSettings.sampling')}
                </div>
                <div class="ls-row">
                    <div class="ls-cell">
                        <div class="ls-lbl">${__('config.logSettings.samplingEnabled')}</div>
                        <div class="ls-ctrl">
                            <div class="ls-sw">
                                <input class="form-check-input" type="checkbox" role="switch" id="ls-sampling-enabled" ${data.enableLogSampling ? 'checked' : ''} />
                                <label class="form-check-label" for="ls-sampling-enabled">${data.enableLogSampling ? 'ON' : 'OFF'}</label>
                            </div>
                        </div>
                        <div class="ls-desc">${__('config.logSettings.samplingEnabledDesc')}</div>
                    </div>
                    <div class="ls-cell ls-cell-2" id="ls-sampling-rate-group" style="opacity:${data.enableLogSampling ? '1' : '0.45'};transition:opacity .2s">
                        <div class="ls-range-head">
                            <span class="ls-lbl" style="margin-bottom:0">${__('config.logSettings.samplingRate')}</span>
                            <span class="ls-range-val" id="ls-sampling-rate-display">${samplingPct}%</span>
                        </div>
                        <div class="ls-ctrl">
                            <input type="range" class="form-range" id="ls-sampling-rate" min="0" max="1" step="0.05" value="${data.logSamplingRate}" style="width:100%" />
                        </div>
                        <div class="ls-range-ticks"><span>0%</span><span>50%</span><span>100%</span></div>
                        <div class="ls-desc">${__('config.logSettings.samplingRateDesc')}</div>
                    </div>
                    <div class="ls-cell">
                        <div class="ls-lbl">${__('config.logSettings.errorsOnly')}</div>
                        <div class="ls-ctrl">
                            <div class="ls-sw">
                                <input class="form-check-input" type="checkbox" role="switch" id="ls-errors-only" ${data.logErrorsOnly ? 'checked' : ''} />
                                <label class="form-check-label" for="ls-errors-only">${data.logErrorsOnly ? 'ON' : 'OFF'}</label>
                            </div>
                        </div>
                        <div class="ls-desc">${__('config.logSettings.errorsOnlyDesc')}</div>
                    </div>
                </div>
            </div>

            <!-- ── Filter & Buffer ── -->
            <div class="ls-card" style="border-left-color:#3b82f6">
                <div class="ls-card-head">
                    <span class="ls-icon" style="background:#3b82f6"><i class="bi bi-sliders"></i></span>
                    ${__('config.logSettings.minLogLevel')} &amp; ${__('config.logSettings.buffer')}
                </div>
                <div class="ls-row">
                    <div class="ls-cell">
                        <label class="ls-lbl" for="ls-min-log-level">${__('config.logSettings.minLogLevel')}</label>
                        <div class="ls-ctrl">
                            <select class="form-select form-select-sm" id="ls-min-log-level">
                                ${levelOptions.map(l => `<option value="${l}" ${data.minLogLevel === l ? 'selected' : ''}>${l}</option>`).join('')}
                            </select>
                        </div>
                        <div class="ls-desc">${__('config.logSettings.minLogLevelDesc')}</div>
                    </div>
                    <div class="ls-cell ls-cell-2">
                        <label class="ls-lbl" for="ls-buffer-capacity">${__('config.logSettings.bufferCapacity')}</label>
                        <div class="ls-ctrl">
                            <div class="input-group input-group-sm">
                                <input type="number" class="form-control" id="ls-buffer-capacity" value="${data.logBufferCapacity}" min="16" max="10000" />
                                <span class="input-group-text ls-unit">${__('index.log.entries') || 'entries'}</span>
                            </div>
                        </div>
                        <div class="ls-desc"><span class="ls-warn">⚠ ${__('config.logSettings.bufferCapacityDesc')}</span></div>
                    </div>
                </div>
            </div>

            <!-- ── Actions ── -->
            <div class="ls-bar">
                <button class="btn btn-sm btn-outline-secondary" onclick="DashboardLogSettings.resetSettings()">
                    <i class="bi bi-arrow-counterclockwise me-1"></i>${__('config.logSettings.reset')}
                </button>
                <button class="btn btn-sm btn-primary px-4" id="ls-save-btn" onclick="DashboardLogSettings.saveSettings()">
                    <i class="bi bi-check-lg me-1"></i>${__('config.logSettings.save')}
                </button>
            </div>
        `;

        bindDynamicBehavior();
    }

    // ── Dynamic UI ──
    function bindDynamicBehavior() {
        // Helper: toggle ON/OFF label text on switch change
        function bindSwitchLabel(checkboxId) {
            var cb = document.getElementById(checkboxId);
            if (!cb) return;
            var lbl = cb.parentElement.querySelector('.form-check-label');
            if (!lbl) return;
            cb.addEventListener('change', function() {
                lbl.textContent = this.checked ? 'ON' : 'OFF';
            });
        }
        ['ls-persistence-enabled', 'ls-req-body-capture', 'ls-res-body-capture',
         'ls-sampling-enabled', 'ls-errors-only'].forEach(bindSwitchLabel);

        // Sampling rate display + opacity toggle
        var samplingToggle = document.getElementById('ls-sampling-enabled');
        var samplingRateGroup = document.getElementById('ls-sampling-rate-group');
        var samplingRate = document.getElementById('ls-sampling-rate');
        var samplingRateDisplay = document.getElementById('ls-sampling-rate-display');

        if (samplingToggle) {
            samplingToggle.addEventListener('change', function() {
                if (samplingRateGroup) samplingRateGroup.style.opacity = this.checked ? '1' : '0.45';
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
