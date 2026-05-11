/**
 * Dashboard App Bootstrap - Aneiang.Yarp Gateway Dashboard
 * Application entry point and module initialization
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

    // ===== Initialization =====
    window.DashboardApp.init = async function() {
        if (this.initialized) {
            console.warn('[Dashboard] Already initialized');
            return;
        }

        console.log('[Dashboard] Initializing v' + this.version + '...');

        try {
            // 1. Initialize core utilities
            if (window.DashboardUtils) {
                window.DashboardUtils.init();
            }

            // 2. Initialize API layer
            if (window.DashboardApi) {
                window.DashboardApi.init();
            }

            // 3. Initialize state management
            if (window.DashboardState) {
                window.DashboardState.init();
            }

            // 4. Initialize i18n
            if (window.DashboardI18n) {
                window.DashboardI18n.init();
            }

            // 5. Initialize storage
            if (window.DashboardStorage) {
                window.DashboardStorage.init();
            }

            // 6. Initialize core (tab system)
            if (window.DashboardCore) {
                window.DashboardCore.init();
            }

            // 7. Load Schema service
            if (window.DashboardSchemaService) {
                console.log('[Dashboard] Loading JSON Schema...');
                await window.DashboardSchemaService.load();
            }

            // 8. Wait for Monaco Editor to be ready
            if (window.__monacoReady) {
                console.log('[Dashboard] Waiting for Monaco Editor...');
                await window.__monacoReady;
            }

            // 9. Initialize modules
            await this.initModules();

            // 10. Setup event handlers
            if (window.DashboardEvents) {
                window.DashboardEvents.setup();
            }

            this.initialized = true;
            console.log('[Dashboard] Initialization complete');

            // Trigger custom event
            document.dispatchEvent(new CustomEvent('dashboard:ready'));

        } catch (error) {
            console.error('[Dashboard] Initialization failed:', error);
            throw error;
        }
    };

    // ===== Module Initialization =====
    window.DashboardApp.initModules = async function() {
        const moduleOrder = [
            'home',
            'clusters',
            'routes',
            'logs',
            'configEditor',
            'modals'
        ];

        for (const moduleName of moduleOrder) {
            const module = this.modules[moduleName];
            if (module && typeof module.init === 'function') {
                try {
                    await module.init();
                    console.log('[Dashboard] Module initialized:', moduleName);
                } catch (error) {
                    console.error('[Dashboard] Module init failed:', moduleName, error);
                }
            }
        }
    };

    // ===== Auto-initialize on DOM ready =====
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function() {
            window.DashboardApp.init();
        });
    } else {
        window.DashboardApp.init();
    }

})();
