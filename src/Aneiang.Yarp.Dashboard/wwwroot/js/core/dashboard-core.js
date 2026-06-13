/**
 * Dashboard Core - Minimal core for multi-page architecture
 * Tab switching is now handled by server-side routing (individual pages)
 */
(function() {
    'use strict';

    let _initialized = false;

    window.DashboardCore = window.DashboardCore || {};

    window.DashboardCore.init = function() {
        if (_initialized) return;
        _initialized = true;

        console.log('[Core] Dashboard multi-page architecture initialized');
        
        // Set page identifier on body for CSS targeting
        var currentPage = window.__dashboard?.currentPage || 'overview';
        document.body.setAttribute('data-page', currentPage);
    };

    // Auto-initialize on DOM ready
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
