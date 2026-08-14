/**
 * Response Compression plugin page module.
 * Renders plugin status and manages route bindings via PluginBindingManager.
 */
window.CompressionModule = (function() {
    'use strict';

    const PLUGIN_ID = 'compression';
    let bindingManager = null;

    async function load() {
        try {
            if (!bindingManager) {
                bindingManager = PluginBindingManager.create({
                    pluginId: PLUGIN_ID,
                    scope: 'Route',
                    containerId: 'compression-content',
                    refreshTimeId: 'compression-refresh-time',
                    moduleName: 'CompressionModule',
                    icon: 'bi-file-zip',
                    color: 'text-success'
                });
            }
            await bindingManager.load();
        } catch (e) {
            console.error('[Compression] load failed:', e);
        }
    }

    return { load };
})();
