/**
 * Two-Factor Authentication (2FA) Module
 * Self-service TOTP binding: enable / disable / QR setup.
 * Hosted on the Settings page ("Account Security" card).
 */
(function() {
    'use strict';

    var TwoFactorModule = {
        twoFactorState: { enabled: false, loading: false, setupData: null },

        load: async function() {
            var container = document.getElementById('twofa-content');
            var badge = document.getElementById('twofa-status-badge');
            if (!container) return;
            try {
                var resp = await window.DashboardApi.get('/api/2fa/status');
                this.twoFactorState.enabled = resp.enabled;
                this.twoFactorState.setupData = null;
                this.render(container, badge);
            } catch (error) {
                container.innerHTML = '<div class="alert alert-danger py-2">' + __('twofa.loadFailed') + ': ' + this.esc(error.message || String(error)) + '</div>';
                if (badge) { badge.className = 'badge bg-danger'; badge.textContent = __('common.loadFailed'); }
            }
        },

        render: function(container, badge) {
            var enabled = this.twoFactorState.enabled;
            var setupData = this.twoFactorState.setupData;

            // Update badge
            if (badge) {
                badge.className = 'badge ' + (enabled ? 'bg-success' : 'bg-secondary');
                badge.textContent = enabled ? __('twofa.enabled') : __('twofa.disabled');
            }

            if (setupData) {
                // Setup mode: show QR + verification
                container.innerHTML = this.renderSetup(setupData);
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
                        '<button class="btn btn-outline-danger btn-sm" onclick="TwoFactorModule.disable()">' +
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
                        '<button class="btn btn-primary btn-sm" onclick="TwoFactorModule.startSetup()">' +
                            '<i class="bi bi-shield-lock-fill me-1"></i>' + __('twofa.enable') +
                        '</button>' +
                    '</div>';
            }
        },

        renderSetup: function(data) {
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
                                '<button class="btn btn-outline-secondary" onclick="TwoFactorModule.copySecret()"><i class="bi bi-clipboard"></i></button>' +
                            '</div>' +
                            '<div class="form-text">' + __('twofa.manualEntryHint') + '</div>' +
                        '</div>' +
                        '<hr>' +
                        '<h6 class="fw-bold mb-2"><i class="bi bi-2-circle me-1"></i>' + __('twofa.step2') + '</h6>' +
                        '<div class="mb-3">' +
                            '<input type="text" class="form-control form-control-lg text-center font-monospace" id="twofa-verify-code" ' +
                                'maxlength="6" pattern="[0-9]{6}" inputmode="numeric" placeholder="------" style="letter-spacing:0.5em;font-size:1.5rem;" ' +
                                'oninput="this.value=this.value.replace(/[^0-9]/g,\'\')" ' +
                                'onkeypress="if(event.key===\'Enter\')TwoFactorModule.verify()">' +
                            '<div class="form-text">' + __('twofa.codeHint') + '</div>' +
                        '</div>' +
                        '<div class="d-flex gap-2">' +
                            '<button class="btn btn-success btn-sm" id="twofa-verify-btn" onclick="TwoFactorModule.verify()">' +
                                '<i class="bi bi-check-lg me-1"></i>' + __('twofa.verifyAndEnable') +
                            '</button>' +
                            '<button class="btn btn-outline-secondary btn-sm" onclick="TwoFactorModule.cancelSetup()">' +
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
            window.DashboardUtils.copyToClipboard(input.value).then(function(success) {
                if (success) {
                    window.DashboardModals.showInfo(__('twofa.secretCopied'));
                } else {
                    window.DashboardModals.showWarning(__('twofa.copyFailed'));
                }
            });
        },

        startSetup: async function() {
            var container = document.getElementById('twofa-content');
            container.innerHTML = '<div class="loading-state"><div class="loading-spinner"></div><div class="loading-text">' + __('twofa.generating') + '</div></div>';
            try {
                var data = await window.DashboardApi.get('/api/2fa/setup');
                this.twoFactorState.setupData = data;
                this.render(container, document.getElementById('twofa-status-badge'));
            } catch (error) {
                container.innerHTML = '<div class="alert alert-danger py-2">' + __('twofa.generateFailed') + ': ' + this.esc(error.message || String(error)) + '</div>';
            }
        },

        cancelSetup: function() {
            this.twoFactorState.setupData = null;
            this.render(document.getElementById('twofa-content'), document.getElementById('twofa-status-badge'));
        },

        verify: async function() {
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
                this.render(document.getElementById('twofa-content'), document.getElementById('twofa-status-badge'));
            } catch (error) {
                window.DashboardModals.showError(__('twofa.verifyFailed') + ': ' + (error.message || error));
                btn.disabled = false;
                btn.innerHTML = '<i class="bi bi-check-lg me-1"></i>' + __('twofa.verifyAndEnable');
            }
        },

        disable: function() {
            var self = this;
            window.DashboardModals.showConfirm(
                __('twofa.disableConfirm'),
                async function() {
                    try {
                        await window.DashboardApi.post('/api/2fa/disable', {});
                        self.twoFactorState.enabled = false;
                        self.twoFactorState.setupData = null;
                        window.DashboardModals.showSuccess(__('twofa.disabledSuccess'));
                        self.render(document.getElementById('twofa-content'), document.getElementById('twofa-status-badge'));
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

    window.TwoFactorModule = TwoFactorModule;
})();
