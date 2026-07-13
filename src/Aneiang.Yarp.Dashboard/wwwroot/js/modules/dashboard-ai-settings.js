/**
 * AI Settings Module - Manages AI configuration page
 */
(function() {
    'use strict';

    function __(key) {
        if (window.DashboardI18n && DashboardI18n.t) return DashboardI18n.t(key);
        if (window.I18N && I18N[key]) return I18N[key];
        return key;
    }

    const PROVIDER_PRESETS = {
        openai:   { baseUrl: 'https://api.openai.com/v1', chatModel: 'gpt-4o-mini', analysisModel: 'gpt-4o-mini' },
        deepseek: { baseUrl: 'https://api.deepseek.com/v1', chatModel: 'deepseek-chat', analysisModel: 'deepseek-chat' },
        qwen:     { baseUrl: 'https://dashscope.aliyuncs.com/compatible-mode/v1', chatModel: 'qwen-turbo', analysisModel: 'qwen-turbo' },
        custom:   { baseUrl: '', chatModel: '', analysisModel: '' }
    };

    window.AISettings = {
        _status: null,

        init: async function() {
            try {
                var statusResp = await DashboardApi.endpoints.getAIStatus();
                this._status = statusResp?.data || statusResp;
                this._updateStatusBadge();
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

        _populateForm: function(s) {
            document.getElementById('ai-enabled').checked = !!s.enabled;
            document.getElementById('ai-provider').value = s.provider || 'openai';
            document.getElementById('ai-api-key').value = s.apiKey || '';
            document.getElementById('ai-base-url').value = s.baseUrl || '';
            document.getElementById('ai-chat-model').value = s.chatModel || '';
            document.getElementById('ai-analysis-model').value = s.analysisModel || '';
            document.getElementById('ai-max-tokens').value = s.maxTokens || 4096;
            document.getElementById('ai-temperature').value = s.temperature || 0.3;
            document.getElementById('ai-temp-display').textContent = s.temperature || 0.3;
            document.getElementById('ai-max-history').value = s.maxConversationHistory || 20;
            document.getElementById('ai-bg-analysis').checked = !!s.enableBackgroundAnalysis;
            document.getElementById('ai-enhance-notif').checked = !!s.enhanceNotifications;
        },

        _collectForm: function() {
            return {
                enabled: document.getElementById('ai-enabled').checked,
                provider: document.getElementById('ai-provider').value,
                apiKey: document.getElementById('ai-api-key').value,
                baseUrl: document.getElementById('ai-base-url').value,
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
            if (preset) {
                if (preset.baseUrl) document.getElementById('ai-base-url').value = preset.baseUrl;
                if (preset.chatModel) document.getElementById('ai-chat-model').value = preset.chatModel;
                if (preset.analysisModel) document.getElementById('ai-analysis-model').value = preset.analysisModel;
            }
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

        save: async function() {
            var data = this._collectForm();
            try {
                await DashboardApi.endpoints.saveAISettings(data);
                DashboardModals.showSuccess(__('ai.saveSuccess'));

                // Refresh status
                var statusResp = await DashboardApi.endpoints.getAIStatus();
                this._status = statusResp?.data || statusResp;
                this._updateStatusBadge();
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
