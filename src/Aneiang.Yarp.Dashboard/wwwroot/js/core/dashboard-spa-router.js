/**
 * Dashboard SPA Router - Single Page Application navigation via hash routing.
 * Keeps page state and prevents full page reloads when switching tabs.
 * URL format: /{prefix}/#{page}  e.g. /apigateway/#clusters
 */
(function() {
    'use strict';

    window.DashboardRouter = window.DashboardRouter || {
        _initialized: false,
        _currentTab: null,
        _pageCache: new Map(),
        _maxCacheSize: 10,
        _loadIndicatorTimer: null,

        init: function() {
            if (this._initialized) return;
            this._initialized = true;

            document.addEventListener('click', this._handleClick.bind(this));
            window.addEventListener('hashchange', this._handleHashChange.bind(this));
            window.addEventListener('popstate', this._handlePopState.bind(this));

            // Initial tab from URL hash
            var initialTab = location.hash.replace('#', '').split('/').pop() || 'overview';
            this.navigate(initialTab, false);

            console.log('[Router] SPA router initialized, initial tab:', initialTab);
        },

        cleanup: function() {
            document.removeEventListener('click', this._handleClick.bind(this));
            window.removeEventListener('hashchange', this._handleHashChange.bind(this));
            window.removeEventListener('popstate', this._handlePopState.bind(this));
            this._pageCache.clear();
            this._initialized = false;
        },

        navigate: function(tab, pushState) {
            if (!tab) tab = 'overview';
            if (tab === this._currentTab && pushState !== false) return;

            var dashPrefix = window.__dashboard?.routePrefix || 'apigateway';
            var newHash = '/' + dashPrefix + '#' + tab;
            var prefix = '/' + dashPrefix;

            if (pushState !== false) {
                history.pushState({ tab: tab }, '', newHash);
            }

            this._switchTab(tab);
            this._currentTab = tab;
            this._updateActiveMenuItem(tab);

            console.log('[Router] Navigated to tab:', tab);
        },

        _handleClick: function(e) {
            var link = e.target.closest('a');
            if (!link) return;
            if (e.ctrlKey || e.metaKey || e.shiftKey || e.altKey) return;
            if (link.hasAttribute('data-no-spa')) return;

            var href = link.getAttribute('href');
            if (!href) return;

            // Skip external links
            if (href.startsWith('http') || href.startsWith('//')) return;

            // Extract hash part
            var hashIndex = href.indexOf('#');
            if (hashIndex === -1) return; // No hash, let browser handle (e.g. login page)

            var pathPart = href.substring(0, hashIndex);
            var hashPart = href.substring(hashIndex + 1);

            if (!hashPart) return;

            // Skip if path doesn't match our dashboard prefix
            var dashPrefix = window.__dashboard?.routePrefix || 'apigateway';
            var expectedPath = '/' + dashPrefix;
            if (pathPart !== expectedPath && pathPart !== dashPrefix) return;

            e.preventDefault();

            // If tab content is embedded (not cached), just switch tabs
            if (document.getElementById('tab-' + hashPart) !== null) {
                this.navigate(hashPart, true);
                return;
            }

            // Otherwise, use AJAX page load (for standalone pages)
            this._loadPage('/' + dashPrefix + '/' + hashPart, hashPart);
        },

        _handleHashChange: function() {
            var tab = location.hash.replace('#', '').split('/').pop() || 'overview';
            if (tab !== this._currentTab) {
                this._switchTab(tab);
                this._currentTab = tab;
                this._updateActiveMenuItem(tab);
            }
        },

        _handlePopState: function(e) {
            var tab = location.hash.replace('#', '').split('/').pop() || 'overview';
            if (tab !== this._currentTab) {
                this._switchTab(tab);
                this._currentTab = tab;
                this._updateActiveMenuItem(tab);
            }
        },

        _switchTab: function(tab) {
            var self = this;

            // Hide all tab content sections
            document.querySelectorAll('.page-tab-content').forEach(function(el) {
                el.style.display = 'none';
            });

            // Deactivate all tab buttons
            document.querySelectorAll('.top-tab-btn').forEach(function(btn) {
                btn.classList.remove('active');
            });

            // Show target tab content
            var targetContent = document.getElementById('tab-' + tab);
            if (targetContent) {
                targetContent.style.display = '';

                // Trigger lazy load for this tab (first time only)
                if (!targetContent.dataset.loaded) {
                    targetContent.dataset.loaded = '1';
                    self._triggerTabLoad(tab);
                }
            }

            // Activate corresponding tab button
            var targetBtn = document.querySelector('.top-tab-btn[data-tab="' + tab + '"]');
            if (targetBtn) targetBtn.classList.add('active');

            // Re-init i18n for new content
            if (window.DashboardI18n && typeof window.DashboardI18n.init === 'function') {
                DashboardI18n.init();
            }

            document.dispatchEvent(new CustomEvent('dashboard:tabChanged', { detail: { tab: tab } }));
        },

        _triggerTabLoad: function(tab) {
            console.log('[Router] Lazy loading tab:', tab);
            var handlers = {
                'overview': function() {
                    if (window.DashboardApp?.modules?.home?.loadInfo) DashboardApp.modules.home.loadInfo();
                    if (window.OpsModule?.loadAlertSummary) OpsModule.loadAlertSummary();
                    if (window.OpsModule?.loadTrafficChart) OpsModule.loadTrafficChart();
                    if (window.OpsModule?.loadTopErrors) OpsModule.loadTopErrors();
                },
                'clusters': function() {
                    if (window.DashboardApp?.modules?.clusters?.init) DashboardApp.modules.clusters.init();
                    if (window.DashboardApp?.modules?.clusters?.loadClusters) DashboardApp.modules.clusters.loadClusters();
                },
                'routes': function() {
                    if (window.DashboardApp?.modules?.routes?.init) DashboardApp.modules.routes.init();
                    if (window.DashboardApp?.modules?.routes?.loadRoutes) DashboardApp.modules.routes.loadRoutes();
                },
                'stats': function() {
                    if (window.DashboardApp?.modules?.stats?.init) DashboardApp.modules.stats.init();
                    if (window.DashboardApp?.modules?.stats?.loadStats) DashboardApp.modules.stats.loadStats();
                },
                'logs': function() {
                    if (window.DashboardApp?.modules?.logs?.init) DashboardApp.modules.logs.init();
                    if (window.DashboardApp?.modules?.logs?.loadLogs) DashboardApp.modules.logs.loadLogs();
                },
                'circuits': function() {
                    if (window.DashboardApp?.modules?.circuits?.init) DashboardApp.modules.circuits.init();
                    if (window.DashboardApp?.modules?.circuits?.loadCircuits) DashboardApp.modules.circuits.loadCircuits();
                },
                'alerts': function() {
                    if (window.DashboardApp?.modules?.alerts?.init) DashboardApp.modules.alerts.init();
                    if (window.DashboardApp?.modules?.alerts?.loadAlerts) DashboardApp.modules.alerts.loadAlerts();
                },
                'security': function() {
                    if (window.DashboardApp?.modules?.security?.init) DashboardApp.modules.security.init();
                    if (window.DashboardApp?.modules?.security?.loadSecurity) DashboardApp.modules.security.loadSecurity();
                },
                'healthcheck': function() {
                    if (window.DashboardApp?.modules?.healthcheck?.init) DashboardApp.modules.healthcheck.init();
                    if (window.DashboardApp?.modules?.healthcheck?.loadData) DashboardApp.modules.healthcheck.loadData();
                },
                'history': function() {
                    if (window.DashboardApp?.modules?.history?.init) DashboardApp.modules.history.init();
                    if (window.DashboardApp?.modules?.history?.loadHistory) DashboardApp.modules.history.loadHistory();
                },
                'policies': function() {
                    if (window.DashboardApp?.modules?.policies?.init) DashboardApp.modules.policies.init();
                    if (window.DashboardApp?.modules?.policies?.loadPolicies) DashboardApp.modules.policies.loadPolicies();
                },
                'plugins': function() {
                    if (window.DashboardApp?.modules?.plugins?.init) DashboardApp.modules.plugins.init();
                    if (window.DashboardApp?.modules?.plugins?.loadPlugins) DashboardApp.modules.plugins.loadPlugins();
                },
                'audit': function() {
                    if (window.DashboardApp?.modules?.audit?.init) DashboardApp.modules.audit.init();
                    if (window.DashboardApp?.modules?.audit?.loadAudit) DashboardApp.modules.audit.loadAudit();
                },
                'settings': function() {
                    if (window.DashboardApp?.modules?.settings?.init) DashboardApp.modules.settings.init();
                    if (window.DashboardApp?.modules?.settings?.loadSettings) DashboardApp.modules.settings.loadSettings();
                }
            };

            if (handlers[tab]) {
                try { handlers[tab](); } catch (e) { console.error('[Router] Tab load error:', e); }
            }

            document.dispatchEvent(new CustomEvent('dashboard:tabLoaded', { detail: { tab: tab } }));
        },

        _loadPage: function(path, tab) {
            var self = this;
            if (this._loadIndicatorTimer) clearTimeout(this._loadIndicatorTimer);
            this._loadIndicatorTimer = setTimeout(function() {
                var el = document.getElementById('page-loading-bar');
                if (el) el.style.display = '';
            }, 200);

            fetch(path, {
                headers: { 'X-Requested-With': 'XMLHttpRequest', 'Accept': 'text/html' }
            })
            .then(function(r) { return r.text(); })
            .then(function(html) {
                var content = self._extractContent(html);
                self._pageCache.set(path, { html: content, timestamp: Date.now() });
                if (self._pageCache.size > self._maxCacheSize) {
                    var firstKey = self._pageCache.keys().next().value;
                    self._pageCache.delete(firstKey);
                }
            })
            .catch(function() {})
            .finally(function() {
                if (self._loadIndicatorTimer) clearTimeout(self._loadIndicatorTimer);
                var el = document.getElementById('page-loading-bar');
                if (el) el.style.display = 'none';
            });
        },

        _extractContent: function(html) {
            var parser = new DOMParser();
            var doc = parser.parseFromString(html, 'text/html');
            var main = doc.querySelector('.main-content');
            return main ? main.innerHTML : doc.body.innerHTML;
        },

        _updateActiveMenuItem: function(tab) {
            document.querySelectorAll('.sidebar .nav-link').forEach(function(link) {
                link.classList.remove('active');
            });
            var match = document.querySelector('.sidebar .nav-link[href$="#' + tab + '"]');
            if (match) match.classList.add('active');
        }
    };

    // Auto-init
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function() { window.DashboardRouter.init(); });
    } else {
        window.DashboardRouter.init();
    }
})();
