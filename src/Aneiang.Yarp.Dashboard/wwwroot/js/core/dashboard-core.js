/**
 * Dashboard Core - Tab switching and panel visibility management
 */
(function() {
    'use strict';

    // ===== Tab Configuration =====
    const tabConfig = {
        overview: ['stat-bar'],
        services: ['stat-bar', 'cluster-panel'],
        routes:   ['stat-bar', 'route-panel'],
        logs:     ['stat-bar', 'log-panel']
    };

    let _initialized = false;

    // ===== Initialize Tab System =====
    window.DashboardCore = window.DashboardCore || {};

    window.DashboardCore.init = function() {
        if (_initialized) return;
        _initialized = true;

        console.log('[Core] Initializing tab system...');
        this.setupTabHandlers();
        this.setupPanelConfig();
        
        // Load initial tab from hash or default to overview
        const hash = window.location.hash.replace('#', '');
        if (hash && tabConfig[hash]) {
            this.switchTab(hash);
        } else {
            this.switchTab('overview');
        }
    };

    // ===== Setup Panel Configuration =====
    window.DashboardCore.setupPanelConfig = function() {
        window.__dashboard = window.__dashboard || {};
        // Always assign the full config to prevent incomplete overrides from inline scripts
        window.__dashboard.tabPanels = tabConfig;
        window.__dashboard.allPanels = ['stat-bar', 'cluster-panel', 'route-panel', 'log-panel'];
    };

    // ===== Setup Tab Click Handlers =====
    window.DashboardCore.setupTabHandlers = function() {
        document.querySelectorAll('.sidebar .nav-link[data-tab]').forEach(function(link) {
            link.addEventListener('click', function(e) {
                e.preventDefault();
                const tabName = this.getAttribute('data-tab');
                if (tabName) {
                    window.DashboardCore.switchTab(tabName);
                }
            });
        });
    };

    // ===== Switch Tab =====
    window.DashboardCore.switchTab = function(tabName) {
        console.log('[Core] Switching to tab:', tabName);
        
        // Update active state in sidebar
        document.querySelectorAll('.sidebar .nav-link[data-tab]').forEach(function(link) {
            if (link.getAttribute('data-tab') === tabName) {
                link.classList.add('active');
            } else {
                link.classList.remove('active');
            }
        });

        // Hide all panels
        const allPanels = window.__dashboard?.allPanels || 
            ['stat-bar', 'cluster-panel', 'route-panel', 'log-panel'];
        
        allPanels.forEach(function(panelId) {
            const panel = document.getElementById(panelId);
            if (panel) {
                panel.style.display = 'none';
            }
        });

        // Show panels for current tab
        const tabPanels = window.__dashboard?.tabPanels?.[tabName] || tabConfig[tabName] || [];
        tabPanels.forEach(function(panelId) {
            const panel = document.getElementById(panelId);
            if (panel) {
                panel.style.display = '';
            }
        });

        // Update URL hash
        window.location.hash = tabName;

        // Update state
        if (window.DashboardState) {
            window.DashboardState.set('app.currentTab', tabName);
        }

        // Trigger custom event
        document.dispatchEvent(new CustomEvent('dashboard:tabChanged', {
            detail: { tab: tabName }
        }));
    };

    // ===== Auto-initialize on DOM ready =====
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
