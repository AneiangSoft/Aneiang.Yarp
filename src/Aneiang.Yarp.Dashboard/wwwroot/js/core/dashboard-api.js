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
                    if (data.code === 200) {
                        // New ApiResponse<T> format: { code, success, data, message }
                        // Old format: { code, data } or { code, message }
                        // When data is present and non-null, return it directly.
                        // When data is null/undefined (message-only response), return whole object
                        // so callers can access .message if needed.
                        if (data.data !== null && data.data !== undefined) {
                            return data.data;
                        }
                        return data;
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

        // Statistics
        getStats: () => DashboardApi.get('/api/stats'),

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
        getAuditLogs: (count, action) => DashboardApi.get('/api/audit-logs', { count: count || 100, action: action || '' }),

        // Circuit Breaker
        getCircuitBreakerStatus: () => DashboardApi.get('/api/circuit-breaker/status'),
        resetCircuitBreakers: () => DashboardApi.post('/api/circuit-breaker/reset', {}),

        // Policies
        getPolicies: (type) => DashboardApi.get('/api/policies/' + type),
        getPolicy: (type, id) => DashboardApi.get('/api/policies/' + type + '/' + id),
        createPolicy: (type, data) => DashboardApi.post('/api/policies/' + type, data),
        updatePolicy: (type, id, data) => DashboardApi.put('/api/policies/' + type + '/' + id, data),
        deletePolicy: (type, id) => DashboardApi.delete('/api/policies/' + type + '/' + id),
        applyPolicy: (type, id, targetId) => DashboardApi.post('/api/policies/' + type + '/' + id + '/apply', { targetId: targetId }),
        unapplyPolicy: (type, id, targetId) => DashboardApi.delete('/api/policies/' + type + '/' + id + '/apply', { targetId: targetId }),
        getRoutePoliciesForRoute: (routeId) => DashboardApi.get('/api/policies/routes/for-route/' + encodeURIComponent(routeId)),
        getClusterPoliciesForCluster: (clusterId) => DashboardApi.get('/api/policies/clusters/for-cluster/' + encodeURIComponent(clusterId)),

        // Plugins
        getPlugins: () => DashboardApi.get('/api/plugins'),
        getPlugin: (id) => DashboardApi.get('/api/plugins/' + id),
        togglePlugin: (id, enabled) => DashboardApi.post('/api/plugins/' + id + '/toggle', { enabled }),
        resetPlugins: () => DashboardApi.post('/api/plugins/reset'),

        // WAF Settings
        getWafSettings: () => DashboardApi.get('/api/config/waf'),
        saveWafSettings: (data) => DashboardApi.put('/api/config/waf', data),

        // Health Check
        getHealthCheckStatus: () => DashboardApi.get('/api/health-check/status'),
        getClusterHealthConfigs: () => DashboardApi.get('/api/health-check/clusters'),
        getHealthSummary: () => DashboardApi.get('/api/operations/health-summary'),

        // Operations (Enhanced Dashboard)
        getTrafficData: (minutes) => DashboardApi.get('/api/operations/traffic', { minutes }),
        getOpsAlertSummary: () => DashboardApi.get('/api/operations/alert-summary'),
        getTopIssues: (count) => DashboardApi.get('/api/operations/top-issues', { count }),
        exportSnapshot: () => DashboardApi.get('/api/operations/snapshot'),

        // Config Snapshot & Diff
        configDiff: (versionId) => DashboardApi.get('/api/config/diff/' + versionId),

        // Database Download
        downloadDatabase: () => DashboardApi.download('/api/settings/database', 'gateway-store.db'),

        // Cluster Toggle (Stage 2)
        toggleCluster: (clusterId) => DashboardApi.post('/api/config/clusters/' + clusterId + '/toggle'),

        getNotificationSettings: () => DashboardApi.get('/api/notifications/settings'),
        saveNotificationSettings: (data) => DashboardApi.put('/api/notifications/settings', data),
        getNotificationHistory: (params) => DashboardApi.get('/api/notifications/history', params),
        clearNotificationHistory: () => DashboardApi.delete('/api/notifications/history'),
        getNotificationSummary: () => DashboardApi.get('/api/notifications/summary'),
        testNotificationChannel: (channelId) => DashboardApi.post('/api/notifications/channels/' + channelId + '/test', {}),
        createNotificationChannel: (data) => DashboardApi.post('/api/notifications/channels', data),
        updateNotificationChannel: (id, data) => DashboardApi.put('/api/notifications/channels/' + id, data),
        deleteNotificationChannel: (id) => DashboardApi.delete('/api/notifications/channels/' + id),
        createNotificationRule: (data) => DashboardApi.post('/api/notifications/rules', data),
        updateNotificationRule: (id, data) => DashboardApi.put('/api/notifications/rules/' + id, data),
        deleteNotificationRule: (id) => DashboardApi.delete('/api/notifications/rules/' + id),
        sendTestNotification: (data) => DashboardApi.post('/api/notifications/test', data || {}),

        // AI Module
        getAIStatus: () => DashboardApi.get('/api/ai/status'),
        getAISettings: () => DashboardApi.get('/api/ai/settings'),
        saveAISettings: (data) => DashboardApi.put('/api/ai/settings', data),
        getAITools: () => DashboardApi.get('/api/ai/tools'),
        getAIAnalysis: (count, type) => DashboardApi.get('/api/ai/analysis', { count: count || 20, type: type || '' }),
        triggerAIAnalysis: () => DashboardApi.post('/api/ai/analyze'),
        deleteAIAnalysis: (id) => DashboardApi.delete(`/api/ai/analysis/${id}`),
        getAISessions: (count) => DashboardApi.get('/api/ai/sessions', { count: count || 50 }),
        getSessionMessages: (sessionId, count) => DashboardApi.get(`/api/ai/sessions/${sessionId}`, { count: count || 50 }),
        deleteAISession: (sessionId) => DashboardApi.delete(`/api/ai/sessions/${sessionId}`)
    };

    // Aliases: expose top-level convenience methods (used by page-level JS)
    window.DashboardApi.getRoutes = () => DashboardApi.endpoints.getRoutes();
    window.DashboardApi.getClusters = () => DashboardApi.endpoints.getClusters();
    window.DashboardApi.getCircuitBreakerStatus = () => DashboardApi.endpoints.getCircuitBreakerStatus();
    window.DashboardApi.resetCircuitBreakers = () => DashboardApi.endpoints.resetCircuitBreakers();
    window.DashboardApi.getPolicies = (type) => DashboardApi.endpoints.getPolicies(type);
    window.DashboardApi.getPolicy = (type, id) => DashboardApi.endpoints.getPolicy(type, id);
    window.DashboardApi.createPolicy = (type, data) => DashboardApi.endpoints.createPolicy(type, data);
    window.DashboardApi.updatePolicy = (type, id, data) => DashboardApi.endpoints.updatePolicy(type, id, data);
    window.DashboardApi.deletePolicy = (type, id) => DashboardApi.endpoints.deletePolicy(type, id);
    window.DashboardApi.applyPolicy = (type, id, targetId) => DashboardApi.endpoints.applyPolicy(type, id, targetId);
    window.DashboardApi.unapplyPolicy = (type, id, targetId) => DashboardApi.endpoints.unapplyPolicy(type, id, targetId);
    window.DashboardApi.getPlugins = () => DashboardApi.endpoints.getPlugins();
    window.DashboardApi.getPlugin = (id) => DashboardApi.endpoints.getPlugin(id);
    window.DashboardApi.togglePlugin = (id, enabled) => DashboardApi.endpoints.togglePlugin(id, enabled);
    window.DashboardApi.resetPlugins = () => DashboardApi.endpoints.resetPlugins();

    window.DashboardApi.getNotificationSettings = () => DashboardApi.endpoints.getNotificationSettings();
    window.DashboardApi.saveNotificationSettings = (data) => DashboardApi.endpoints.saveNotificationSettings(data);

    window.DashboardApi.getLogSettings = () => DashboardApi.endpoints.getLogSettings();
    window.DashboardApi.updateLogSettings = (data) => DashboardApi.endpoints.updateLogSettings(data);
    window.DashboardApi.resetLogSettings = () => DashboardApi.endpoints.resetLogSettings();

})();
