/**
 * Dashboard App Bootstrap - Multi-page architecture
 * Each page now defines its own module loading in @section Scripts
 * This file only handles core utility initialization + module registration
 */
(function() {
    'use strict';

    // ===== Application State =====
    window.DashboardApp = window.DashboardApp || {
        version: '2.3.0',
        initialized: false,
        modules: {}
    };

    // ===== Module Registration =====
    window.DashboardApp.registerModule = function(name, module) {
        this.modules[name] = module;
        console.log('[Dashboard] Module registered:', name);
    };

    // ===== Module Cleanup =====
    window.DashboardApp.cleanup = function() {
        // Cleanup modules first
        Object.keys(this.modules).forEach(function(name) {
            var module = this.modules[name];
            if (module && typeof module.destroy === 'function') {
                console.log('[Dashboard] Cleaning up module:', name);
                module.destroy();
            }
        }.bind(this));
        this.modules = {};

        // Cleanup core state
        if (window.DashboardState && typeof window.DashboardState.cleanup === 'function') {
            window.DashboardState.cleanup();
        }

        // Cleanup performance monitors
        if (window.DashboardPerformance && typeof window.DashboardPerformance.cleanupAll === 'function') {
            window.DashboardPerformance.cleanupAll();
        }

        // Cleanup events handlers
        if (window.DashboardEvents && typeof window.DashboardEvents.cleanup === 'function') {
            window.DashboardEvents.cleanup();
        }
    };

    // ===== Core Initialization (utilities only) =====
    window.DashboardApp.init = async function() {
        if (this.initialized) {
            console.warn('[Dashboard] Already initialized');
            return;
        }

        console.log('[Dashboard] Initializing v' + this.version + ' (multi-page)...');

        try {
            // Initialize core utilities
            if (window.DashboardUtils) DashboardUtils.init();
            if (window.DashboardApi) DashboardApi.init();
            if (window.DashboardState) DashboardState.init();
            if (window.DashboardI18n) DashboardI18n.init();
            if (window.DashboardStorage) DashboardStorage.init();
            if (window.DashboardCore) DashboardCore.init();

            this.initialized = true;
            console.log('[Dashboard] Core initialization complete');

        } catch (error) {
            console.error('[Dashboard] Initialization failed:', error);
            throw error;
        }
    };

    // Note: Core initialization is done per-page in @section Scripts
    // DashboardApp.init() is available but NOT auto-called here

})();
