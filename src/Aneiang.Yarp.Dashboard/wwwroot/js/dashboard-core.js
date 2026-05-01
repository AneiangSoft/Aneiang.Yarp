/**
 * Dashboard Core Module - Aneiang.Yarp Gateway Dashboard
 * Core setup, utilities, and initialization
 */
(function() {
    'use strict';

    // ===== Core Variables =====
    // These will be set by the inline init script in Index.cshtml
    window.__dashboard = window.__dashboard || {
        basePath: '/dashboard',
        token: null,
        logPanelEnabled: false,
        tabPanels: {
            overview: [],
            services: ['cluster-panel'],
            routes: ['route-panel']
        },
        allPanels: ['cluster-panel', 'route-panel'],
        CURRENT_LOCALE: 'zh-CN',
        I18N: {}
    };

    // ===== Auth Fetch =====
    window.authFetch = async function(url, options) {
        options = options || {};
        options.headers = options.headers || {};
        var d = window.__dashboard;
        if (d.token) {
            options.headers['Authorization'] = 'Bearer ' + d.token;
            options.headers['X-Requested-With'] = 'XMLHttpRequest';
        }
        var res = await fetch(url, options);
        if (res.status === 401) {
            localStorage.removeItem('dashboard_token');
            window.location.href = d.basePath + '/login';
            throw new Error('Unauthorized');
        }
        return res;
    };

    // ===== Tab Switching =====
    window.switchTab = function(tabName) {
        var d = window.__dashboard;
        // Update sidebar active state
        document.querySelectorAll('.sidebar .nav-link[data-tab]').forEach(function(l) {
            l.classList.toggle('active', l.dataset.tab === tabName);
        });
        // Show/hide panels
        d.allPanels.forEach(function(id) {
            var el = document.getElementById(id);
            if (el) el.style.display = (d.tabPanels[tabName] || []).includes(id) ? '' : 'none';
        });
    };

    // ===== Utility Functions =====
    window.timeStr = function() {
        var locale = window.__dashboard.CURRENT_LOCALE === 'en-US' ? 'en-US' : 'zh-CN';
        return new Date().toLocaleTimeString(locale, { hour12: false });
    };

    window.healthDot = function(status) {
        var s = (status || '').toLowerCase();
        var __ = window.__;
        if (s === 'healthy')   return '<span class="health-dot healthy"></span><span style="color:#22c55e;">' + __('index.health.healthy') + '</span>';
        if (s === 'unhealthy') return '<span class="health-dot unhealthy"></span><span style="color:#ef4444;">' + __('index.health.unhealthy') + '</span>';
        return '<span class="health-dot unknown"></span><span style="color:#f59e0b;">' + __('index.health.unknown') + '</span>';
    };

    window.escapeHtml = function(text) {
        if (!text) return '';
        var div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    };

    // ===== Manual Refresh =====
    window.manualRefresh = async function(btn) {
        if (btn) {
            btn.disabled = true;
            var icon = btn.querySelector('i');
            if (icon) icon.classList.add('spin');
        }
        await window.refreshAll();
        if (btn) {
            btn.disabled = false;
            var icon = btn.querySelector('i');
            if (icon) icon.classList.remove('spin');
        }
    };

    // ===== Full Refresh =====
    window.refreshAll = async function() {
        await Promise.all([window.loadInfo(), window.loadClusters(), window.loadRoutes()]);
    };

    // ===== Initialize Tab Click Handlers =====
    document.addEventListener('DOMContentLoaded', function() {
        document.querySelectorAll('.sidebar .nav-link[data-tab]').forEach(function(link) {
            link.addEventListener('click', function(e) {
                e.preventDefault();
                window.switchTab(this.dataset.tab);
            });
        });
    });
})();
