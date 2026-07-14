/**
 * AI Settings Module - Manages AI configuration page
 *
 * Security: BaseUrl is user-editable for ALL providers (supports API proxies / mirrors).
 * Server-side SSRF validation is always enforced — invalid or dangerous URLs are rejected.
 * Client-side format validation is performed before submission.
 */
(function() {
    'use strict';

    function __(key) {
        if (window.DashboardI18n && DashboardI18n.t) return DashboardI18n.t(key);
        if (window.I18N && I18N[key]) return I18N[key];
        return key;
    }

    // Known provider presets — their BaseUrls are locked server-side
    const PROVIDER_PRESETS = {
        openai:   {
            baseUrl: 'https://api.openai.com/v1',
            chatModel: 'gpt-4o-mini',
            analysisModel: 'gpt-4o-mini',
            knownDomains: ['openai.com']  // matches *.openai.com
        },
        deepseek: {
            baseUrl: 'https://api.deepseek.com',
            chatModel: 'deepseek-v4-flash',
            analysisModel: 'deepseek-v4-pro',
            knownDomains: ['deepseek.com']
        },
        qwen:     {
            baseUrl: 'https://dashscope.aliyuncs.com/compatible-mode/v1',
            chatModel: 'qwen3.7-plus',
            analysisModel: 'qwen3.7-plus',
            knownDomains: ['aliyuncs.com']  // covers dashscope.aliyuncs.com and ws-xxx.*.maas.aliyuncs.com
        }
    };

    /**
     * Check if a URL's hostname belongs to a known provider's domain family.
     * e.g. "ws-abc.cn-beijing.maas.aliyuncs.com" matches qwen's "aliyuncs.com".
     */
    function isKnownProviderDomain(provider, url) {
        if (!url) return false;
        var preset = PROVIDER_PRESETS[provider];
        if (!preset || !preset.knownDomains) return false;
        try {
            var hostname = new URL(url.trim()).hostname.toLowerCase();
            for (var i = 0; i < preset.knownDomains.length; i++) {
                var domain = preset.knownDomains[i].toLowerCase();
                if (hostname === domain || hostname.endsWith('.' + domain)) return true;
            }
        } catch (e) { /* invalid URL */ }
        return false;
    }

    window.AISettings = {
        _status: null,
        _allowCustom: false,  // controlled by server-side AllowCustomProvider config

        init: async function() {
            try {
                var statusResp = await DashboardApi.endpoints.getAIStatus();
                this._status = statusResp?.data || statusResp;
                this._allowCustom = !!(this._status && this._status.allowCustomProvider);
                this._updateStatusBadge();
                this._updateProviderDropdown();
            } catch (e) {
                console.warn('[AI] Status check failed:', e);
            }

            try {
                var settingsResp = await DashboardApi.endpoints.getAISettings();
                var s = settingsResp?.data || settingsResp;
                this._populateForm(s);
            } catch (e) {
                console.warn('[AI] Settings load failed:', e);
            }

            await this.refreshAnalysis();
        },

        _updateStatusBadge: function() {
            var badge = document.getElementById('ai-status-badge');
            if (!badge || !this._status) return;

            if (this._status.available) {
                badge.className = 'badge bg-success ms-2';
                badge.textContent = __('ai.statusReady');
            } else if (this._status.enabled) {
                badge.className = 'badge bg-warning ms-2';
                badge.textContent = __('ai.statusNoKey');
            } else {
                badge.className = 'badge bg-secondary ms-2';
                badge.textContent = __('ai.statusDisabled');
            }
        },

        /**
         * Add or remove the "custom" option from the provider dropdown.
         * Called once during init, based on server-side AllowCustomProvider.
         */
        _updateProviderDropdown: function() {
            var sel = document.getElementById('ai-provider');
            if (!sel) return;

            var existingCustom = sel.querySelector('option[value="custom"]');
            if (this._allowCustom) {
                // Add custom option if not already present
                if (!existingCustom) {
                    var opt = document.createElement('option');
                    opt.value = 'custom';
                    opt.textContent = __('ai.providerCustom') || '自定义 (Custom)';
                    sel.appendChild(opt);
                }
            } else {
                // Remove custom option if present
                if (existingCustom) existingCustom.remove();
            }
        },

        _populateForm: function(s) {
            document.getElementById('ai-enabled').checked = !!s.enabled;

            // Only set provider if it's a valid option (builtin or allowed custom)
            var provider = s.provider || 'openai';
            var sel = document.getElementById('ai-provider');
            var validValues = Array.from(sel.options).map(function(o) { return o.value; });
            sel.value = validValues.indexOf(provider) >= 0 ? provider : 'openai';

            document.getElementById('ai-api-key').value = s.apiKey || '';

            // BaseUrl: editable only for custom provider, otherwise read-only
            var baseUrlInput = document.getElementById('ai-base-url');
            if (baseUrlInput) {
                baseUrlInput.value = s.baseUrl || '';
                this._updateBaseUrlState(sel.value);
            }

            document.getElementById('ai-chat-model').value = s.chatModel || '';
            document.getElementById('ai-analysis-model').value = s.analysisModel || '';
            document.getElementById('ai-max-tokens').value = s.maxTokens || 4096;
            document.getElementById('ai-temperature').value = s.temperature || 0.3;
            document.getElementById('ai-temp-display').textContent = s.temperature || 0.3;
            document.getElementById('ai-max-history').value = s.maxConversationHistory || 20;
            document.getElementById('ai-bg-analysis').checked = !!s.enableBackgroundAnalysis;
            document.getElementById('ai-enhance-notif').checked = !!s.enhanceNotifications;
        },

        /**
         * BaseUrl is ALWAYS editable (supports proxies / mirrors for known providers).
         * Visual hint changes based on whether it matches the official endpoint or known domain.
         */
        _updateBaseUrlState: function(provider) {
            var input = document.getElementById('ai-base-url');
            if (!input) return;

            var preset = PROVIDER_PRESETS[provider];
            var officialUrl = preset ? preset.baseUrl : '';
            var currentVal = (input.value || '').trim().replace(/\/$/, '');
            var isOfficial = officialUrl && currentVal.replace(/\/$/, '') === officialUrl.replace(/\/$/, '');
            var isKnownDomain = isKnownProviderDomain(provider, currentVal);

            if (isOfficial || isKnownDomain || !currentVal) {
                // Official URL / known provider domain / empty — neutral styling
                input.title = __('ai.baseUrlHint') || '可修改为 API 代理/镜像地址，将通过安全验证';
                input.classList.remove('bg-light', 'border-warning');
            } else {
                // Non-official URL — subtle visual indicator
                input.title = __('ai.baseUrlCustomHint') || '当前使用自定义地址，将通过安全验证';
                input.classList.remove('bg-light');
                input.classList.add('border-warning');
            }
        },

        _collectForm: function() {
            var provider = document.getElementById('ai-provider').value;
            return {
                enabled: document.getElementById('ai-enabled').checked,
                provider: provider,
                apiKey: document.getElementById('ai-api-key').value,
                // BaseUrl: always send current value (known provider: server uses official URL; custom: user-supplied with SSRF validation)
                baseUrl: document.getElementById('ai-base-url').value || '',
                chatModel: document.getElementById('ai-chat-model').value,
                analysisModel: document.getElementById('ai-analysis-model').value,
                maxTokens: parseInt(document.getElementById('ai-max-tokens').value) || 4096,
                temperature: parseFloat(document.getElementById('ai-temperature').value) || 0.3,
                maxConversationHistory: parseInt(document.getElementById('ai-max-history').value) || 20,
                enableBackgroundAnalysis: document.getElementById('ai-bg-analysis').checked,
                enhanceNotifications: document.getElementById('ai-enhance-notif').checked
            };
        },

        onProviderChange: function() {
            var provider = document.getElementById('ai-provider').value;
            var preset = PROVIDER_PRESETS[provider];
            var baseUrlInput = document.getElementById('ai-base-url');

            if (preset) {
                // Known provider: fill in the recommended BaseUrl (editable — user can change it)
                if (baseUrlInput && preset.baseUrl) baseUrlInput.value = preset.baseUrl;
                if (preset.chatModel) document.getElementById('ai-chat-model').value = preset.chatModel;
                if (preset.analysisModel) document.getElementById('ai-analysis-model').value = preset.analysisModel;
            } else if (provider === 'custom') {
                // Custom: keep current BaseUrl if already set, otherwise blank
                if (baseUrlInput && !baseUrlInput.value) {
                    baseUrlInput.value = '';
                    baseUrlInput.placeholder = 'https://your-endpoint.example.com/v1';
                }
            }

            this._updateBaseUrlState(provider);
        },

        toggleKeyVisibility: function() {
            var input = document.getElementById('ai-api-key');
            var icon = document.getElementById('ai-key-toggle-icon');
            if (input.type === 'password') {
                input.type = 'text';
                icon.className = 'bi bi-eye-slash';
            } else {
                input.type = 'password';
                icon.className = 'bi bi-eye';
            }
        },

        /**
         * Client-side BaseUrl format validation.
         * Returns an error message string, or null if valid.
         */
        _validateBaseUrl: function(url) {
            if (!url || !url.trim()) return null; // empty is fine (server uses official URL)
            url = url.trim();
            try {
                var u = new URL(url);
                if (u.protocol !== 'http:' && u.protocol !== 'https:')
                    return __('ai.baseUrlBadScheme') || 'URL 必须以 http:// 或 https:// 开头';
                if (!u.hostname)
                    return __('ai.baseUrlNoHost') || 'URL 缺少主机名';
                return null;
            } catch (e) {
                return __('ai.baseUrlInvalid') || 'URL 格式无效';
            }
        },

        save: async function() {
            var data = this._collectForm();

            // Client-side URL format validation
            var urlError = this._validateBaseUrl(data.baseUrl);
            if (urlError) {
                DashboardModals.showError(__('ai.baseUrlInvalid') + ': ' + urlError);
                return;
            }

            // Confirmation when using a non-official URL (skip for known provider domains)
            var provider = data.provider;
            var preset = PROVIDER_PRESETS[provider];
            var officialUrl = preset ? preset.baseUrl : '';
            var currentUrl = (data.baseUrl || '').trim().replace(/\/$/, '');
            var isKnownDomain = isKnownProviderDomain(provider, currentUrl);
            var isNonOfficial = currentUrl && officialUrl &&
                currentUrl !== officialUrl.replace(/\/$/, '') && !isKnownDomain;

            if (isNonOfficial) {
                var confirmed = confirm(
                    (__('ai.baseUrlConfirmTitle') || '使用自定义 API 地址') + '\n\n' +
                    (__('ai.baseUrlConfirmMsg') || '您填写的 Base URL 不是该服务商的官方地址，将通过安全验证。确认继续使用？') + '\n\n' +
                    currentUrl
                );
                if (!confirmed) return;
            }

            try {
                await DashboardApi.endpoints.saveAISettings(data);
                DashboardModals.showSuccess(__('ai.saveSuccess'));

                // Refresh status
                var statusResp = await DashboardApi.endpoints.getAIStatus();
                this._status = statusResp?.data || statusResp;
                this._updateStatusBadge();

                // Re-sync BaseUrl state (server may have sanitised it)
                var settingsResp = await DashboardApi.endpoints.getAISettings();
                var s = settingsResp?.data || settingsResp;
                var baseUrlInput = document.getElementById('ai-base-url');
                if (baseUrlInput && s.baseUrl) {
                    baseUrlInput.value = s.baseUrl;
                    this._updateBaseUrlState(document.getElementById('ai-provider').value);
                }
            } catch (e) {
                DashboardModals.showError(__('ai.saveFailed') + (e.message || e));
            }
        },

        testConnection: async function() {
            DashboardModals.showInfo(__('ai.testing'));

            try {
                var resp = await DashboardApi.post('/api/ai/test', {});
                var result = resp?.data || resp;

                if (result && result.success === true) {
                    DashboardModals.showSuccess(__('ai.testSuccess') + ' - ' + (result.message || ''));
                } else {
                    var errMsg = result?.error || __('ai.testFailed');
                    DashboardModals.showError(__('ai.testFailed') + ': ' + errMsg);
                }
            } catch (e) {
                DashboardModals.showError(__('ai.testFailed') + ': ' + (e.message || e));
            }
        },

        refreshAnalysis: async function() {
            var container = document.getElementById('ai-analysis-list');
            if (!container) return;

            try {
                var resp = await DashboardApi.endpoints.getAIAnalysis(10);
                var items = resp?.data || resp || [];

                if (!Array.isArray(items) || items.length === 0) {
                    container.innerHTML = '<p class="text-muted small mb-0">' + __('ai.noAnalysis') + '</p>';
                    return;
                }

                var html = '';
                for (var i = 0; i < items.length; i++) {
                    var item = items[i];
                    var sevClass = item.severity >= 2 ? 'border-start border-danger border-3' :
                                   item.severity >= 1 ? 'border-start border-warning border-3' :
                                   'border-start border-info border-3';
                    var typeLabel = item.type === 'anomaly' ? __('ai.typeAnomaly') :
                                    item.type === 'suggestion' ? __('ai.typeSuggestion') : __('ai.typeSummary');
                    var date = item.createdAt ? new Date(item.createdAt).toLocaleString() : '';

                    html += '<div class="p-2 mb-2 ' + sevClass + ' bg-light rounded">' +
                        '<div class="d-flex justify-content-between align-items-center mb-1">' +
                        '<span class="badge bg-' + (item.severity >= 2 ? 'danger' : item.severity >= 1 ? 'warning' : 'info') + ' me-2">' + typeLabel + '</span>' +
                        '<small class="text-muted">' + date + '</small>' +
                        '</div>' +
                        '<div class="small">' + this._escapeHtml(item.content).substring(0, 300) + '</div>' +
                        '</div>';
                }
                container.innerHTML = html;
            } catch (e) {
                console.warn('[AI] Analysis load failed:', e);
            }
        },

        _escapeHtml: function(text) {
            if (!text) return '';
            var div = document.createElement('div');
            div.textContent = text;
            return div.innerHTML;
        }
    };
})();
