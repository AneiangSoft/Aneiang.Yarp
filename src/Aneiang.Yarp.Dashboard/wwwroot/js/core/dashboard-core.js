/**
 * Dashboard Core - Minimal core for multi-page architecture
 * Tab switching is now handled by server-side routing (individual pages)
 */
(function() {
    'use strict';

    let _initialized = false;

    window.DashboardCore = window.DashboardCore || {};

    window.DashboardApp = window.DashboardApp || {
        modules: {},

        registerModule: function(name, module) {
            this.modules[name] = module;
        },

        navigateTo: function(page) {
            var basePath = (window.__dashboard && window.__dashboard.basePath) || '';
            window.location.href = basePath + '/' + page;
        }
    };

    window.DashboardCore.init = function() {
        if (_initialized) return;
        _initialized = true;

        // Set page identifier on body for CSS targeting
        var currentPage = window.__dashboard?.currentPage || 'overview';
        document.body.setAttribute('data-page', currentPage);
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function() {
            if (window.DashboardCore) {
                window.DashboardCore.init();
            }
        });
    } else {
        if (window.DashboardCore) {
            window.DashboardCore.init();
        }
    }

})();
