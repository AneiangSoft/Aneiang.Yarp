/**
 * Dashboard API Layer - Aneiang.Yarp Gateway Dashboard
 * Unified API request handling with auth, error handling, and loading states
 */
(function() {
    'use strict';

    // ===== API Client =====
    var DashboardApi = {
        /**
         * Base path for all API requests
         */
        basePath: '',

        /**
         * Auth token for API requests
         */
        token: null,

        /**
         * Make an authenticated API request
         * @param {string} url - Relative URL path
         * @param {object} options - Fetch options
         * @returns {Promise<Response>}
         */
        async request(url, options) {
            options = options || {};
            options.headers = options.headers || {};
            
            if (this.token) {
                options.headers['Authorization'] = 'Bearer ' + this.token;
                options.headers['X-Requested-With'] = 'XMLHttpRequest';
            }

            var fullUrl = this.basePath + url;
            var res = await fetch(fullUrl, options);

            if (res.status === 401) {
                localStorage.removeItem('dashboard_token');
                window.location.href = this.basePath + '/login';
                throw new Error('Unauthorized');
            }

            return res;
        },

        /**
         * GET request
         * @param {string} url - Relative URL path
         * @param {object} params - Query parameters
         * @returns {Promise<object>} Response data
         */
        async get(url, params) {
            if (params) {
                var queryString = Object.keys(params)
                    .filter(function(key) { return params[key] !== undefined && params[key] !== null; })
                    .map(function(key) {
                        return encodeURIComponent(key) + '=' + encodeURIComponent(params[key]);
                    })
                    .join('&');
                if (queryString) url += '?' + queryString;
            }

            var res = await this.request(url, { method: 'GET' });
            return await res.json();
        },

        /**
         * POST request
         * @param {string} url - Relative URL path
         * @param {object} data - Request body
         * @returns {Promise<object>} Response data
         */
        async post(url, data) {
            var res = await this.request(url, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data)
            });
            return await res.json();
        },

        /**
         * DELETE request
         * @param {string} url - Relative URL path
         * @returns {Promise<object>} Response data
         */
        async delete(url) {
            var res = await this.request(url, { method: 'DELETE' });
            return await res.json();
        },

        /**
         * PUT request
         * @param {string} url - Relative URL path
         * @param {object} data - Request body
         * @returns {Promise<object>} Response data
         */
        async put(url, data) {
            var res = await this.request(url, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(data)
            });
            return await res.json();
        }
    };

    // ===== API Methods =====
    var ApiMethods = {
        /**
         * Get gateway info
         * @returns {Promise<object>}
         */
        getInfo() {
            return DashboardApi.get('/info');
        },

        /**
         * Get all clusters
         * @returns {Promise<object>}
         */
        getClusters() {
            return DashboardApi.get('/clusters');
        },

        /**
         * Get all routes
         * @returns {Promise<object>}
         */
        getRoutes() {
            return DashboardApi.get('/routes');
        },

        /**
         * Get recent logs
         * @param {number} count - Number of logs to fetch
         * @returns {Promise<object>}
         */
        getLogs(count) {
            return DashboardApi.get('/logs', { count: count || 100 });
        },

        /**
         * Clear all logs
         * @returns {Promise<object>}
         */
        clearLogs() {
            return DashboardApi.delete('/logs');
        },

        /**
         * Login to dashboard
         * @param {string} username
         * @param {string} password
         * @returns {Promise<object>}
         */
        login(username, password) {
            return DashboardApi.post('/login', {
                username: username,
                password: password
            });
        },

        /**
         * Get auth status
         * @returns {Promise<object>}
         */
        getAuthStatus() {
            return DashboardApi.get('/auth/status');
        }
    };

    // ===== Export to global =====
    window.dashboardApi = DashboardApi;
    window.dashboardApiMethods = ApiMethods;

    // Initialize base path and token from dashboard config
    document.addEventListener('DOMContentLoaded', function() {
        var d = window.__dashboard;
        if (d) {
            DashboardApi.basePath = d.basePath || '/dashboard';
            DashboardApi.token = d.token || null;
        }
    });

})();
