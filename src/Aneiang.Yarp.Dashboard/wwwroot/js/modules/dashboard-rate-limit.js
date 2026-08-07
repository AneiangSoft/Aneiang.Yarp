/**
 * Rate Limit Module - Full CRUD plugin binding manager
 */
(function() {
    'use strict';
    window.PluginBindingManager.create({
        pluginId: 'rate-limit',
        scope: 'Route',
        containerId: 'rate-limit-content',
        refreshTimeId: 'rate-limit-refresh-time',
        moduleName: 'RateLimitModule',
        icon: 'bi-speedometer2',
        color: 'text-warning',
        titleKey: 'plugin.name.rate-limit',
        descKey: 'pluginPage.desc.rate-limit',
        helpKey: 'pluginPage.help.rate-limit'
    });
})();
