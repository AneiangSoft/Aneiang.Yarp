/**
 * Distributed Rate Limit (Redis) plugin page module.
 * Renders plugin status and manages route bindings via PluginBindingManager.
 */
window.RateLimitRedisModule = (function() {
    'use strict';

    const PLUGIN_ID = 'rate-limit-redis';
    let bindingManager = null;

    async function load() {
        try {
            if (!bindingManager) {
                bindingManager = PluginBindingManager.create({
                    pluginId: PLUGIN_ID,
                    scope: 'Route',
                    containerId: 'rate-limit-redis-content',
                    refreshTimeId: 'rate-limit-redis-refresh-time',
                    moduleName: 'RateLimitRedisModule',
                    icon: 'bi-hdd-rack',
                    color: 'text-warning'
                });
            }
            await bindingManager.load();
        } catch (e) {
            console.error('[RateLimitRedis] load failed:', e);
        }
    }

    return { load };
})();
