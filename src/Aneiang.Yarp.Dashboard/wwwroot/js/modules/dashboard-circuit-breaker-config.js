/**
 * Circuit Breaker Config Module - Plugin binding manager (Cluster scope)
 */
(function() {
    'use strict';
    window.PluginBindingManager.create({
        pluginId: 'circuit-breaker',
        scope: 'Cluster',
        scopeLabel: __('pluginPage.cluster'),
        containerId: 'cb-config-content',
        refreshTimeId: 'cb-config-refresh-time',
        moduleName: 'CircuitBreakerConfigModule',
        icon: 'bi-lightning-charge',
        color: 'text-warning',
        titleKey: 'plugin.name.circuit-breaker',
        descKey: 'pluginPage.desc.circuit-breaker',
        helpKey: 'pluginPage.help.circuit-breaker'
    });
})();
