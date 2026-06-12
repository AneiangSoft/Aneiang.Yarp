/**
 * Dashboard API Layer - Unified API client with authentication
 */
(function() {
    'use strict';

    window.DashboardApi = window.DashboardApi || {};

    // ===== Configuration =====
    let config = {
        baseUrl: '',
        token: null,
        timeout: 30000,
        retries: 0
    };

    // ===== Initialization =====
    window.DashboardApi.init = function() {
        const dashboard = window.__dashboard;
        if (dashboard) {
            config.baseUrl = dashboard.basePath || '';
            config.token = dashboard.token || null;
        }
        console.log('[API] Initialized with base URL:', config.baseUrl);
    };

    // ===== Token Management =====
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

    // ===== Core Request Method =====
    window.DashboardApi.request = async function(url, options = {}) {
        const {
            method = 'GET',
            body = null,
            headers = {},
            timeout = config.timeout,
            parseJson = true,
            requireAuth = true
        } = options;

        // Build full URL
        const fullUrl = url.startsWith('http') ? url : `${config.baseUrl}${url}`;

        // Setup headers
        const requestHeaders = {
            'Content-Type': 'application/json',
            ...headers
        };

        // Add authentication if required
        if (requireAuth) {
            const token = this.getToken();
            if (token) {
                requestHeaders['Authorization'] = `Bearer ${token}`;
            }
        }

        // Build request options
        const fetchOptions = {
            method,
            headers: requestHeaders,
            signal: AbortSignal.timeout(timeout)
        };

        if (body) {
            fetchOptions.body = typeof body === 'string' ? body : JSON.stringify(body);
        }

        try {
            const response = await fetch(fullUrl, fetchOptions);

            // Handle authentication errors
            if (response.status === 401) {
                this.handleAuthError();
                throw new Error('Unauthorized');
            }

            // Handle server errors
            if (response.status >= 500) {
                throw new Error(`Server error: ${response.status}`);
            }

            // Parse response
            if (parseJson && response.status !== 204) {
                const data = await response.json();
                
                // Unwrap { code: 200, data: ... } response format
                if (data && typeof data === 'object' && 'code' in data) {
                    if (data.code === 200) {
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
            // Network or parsing errors
            if (error.name === 'AbortError') {
                throw new Error('Request timeout');
            }
            throw error;
        }
    };

    // ===== HTTP Method Helpers =====
    window.DashboardApi.get = function(url, params, options = {}) {
        if (params) {
            const queryString = new URLSearchParams(params).toString();
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

    // ===== File Operations =====
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

    // ===== Error Handling =====
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
            window.DashboardModals.showError(error.message || '请求失败');
        }
        
        return error;
    };

    // ===== API Endpoints =====
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

        // Statistics
        getStats: () => DashboardApi.get('/api/stats'),

        // Config History
        getHistory: () => DashboardApi.get('/api/config/history'),
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

        // Alerts
        getAlerts: (count) => DashboardApi.get('/api/alerts', { count: count || 100 }),
        clearAlerts: () => DashboardApi.delete('/api/alerts'),
        testAlert: (data) => DashboardApi.post('/api/alerts/test', data),
        getAlertSummary: () => DashboardApi.get('/api/alerts/summary'),

        // Security Events
        getSecurityEvents: (count) => DashboardApi.get('/api/security-events', { count: count || 100 }),
        clearSecurityEvents: () => DashboardApi.delete('/api/security-events'),
        getSecurityEventSummary: () => DashboardApi.get('/api/security-events/summary'),

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

        // Webhook Settings
        getWebhookSettings: () => DashboardApi.get('/api/config/webhook'),
        saveWebhookSettings: (data) => DashboardApi.put('/api/config/webhook', data),
        testWebhook: (data) => DashboardApi.post('/api/config/webhook/test', data),

        // WAF Settings
        getWafSettings: () => DashboardApi.get('/api/config/waf'),
        saveWafSettings: (data) => DashboardApi.put('/api/config/waf', data),

        // Alert Settings (/api/config/alert-settings)
        getAlertSettings: () => DashboardApi.get('/api/config/alert-settings'),
        saveAlertSettings: (data) => DashboardApi.put('/api/config/alert-settings', data),

        // Health Check
        getHealthCheckStatus: () => DashboardApi.get('/api/health-check/status'),
        getClusterHealthConfigs: () => DashboardApi.get('/api/health-check/clusters'),
        getHealthSummary: () => DashboardApi.get('/api/operations/health-summary'),

        // Operations (Enhanced Dashboard)
        getTrafficData: (minutes) => DashboardApi.get('/api/operations/traffic', { minutes }),
        getOpsAlertSummary: () => DashboardApi.get('/api/operations/alert-summary'),
        getTopIssues: (count) => DashboardApi.get('/api/operations/top-issues', { count }),
        exportSnapshot: () => DashboardApi.get('/api/operations/snapshot'),

        // Config Snapshot & Diff (Stage 2)
        getSnapshots: (limit) => DashboardApi.get('/api/dashboard/config/snapshots', { limit: limit || 50 }),
        getSnapshot: (id) => DashboardApi.get('/api/dashboard/config/snapshots/' + id),
        compareSnapshots: (fromId, toId) => DashboardApi.get('/api/dashboard/config/diff', { fromId, toId: toId || 'current' }),
        compareWithCurrent: (fromId) => DashboardApi.get('/api/dashboard/config/diff/' + fromId + '/current'),
        configDiff: (versionId) => DashboardApi.get('/api/config/diff/' + versionId),

        // Cluster Toggle (Stage 2)
        toggleCluster: (clusterId) => DashboardApi.post('/api/config/clusters/' + clusterId + '/toggle'),

        // Notifications (New Unified System)
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
        sendTestNotification: (data) => DashboardApi.post('/api/notifications/test', data || {})
    };

    // Aliases: expose top-level convenience methods (used by page-level JS)
    window.DashboardApi.getRoutes = () => DashboardApi.endpoints.getRoutes();
    window.DashboardApi.getClusters = () => DashboardApi.endpoints.getClusters();
    window.DashboardApi.getAlerts = (count) => DashboardApi.endpoints.getAlerts(count);
    window.DashboardApi.clearAlerts = () => DashboardApi.endpoints.clearAlerts();
    window.DashboardApi.getCircuitBreakerStatus = () => DashboardApi.endpoints.getCircuitBreakerStatus();
    window.DashboardApi.resetCircuitBreakers = () => DashboardApi.endpoints.resetCircuitBreakers();
    window.DashboardApi.getSecurityEvents = (count) => DashboardApi.endpoints.getSecurityEvents(count);
    window.DashboardApi.clearSecurityEvents = () => DashboardApi.endpoints.clearSecurityEvents();
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
    window.DashboardApi.getWebhookSettings = () => DashboardApi.endpoints.getWebhookSettings();
    window.DashboardApi.saveWebhookSettings = (data) => DashboardApi.endpoints.saveWebhookSettings(data);
    window.DashboardApi.testWebhook = (data) => DashboardApi.endpoints.testWebhook(data);

    // Notifications (New Unified System)
    window.DashboardApi.getNotificationSettings = () => DashboardApi.endpoints.getNotificationSettings();
    window.DashboardApi.saveNotificationSettings = (data) => DashboardApi.endpoints.saveNotificationSettings(data);

})();
