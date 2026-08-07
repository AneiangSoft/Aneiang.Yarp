/**
 * WAF Module - Full CRUD plugin binding manager
 */
(function() {
    'use strict';
    window.PluginBindingManager.create({
        pluginId: 'waf',
        scope: 'Route',
        containerId: 'waf-content',
        refreshTimeId: 'waf-refresh-time',
        moduleName: 'WafModule',
        icon: 'bi-shield-lock',
        color: 'text-danger',
        titleKey: 'plugin.name.waf',
        descKey: 'pluginPage.desc.waf',
        helpKey: 'pluginPage.help.waf'
    });
})();
