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
                requestHeaders['X-Requested-With'] = 'XMLHttpRequest';
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

    window.DashboardApi.delete = function(url, options = {}) {
        return this.request(url, { method: 'DELETE', ...options });
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
        getInfo: () => DashboardApi.get('/info'),

        // Clusters
        getClusters: () => DashboardApi.get('/clusters'),
        addCluster: (data) => DashboardApi.post('/clusters', data),
        updateCluster: (id, data) => DashboardApi.put(`/clusters/${id}`, data),
        deleteCluster: (id) => DashboardApi.delete(`/clusters/${id}`),

        // Routes
        getRoutes: () => DashboardApi.get('/routes'),
        addRoute: (data) => DashboardApi.post('/routes', data),
        updateRoute: (id, data) => DashboardApi.put(`/routes/${id}`, data),
        deleteRoute: (id) => DashboardApi.delete(`/routes/${id}`),

        // Logs
        getLogs: (count = 100) => DashboardApi.get('/logs', { count }),
        clearLogs: () => DashboardApi.delete('/logs'),

        // Statistics
        getStats: () => DashboardApi.get('/stats'),

        // Rate Limit Status
        getRateLimitStatus: () => DashboardApi.get('/rate-limit'),

        // Config History
        getHistory: () => DashboardApi.get('/api/config/history'),
        rollback: (versionId) => DashboardApi.post(`/api/config/rollback/${versionId}`),
        createSnapshot: (description) => DashboardApi.post('/api/config/snapshot', { description }),

        // Auth
        login: (credentials) => DashboardApi.post('/login', credentials, { requireAuth: false }),
        getAuthStatus: () => DashboardApi.get('/auth/status'),

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
        getAuditLogs: (count, action) => DashboardApi.get('/audit-logs', { count: count || 100, action: action || '' }),

        // Circuit Breaker
        getCircuitBreakerStatus: () => DashboardApi.get('/circuit-breaker/status'),
        resetCircuitBreakers: () => DashboardApi.post('/circuit-breaker/reset'),

        // Webhook Settings
        getWebhookSettings: () => DashboardApi.get('/api/config/webhook'),
        saveWebhookSettings: (data) => DashboardApi.put('/api/config/webhook', data)
    };

})();
