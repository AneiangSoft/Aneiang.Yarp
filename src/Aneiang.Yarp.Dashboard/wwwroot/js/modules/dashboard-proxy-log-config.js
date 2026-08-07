/**
 * Proxy Log Config Module - Plugin binding manager (Route scope)
 */
(function() {
    'use strict';
    window.PluginBindingManager.create({
        pluginId: 'proxy-log',
        scope: 'Route',
        containerId: 'pl-config-content',
        refreshTimeId: 'pl-config-refresh-time',
        moduleName: 'ProxyLogConfigModule',
        icon: 'bi-journal-text',
        color: 'text-info',
        titleKey: 'plugin.name.proxy-log',
        descKey: 'pluginPage.desc.proxy-log',
        helpKey: 'pluginPage.help.proxy-log'
    });
})();
