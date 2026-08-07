/**
 * Request Retry Module - Full CRUD plugin binding manager
 */
(function() {
    'use strict';
    window.PluginBindingManager.create({
        pluginId: 'request-retry',
        scope: 'Route',
        containerId: 'retry-content',
        refreshTimeId: 'retry-refresh-time',
        moduleName: 'RetryModule',
        icon: 'bi-arrow-repeat',
        color: 'text-primary',
        titleKey: 'plugin.name.request-retry',
        descKey: 'pluginPage.desc.request-retry',
        helpKey: 'pluginPage.help.request-retry'
    });
})();
