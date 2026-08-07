/**
 * Response Cache Module - Full CRUD plugin binding manager
 */
(function() {
    'use strict';
    window.PluginBindingManager.create({
        pluginId: 'response-cache',
        scope: 'Route',
        containerId: 'cache-content',
        refreshTimeId: 'cache-refresh-time',
        moduleName: 'ResponseCacheModule',
        icon: 'bi-hdd-network',
        color: 'text-success',
        titleKey: 'plugin.name.response-cache',
        descKey: 'pluginPage.desc.response-cache',
        helpKey: 'pluginPage.help.response-cache'
    });
})();
