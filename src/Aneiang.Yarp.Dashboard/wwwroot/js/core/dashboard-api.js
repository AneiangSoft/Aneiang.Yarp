/**
 * Dashboard API Layer - Unified API client with authentication
 */
(function() {
    'use strict';

    window.DashboardApi = window.DashboardApi || {};

    let config = {
        baseUrl: '',
        token: null,
        timeout: 30000,
        retries: 0
    };

    window.DashboardApi.init = function() {
        const dashboard = window.__dashboard;
        if (dashboard) {
            config.baseUrl = dashboard.basePath || '';
            config.token = dashboard.token || null;
        }
    };

    window.DashboardApi.setToken = function(token) {
        config.token = token;
        if (token) {
            localStorage.setItem('dashboard_token', token);
        } else {
            localStorage.removeItem('dashboard_token');
        }
    };

    window.DashboardApi.getToken = function() {
        return config.token || localStorage.getItem('dashboard_token');
    };

    window.DashboardApi.request = async function(url, options = {}) {
        const {
            method = 'GET',
            body = null,
            headers = {},
            timeout = config.timeout,
            parseJson = true,
            requireAuth = true,
            silent = false
        } = options;

        const fullUrl = url.startsWith('http') ? url : `${config.baseUrl}${url}`;

        const requestHeaders = {
            'Content-Type': 'application/json',
            ...headers
        };

        if (requireAuth) {
            const token = this.getToken();
            if (token) {
                requestHeaders['Authorization'] = `Bearer ${token}`;
            }
        }

        const fetchOptions = {
            method,
            headers: requestHeaders,
            signal: AbortSignal.timeout(timeout)
        };

        if (body) {
            fetchOptions.body = typeof body === 'string' ? body : JSON.stringify(body);
        }

        // Notify global loading indicator (begin/end paired even on error).
        if (!silent && window.DashboardLoading) window.DashboardLoading.begin();

        try {
            const response = await fetch(fullUrl, fetchOptions);

            if (response.status === 401) {
                this.handleAuthError();
                throw new Error('Unauthorized');
            }

            if (response.status >= 400) {
                let errMsg = `Request failed: ${response.status}`;
                try {
                    const errBody = await response.json();
                    errMsg = errBody.title || errBody.message || errBody.detail || errMsg;
                    // If there are validation errors, include them
                    if (errBody.errors) {
                        const details = Object.entries(errBody.errors)
                            .map(([k, v]) => `${k}: ${Array.isArray(v) ? v.join(', ') : v}`)
                            .join('; ');
                        if (details) errMsg = details;
                    }
                } catch (_) { /* use default message */ }
                throw new Error(errMsg);
            }

            if (response.status >= 500) {
                throw new Error(`Server error: ${response.status}`);
            }

            if (parseJson && response.status !== 204) {
                const data = await response.json();

                // Unwrap { code: 200, data: ... } response format
                if (data && typeof data === 'object' && 'code' in data) {
                    if (data.code >= 200 && data.code < 300) {
                        return data.data !== undefined ? data.data : data;
                    } else if (data.code === 401) {
                        this.handleAuthError();
                        throw new Error(data.message || 'Unauthorized');
                    } else {
                        throw new Error(data.message || `API error: ${data.code}`);
                    }
                }

                // Fallback: return data directly if no code field
                return data;
            }

            return response;

        } catch (error) {
            if (error.name === 'AbortError') {
                throw new Error('Request timeout');
            }
            throw error;
        } finally {
            if (!silent && window.DashboardLoading) window.DashboardLoading.end();
        }
    };

    window.DashboardApi.get = function(url, params, options = {}) {
        if (params) {
            // Strip null/undefined values so they never become the string "undefined"/"null" in the query string.
            const cleaned = {};
            for (const key of Object.keys(params)) {
                const v = params[key];
                if (v != null && v !== undefined && v !== '') {
                    cleaned[key] = v;
                }
            }
            const queryString = new URLSearchParams(cleaned).toString();
            url = queryString ? `${url}?${queryString}` : url;
        }
        return this.request(url, { method: 'GET', ...options });
    };

    window.DashboardApi.post = function(url, body, options = {}) {
        return this.request(url, { method: 'POST', body, ...options });
    };

    window.DashboardApi.put = function(url, body, options = {}) {
        return this.request(url, { method: 'PUT', body, ...options });
    };

    window.DashboardApi.delete = function(url, bodyOrOptions, options = {}) {
        if (bodyOrOptions && typeof bodyOrOptions === 'object' && !bodyOrOptions.method && !bodyOrOptions.headers) {
            return this.request(url, { method: 'DELETE', body: bodyOrOptions, ...options });
        }
        return this.request(url, { method: 'DELETE', ...bodyOrOptions, ...options });
    };

    window.DashboardApi.download = async function(url, filename) {
        try {
            const response = await this.request(url, {
                parseJson: false,
                requireAuth: true
            });

            const blob = await response.blob();
            const blobUrl = window.URL.createObjectURL(blob);
            
            const a = document.createElement('a');
            a.href = blobUrl;
            a.download = filename || 'download';
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            window.URL.revokeObjectURL(blobUrl);

            return true;
        } catch (error) {
            console.error('[API] Download failed:', error);
            throw error;
        }
    };

    window.DashboardApi.upload = async function(url, file, options = {}) {
        const formData = new FormData();
        formData.append('file', file);

        return this.request(url, {
            method: 'POST',
            body: formData,
            headers: {}, // Let browser set Content-Type for FormData
            ...options
        });
    };

    window.DashboardApi.handleAuthError = function() {
        localStorage.removeItem('dashboard_token');
        
        // Redirect to login if not already there
        if (!window.location.pathname.includes('/login')) {
            window.location.href = `${config.baseUrl}/login`;
        }
    };

    window.DashboardApi.handleError = function(error, showMessage = true) {
        console.error('[API] Error:', error);
        
        if (showMessage && window.DashboardModals) {
            window.DashboardModals.showError(error.message || __('api.requestFailed'));
        }
        
        return error;
    };

    window.DashboardApi.endpoints = {
        // Info
        getInfo: () => DashboardApi.get('/api/info'),

        // Clusters (read-only list; CRUD via /api/config/*)
        getClusters: () => DashboardApi.get('/api/clusters'),

        // Routes (read-only list; CRUD via /api/config/*)
        getRoutes: () => DashboardApi.get('/api/routes'),

        // Logs
        getLogs: (count = 100) => DashboardApi.get('/api/logs', { count }),
        clearLogs: () => DashboardApi.delete('/api/logs'),
        getLogHistory: (params) => DashboardApi.get('/api/logs/history', params),
        getLogDetail: (id) => DashboardApi.get(`/api/logs/detail/${id}`),
        getLogStats: () => DashboardApi.get('/api/logs/stats'),
        getLogSettings: () => DashboardApi.get('/api/logs/settings'),
        updateLogSettings: (data) => DashboardApi.put('/api/logs/settings', data),
        resetLogSettings: () => DashboardApi.put('/api/logs/settings/reset', {}),
        getLogRestartRequired: () => DashboardApi.get('/api/logs/settings/restart-required'),

        // Statistics (aggregated from proxy log store)
        getStats: () => DashboardApi.get('/api/traffic/stats'),

        // Config History
        getHistory: () => DashboardApi.get('/api/config/history'),
        clearConfigHistory: () => DashboardApi.delete('/api/config/history'),
        rollback: (versionId) => DashboardApi.post(`/api/config/rollback/${versionId}`),
        createSnapshot: (description) => DashboardApi.post('/api/config/snapshot', { description }),

        // Auth
        login: (credentials) => DashboardApi.post('/login', credentials, { requireAuth: false }),
        getAuthStatus: () => DashboardApi.get('/api/auth/status'),

        // Configuration Management (Phase 6)
        exportConfig: () => DashboardApi.get('/api/config/export'),
        importConfig: (config) => DashboardApi.post('/api/config/import', config),
        saveCluster: (clusterId, config) => DashboardApi.put(`/api/config/clusters/${clusterId}`, config),
        deleteClusterConfig: (clusterId) => DashboardApi.delete(`/api/config/clusters/${clusterId}`),
        renameCluster: (oldClusterId, config) => DashboardApi.put(`/api/config/clusters/${oldClusterId}/rename`, config),
        saveRoute: (routeId, config) => DashboardApi.put(`/api/config/routes/${routeId}`, config),
        deleteRouteConfig: (routeId) => DashboardApi.delete(`/api/config/routes/${routeId}`),
        getConfigHistory: () => DashboardApi.get('/api/config/history'),
        rollbackConfig: (versionId) => DashboardApi.post(`/api/config/rollback/${versionId}`),
        validateConfig: (config) => DashboardApi.post('/api/config/validate', config),

        // Audit Logs
        getAuditLogs: (page, pageSize, action) => DashboardApi.get('/api/audit-logs', { page: page || 1, pageSize: pageSize || 100, action: action || '' }),

        // Circuit Breaker
        getCircuitBreakerStatus: () => DashboardApi.get('/api/circuit-breaker/status'),
        resetCircuitBreakers: () => DashboardApi.post('/api/circuit-breaker/reset', {}),

        // Plugins
        getPlugins: () => DashboardApi.get('/api/plugins'),
        getPlugin: (id) => DashboardApi.get('/api/plugins/' + id),
        togglePlugin: (id, enabled) => DashboardApi.post('/api/plugins/' + id + '/toggle', { enabled }),
        resetPlugins: () => DashboardApi.post('/api/plugins/reset'),
        installPlugin: (sourceDirectory) => DashboardApi.post('/api/plugins/install', { sourceDirectory }),
        uninstallPlugin: (pluginId) => DashboardApi.delete('/api/plugins/' + pluginId),
        upgradePlugin: (pluginId, sourceDirectory) => DashboardApi.post('/api/plugins/' + pluginId + '/upgrade', { sourceDirectory }),

        // Plugin Bindings
        getBindings: (scope, scopeId) => DashboardApi.get('/api/plugin-bindings', { scope, scopeId }),
        createBinding: (data) => DashboardApi.post('/api/plugin-bindings', data),
        updateBinding: (id, data) => DashboardApi.put('/api/plugin-bindings/' + id, data),
        deleteBinding: (id) => DashboardApi.delete('/api/plugin-bindings/' + id),

        // Strategy Presets
        getPresets: (pluginId) => DashboardApi.get('/api/presets', { pluginId }),
        getPreset: (id) => DashboardApi.get('/api/presets/' + id),
        savePreset: (data) => DashboardApi.post('/api/presets', data),
        deletePreset: (id) => DashboardApi.delete('/api/presets/' + id),
        applyPreset: (id, bindingId) => DashboardApi.post('/api/presets/' + id + '/apply', { bindingId }),


        // Operations (Enhanced Dashboard)
        getTrafficData: (minutes) => DashboardApi.get('/api/traffic/stats', { minutes }),

        // Config Snapshot & Diff
        configDiff: (versionId) => DashboardApi.get('/api/config/diff/' + versionId),

        // Database Download
        downloadDatabase: () => DashboardApi.download('/api/settings/database', 'gateway-store.db'),

        // Overview snapshot (HTTP fallback for the SignalR push)
        getOverviewSnapshot: () => DashboardApi.get('/api/overview/snapshot'),

        // Webhook notifications (config-change events)
        getWebhookSettings: () => DashboardApi.get('/api/webhook/settings'),
        saveWebhookSettings: (data) => DashboardApi.post('/api/webhook/settings', data),
        testWebhook: (data) => DashboardApi.post('/api/webhook/test', data),

        // Plugin Resource Monitor
        getPluginResources: () => DashboardApi.get('/api/plugin-resources'),
        getPluginResource: (pluginId) => DashboardApi.get('/api/plugin-resources/' + encodeURIComponent(pluginId)),
        getPluginResourceTotals: () => DashboardApi.get('/api/plugin-resources/totals')
    };

    // Aliases: expose top-level convenience methods (used by page-level JS)
    window.DashboardApi.getRoutes = () => DashboardApi.endpoints.getRoutes();
    window.DashboardApi.getClusters = () => DashboardApi.endpoints.getClusters();
    window.DashboardApi.getCircuitBreakerStatus = () => DashboardApi.endpoints.getCircuitBreakerStatus();
    window.DashboardApi.resetCircuitBreakers = () => DashboardApi.endpoints.resetCircuitBreakers();
    window.DashboardApi.getPlugins = () => DashboardApi.endpoints.getPlugins();
    window.DashboardApi.getPlugin = (id) => DashboardApi.endpoints.getPlugin(id);
    window.DashboardApi.togglePlugin = (id, enabled) => DashboardApi.endpoints.togglePlugin(id, enabled);
    window.DashboardApi.resetPlugins = () => DashboardApi.endpoints.resetPlugins();
    window.DashboardApi.installPlugin = (sourceDirectory) => DashboardApi.endpoints.installPlugin(sourceDirectory);
    window.DashboardApi.uninstallPlugin = (pluginId) => DashboardApi.endpoints.uninstallPlugin(pluginId);
    window.DashboardApi.upgradePlugin = (pluginId, sourceDirectory) => DashboardApi.endpoints.upgradePlugin(pluginId, sourceDirectory);
    window.DashboardApi.getBindings = (scope, scopeId) => DashboardApi.endpoints.getBindings(scope, scopeId);
    window.DashboardApi.getPluginBindings = async (pluginId, scope) => {
        const bindings = await DashboardApi.endpoints.getBindings();
        const scopeValue = scope === 'Route' ? 1 : scope === 'Cluster' ? 2 : scope;
        return (Array.isArray(bindings) ? bindings : []).filter(binding =>
            binding.pluginId === pluginId && (!scope || binding.scope === scope || binding.scope === scopeValue));
    };
    window.DashboardApi.getTrafficData = (minutes) => DashboardApi.endpoints.getTrafficData(minutes);
    window.DashboardApi.createBinding = (data) => DashboardApi.endpoints.createBinding(data);
    window.DashboardApi.updateBinding = (id, data) => DashboardApi.endpoints.updateBinding(id, data);
    window.DashboardApi.deleteBinding = (id) => DashboardApi.endpoints.deleteBinding(id);
    window.DashboardApi.getPresets = (pluginId) => DashboardApi.endpoints.getPresets(pluginId);
    window.DashboardApi.getPluginResources = () => DashboardApi.endpoints.getPluginResources();
    window.DashboardApi.getPluginResource = (pluginId) => DashboardApi.endpoints.getPluginResource(pluginId);
    window.DashboardApi.getPluginResourceTotals = () => DashboardApi.endpoints.getPluginResourceTotals();
    window.DashboardApi.savePreset = (data) => DashboardApi.endpoints.savePreset(data);
    window.DashboardApi.deletePreset = (id) => DashboardApi.endpoints.deletePreset(id);
    window.DashboardApi.applyPreset = (id, bindingId) => DashboardApi.endpoints.applyPreset(id, bindingId);

})();
