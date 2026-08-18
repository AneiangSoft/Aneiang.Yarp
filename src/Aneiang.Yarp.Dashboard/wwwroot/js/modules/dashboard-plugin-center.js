/**
 * Plugin Center - Aggregated plugin management page.
 * Consolidates 11 individual plugin pages into one tabbed interface.
 *
 * Two types of tabs:
 *  - configType: pure PluginBindingManager CRUD (7 plugins)
 *  - featureType: independent monitoring UI + lazy-loaded JS (4 plugins)
 */
(function() {
    'use strict';

    var MODULE_BASE = '/_content/Aneiang.Yarp.Dashboard/js/modules/';
    var VENDOR_BASE = '/_content/Aneiang.Yarp.Dashboard/js/vendor/';

    function t(key, fallback) {
        if (typeof window.__ === 'function') {
            var v = window.__(key);
            if (v && v !== key) return v;
        }
        return fallback || key;
    }

    function esc(s) {
        if (typeof window.DashboardUtils !== 'undefined' && DashboardUtils.esc) return DashboardUtils.esc(s);
        var d = document.createElement('div');
        d.textContent = s || '';
        return d.innerHTML;
    }

    /* ── Plugin registry ──
       Each entry: {
         id, label, labelKey, icon, color, order, type ('config'|'feature'),
         group ('strategy'|'monitor'),
         config (PluginBindingManager.create params, for config type),
         script (JS file to lazy-load, for feature type),
         deps (dependency JS files to load first),
         init (function called after scripts loaded)
       } */
    var REGISTRY = [
        /* ── Strategy / Config plugins ── */
        { id: 'rate-limit', labelKey: 'menu.rateLimit', label: '限流策略', icon: 'bi-speedometer2', color: 'text-warning', order: 200, type: 'config', group: 'strategy',
          config: { pluginId: 'rate-limit', scope: 'Route', containerId: 'pc-rate-limit-content', refreshTimeId: 'pc-rate-limit-refresh-time', moduleName: 'RateLimitModule', icon: 'bi-speedometer2', color: 'text-warning', titleKey: 'plugin.name.rate-limit', descKey: 'pluginPage.desc.rate-limit', helpKey: 'pluginPage.help.rate-limit' } },
        { id: 'rate-limit-redis', labelKey: 'menu.rateLimitRedis', label: '分布式限流', icon: 'bi-hdd-rack', color: 'text-warning', order: 210, type: 'config', group: 'strategy',
          config: { pluginId: 'rate-limit-redis', scope: 'Route', containerId: 'pc-rate-limit-redis-content', refreshTimeId: 'pc-rate-limit-redis-refresh-time', moduleName: 'RateLimitRedisModule', icon: 'bi-hdd-rack', color: 'text-warning' } },
        { id: 'waf', labelKey: 'menu.waf', label: 'WAF 防火墙', icon: 'bi-shield-lock', color: 'text-danger', order: 220, type: 'config', group: 'strategy',
          config: { pluginId: 'waf', scope: 'Route', containerId: 'pc-waf-content', refreshTimeId: 'pc-waf-refresh-time', moduleName: 'WafModule', icon: 'bi-shield-lock', color: 'text-danger', titleKey: 'plugin.name.waf', descKey: 'pluginPage.desc.waf', helpKey: 'pluginPage.help.waf' } },
        { id: 'retry', labelKey: 'menu.retry', label: '请求重试', icon: 'bi-arrow-repeat', color: 'text-primary', order: 230, type: 'config', group: 'strategy',
          config: { pluginId: 'request-retry', scope: 'Route', containerId: 'pc-retry-content', refreshTimeId: 'pc-retry-refresh-time', moduleName: 'RetryModule', icon: 'bi-arrow-repeat', color: 'text-primary', titleKey: 'plugin.name.request-retry', descKey: 'pluginPage.desc.request-retry', helpKey: 'pluginPage.help.request-retry' } },
        { id: 'response-cache', labelKey: 'menu.responseCache', label: '响应缓存', icon: 'bi-hdd-network', color: 'text-success', order: 240, type: 'config', group: 'strategy',
          config: { pluginId: 'response-cache', scope: 'Route', containerId: 'pc-cache-content', refreshTimeId: 'pc-cache-refresh-time', moduleName: 'ResponseCacheModule', icon: 'bi-hdd-network', color: 'text-success', titleKey: 'plugin.name.response-cache', descKey: 'pluginPage.desc.response-cache', helpKey: 'pluginPage.help.response-cache' } },
        { id: 'service-discovery', labelKey: 'menu.serviceDiscovery', label: '服务发现', icon: 'bi-binoculars', color: 'text-primary', order: 250, type: 'config', group: 'strategy',
          config: { pluginId: 'service-discovery', scope: 'Cluster', scopeLabel: t('pluginPage.cluster', 'Cluster'), containerId: 'pc-sd-content', refreshTimeId: 'pc-sd-refresh-time', moduleName: 'ServiceDiscoveryModule', icon: 'bi-binoculars', color: 'text-primary', titleKey: 'plugin.name.service-discovery', descKey: 'pluginPage.desc.service-discovery', helpKey: 'pluginPage.help.service-discovery' } },
        { id: 'compression', labelKey: 'menu.compression', label: '响应压缩', icon: 'bi-file-zip', color: 'text-success', order: 260, type: 'config', group: 'strategy',
          config: { pluginId: 'compression', scope: 'Route', containerId: 'pc-compression-content', refreshTimeId: 'pc-compression-refresh-time', moduleName: 'CompressionModule', icon: 'bi-file-zip', color: 'text-success' } },
        { id: 'proxy-log', labelKey: 'menu.logs', label: '日志插件', icon: 'bi-journal-text', color: 'text-info', order: 270, type: 'config', group: 'strategy',
          config: { pluginId: 'proxy-log', scope: 'Route', containerId: 'pc-proxy-log-content', refreshTimeId: 'pc-proxy-log-refresh-time', moduleName: 'ProxyLogConfigModule', icon: 'bi-journal-text', color: 'text-info', titleKey: 'plugin.name.proxy-log', descKey: 'pluginPage.desc.proxy-log', helpKey: 'pluginPage.help.proxy-log' } }
    ];

    var loaded = {};      /* track which feature tabs have been initialized */
    var configModules = {}; /* track which config modules have been created */
    var activeTab = null;

    function findEntry(id) {
        for (var i = 0; i < REGISTRY.length; i++) { if (REGISTRY[i].id === id) return REGISTRY[i]; }
        return null;
    }

    /* Dynamically load a JS file, returns a Promise */
    function loadScript(src) {
        return new Promise(function(resolve, reject) {
            var existing = document.querySelector('script[src="' + src + '"]');
            if (existing) { resolve(); return; }
            var s = document.createElement('script');
            s.src = src;
            s.onload = resolve;
            s.onerror = function() { reject(new Error('Failed to load ' + src)); };
            document.head.appendChild(s);
        });
    }

    function loadScriptsSequentially(scripts) {
        return scripts.reduce(function(p, src) {
            return p.then(function() { return loadScript(src); });
        }, Promise.resolve());
    }

    /* ── Render the tab bar ── */
    function renderTabBar(container) {
        var html = '';
        html += '<div class="pc-tabs-bar">';

        /* Strategy group */
        html += '<span class="pc-tab-group-label">' + t('pc.strategy', '策略配置') + '</span>';
        REGISTRY.filter(function(e) { return e.group === 'strategy'; }).forEach(function(e) {
            html += '<button class="pc-tab-btn' + (activeTab === e.id ? ' active' : '') + '" data-plugin="' + e.id + '" onclick="PluginCenter.activate(\'' + e.id + '\')">';
            html += '<i class="bi ' + e.icon + ' ' + e.color + ' me-1"></i><span>' + t(e.labelKey, e.label) + '</span>';
            html += '</button>';
        });

        html += '</div>';
        container.innerHTML = html;
    }

    /* ── Render the content for a config-type tab ── */
    function renderConfigPanel(entry) {
        var c = entry.config;
        var panel = document.querySelector('.pc-tab-panel[data-plugin="' + entry.id + '"]');
        if (!panel) return;
        var html = '';
        html += '<div class="card-panel mb-0">';
        html += '  <div class="card-header">';
        html += '    <span><i class="bi ' + c.icon + ' me-2 ' + c.color + '"></i><span>' + (c.titleKey ? t(c.titleKey, entry.label) : entry.label) + '</span></span>';
        html += '    <div class="card-header-actions">';
        html += '      <span id="' + c.refreshTimeId + '" class="refresh-badge"></span>';
        html += '      <button class="btn btn-sm btn-outline-primary btn-icon-only" onclick="PluginCenter.reload(\'' + entry.id + '\')" title="' + t('index.btn.refresh', '刷新') + '" aria-label="' + t('index.btn.refresh', '刷新') + '"><i class="bi bi-arrow-clockwise"></i></button>';
        html += '    </div>';
        html += '  </div>';
        html += '  <div id="' + c.containerId + '" class="card-body"><div class="loading-state"><div class="loading-spinner"></div><div class="loading-text">' + t('common.loading', '加载中...') + '</div></div></div>';
        html += '</div>';
        panel.innerHTML = html;
    }

    /* ── Activate a tab ── */
    async function activate(id) {
        var entry = findEntry(id);
        if (!entry) return;

        /* Update tab bar active state */
        document.querySelectorAll('.pc-tab-btn').forEach(function(btn) {
            btn.classList.toggle('active', btn.getAttribute('data-plugin') === id);
        });

        /* Show/hide panels */
        document.querySelectorAll('.pc-tab-panel').forEach(function(p) {
            p.style.display = p.getAttribute('data-plugin') === id ? '' : 'none';
        });

        activeTab = id;

        /* Update URL hash without scrolling */
        if (window.location.hash !== '#' + id) {
            history.replaceState(null, '', '#' + id);
        }

        if (entry.type === 'config') {
            await initConfigTab(entry);
        }
    }

    /* ── Initialize a config-type tab (lazy create + load) ── */
    async function initConfigTab(entry) {
        if (!configModules[entry.id]) {
            renderConfigPanel(entry);
            if (window.PluginBindingManager) {
                var cfg = Object.assign({}, entry.config);
                configModules[entry.id] = window.PluginBindingManager.create(cfg);
            }
        }
        if (configModules[entry.id]) {
            await configModules[entry.id].load();
        }
    }

    /* ── Reload a config tab ── */
    async function reload(id) {
        var entry = findEntry(id);
        if (!entry) return;
        if (entry.type === 'config' && configModules[id]) {
            await configModules[id].load();
        }
    }

    /* ── Init: read hash, build tabs, activate default ── */
    async function init() {
        var tabBarContainer = document.getElementById('pc-tab-bar');
        if (!tabBarContainer) return;

        renderTabBar(tabBarContainer);

        /* Determine initial tab from hash or default to first */
        var hash = window.location.hash.replace('#', '');
        var initial = findEntry(hash) ? hash : REGISTRY[0].id;

        await activate(initial);
    }

    window.PluginCenter = {
        init: init,
        activate: activate,
        reload: reload,
        REGISTRY: REGISTRY
    };
})();
