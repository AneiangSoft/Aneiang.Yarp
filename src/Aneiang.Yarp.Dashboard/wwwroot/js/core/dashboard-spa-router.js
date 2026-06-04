/**
 * Dashboard SPA Router - Single Page Application navigation
 * Keeps page state and prevents full page reloads when switching tabs
 */
(function() {
    'use strict';

    window.DashboardRouter = window.DashboardRouter || {};

    // ===== State =====
    let _isNavigating = false;
    let _currentPath = null;
    let _abortController = null;

    // ===== Cache for loaded pages =====
    const _pageCache = new Map();
    const _maxCacheSize = 10;

    // ===== Initialize =====
    window.DashboardRouter.init = function() {
        if (this._initialized) return;
        this._initialized = true;

        // Intercept all navigation link clicks
        document.addEventListener('click', handleLinkClick);

        // Handle browser back/forward buttons
        window.addEventListener('popstate', handlePopState);

        // Store initial path
        _currentPath = window.location.pathname;

        console.log('[Router] SPA router initialized');
    };

    // ===== Cleanup =====
    window.DashboardRouter.cleanup = function() {
        document.removeEventListener('click', handleLinkClick);
        window.removeEventListener('popstate', handlePopState);
        if (_abortController) {
            _abortController.abort();
        }
        _pageCache.clear();
        this._initialized = false;
    };

    // ===== Handle Link Clicks =====
    function handleLinkClick(e) {
        // Find closest anchor element
        const link = e.target.closest('a');
        if (!link) return;

        // Skip if modifier keys are pressed (let browser handle it)
        if (e.ctrlKey || e.metaKey || e.shiftKey || e.altKey) return;

        // Skip external links
        const href = link.getAttribute('href');
        if (!href || href.startsWith('http') || href.startsWith('//') || href.startsWith('#')) return;

        // Skip if explicitly marked to skip SPA
        if (link.hasAttribute('data-no-spa')) return;

        // Check if it's a dashboard link
        const dashPrefix = window.__dashboard?.routePrefix || 'apigateway';
        if (!href.startsWith('/' + dashPrefix) && !href.startsWith(dashPrefix)) return;

        // Skip if it's the logout link
        if (href.includes('logout') || link.getAttribute('onclick')) return;

        // Prevent default navigation
        e.preventDefault();

        // Navigate to the path
        navigateTo(href);
    }

    // ===== Handle Browser Back/Forward =====
    function handlePopState(e) {
        if (e.state && e.state.path) {
            loadPage(e.state.path, false);
        }
    }

    // ===== Navigate to Path =====
    window.DashboardRouter.navigateTo = navigateTo;
    function navigateTo(path, pushState = true) {
        if (_isNavigating || path === _currentPath) return;

        _isNavigating = true;

        // Show loading indicator
        showLoading();

        // Abort any pending navigation
        if (_abortController) {
            _abortController.abort();
        }
        _abortController = new AbortController();

        // Update URL if needed
        if (pushState) {
            window.history.pushState({ path: path }, '', path);
        }

        // Load the page content
        loadPageContent(path)
            .then(content => {
                updatePageContent(content, path);
                _currentPath = path;

                // Update active menu item
                updateActiveMenuItem(path);

                // Scroll to top
                window.scrollTo(0, 0);
            })
            .catch(err => {
                if (err.name !== 'AbortError') {
                    console.error('[Router] Navigation failed:', err);
                    // Fallback to full page reload on error
                    window.location.href = path;
                }
            })
            .finally(() => {
                hideLoading();
                _isNavigating = false;
            });
    }

    // ===== Load Page Content =====
    async function loadPageContent(path) {
        // Check cache first
        if (_pageCache.has(path)) {
            const cached = _pageCache.get(path);
            // Only use cache if it's less than 30 seconds old
            if (Date.now() - cached.timestamp < 30000) {
                console.log('[Router] Using cached page:', path);
                return cached.content;
            }
        }

        // Fetch the page
        const response = await fetch(path, {
            headers: {
                'X-Requested-With': 'XMLHttpRequest',
                'Accept': 'text/html'
            },
            signal: _abortController.signal
        });

        if (!response.ok) {
            throw new Error('Failed to load page: ' + response.status);
        }

        const html = await response.text();

        // Parse and extract main content
        const content = extractMainContent(html);

        // Cache the content
        cachePage(path, content);

        return content;
    }

    // ===== Extract Main Content from HTML =====
    function extractMainContent(html) {
        const parser = new DOMParser();
        const doc = parser.parseFromString(html, 'text/html');

        // Get the main content
        const mainContent = doc.querySelector('.main-content');
        if (!mainContent) {
            // Fallback: return body content if no main-content found
            return doc.body.innerHTML;
        }

        // Also extract any page-specific scripts
        const scripts = doc.querySelectorAll('script');
        const pageScripts = [];
        scripts.forEach(script => {
            // Skip library scripts and inline dashboard scripts
            if (script.src && (
                script.src.includes('bootstrap') ||
                script.src.includes('codemirror') ||
                script.src.includes('monaco') ||
                script.src.includes('dashboard-core.js') ||
                script.src.includes('dashboard-state.js') ||
                script.src.includes('dashboard-api.js') ||
                script.src.includes('dashboard-spa-router.js')
            )) {
                return;
            }
            // Keep module scripts
            if (script.src && script.src.includes('/js/modules/')) {
                pageScripts.push({ src: script.src, content: script.textContent });
            }
        });

        // Extract any section scripts
        const sectionScripts = doc.querySelectorAll('[data-page-script]');
        sectionScripts.forEach(script => {
            pageScripts.push({ content: script.textContent });
        });

        return {
            html: mainContent.innerHTML,
            scripts: pageScripts,
            title: doc.title
        };
    }

    // ===== Update Page Content =====
    function updatePageContent(content, path) {
        const mainContent = document.querySelector('.main-content');
        if (!mainContent) return;

        // Cleanup existing modules
        if (window.DashboardApp && typeof window.DashboardApp.cleanup === 'function') {
            window.DashboardApp.cleanup();
        }

        // Update content
        mainContent.innerHTML = content.html;

        // Update page title
        if (content.title) {
            document.title = content.title;
        }

        // Execute page scripts
        if (content.scripts && content.scripts.length > 0) {
            content.scripts.forEach(scriptInfo => {
                if (scriptInfo.src) {
                    // Load external script
                    loadScript(scriptInfo.src);
                } else if (scriptInfo.content) {
                    // Execute inline script
                    try {
                        // Use Function to execute in global scope but with proper error handling
                        new Function(scriptInfo.content)();
                    } catch (err) {
                        console.error('[Router] Script execution error:', err);
                    }
                }
            });
        }

        // Trigger page load event
        document.dispatchEvent(new CustomEvent('dashboard:pageLoaded', {
            detail: { path: path }
        }));

        // Re-initialize i18n for new content
        if (typeof initializeI18n === 'function') {
            initializeI18n();
        }

        // Initialize page-specific modules after a short delay to ensure scripts are loaded
        setTimeout(() => {
            initializePageModules(path);
        }, 50);
    }

    // ===== Initialize Page Modules =====
    function initializePageModules(path) {
        // Extract page name from path
        const pathParts = path.split('/').filter(p => p);
        const pageName = pathParts.length > 1 ? pathParts[1] : 'overview';

        console.log('[Router] Initializing page module:', pageName);

        // Map page names to their module initializers
        const moduleMap = {
            'overview': () => {
                if (window.DashboardApp?.modules?.overview?.loadData) {
                    window.DashboardApp.modules.overview.loadData();
                } else if (typeof loadOverviewData === 'function') {
                    loadOverviewData();
                }
            },
            'clusters': () => {
                if (window.DashboardApp?.modules?.clusters?.loadClusters) {
                    window.DashboardApp.modules.clusters.loadClusters();
                }
            },
            'routes': () => {
                if (window.DashboardApp?.modules?.routes?.loadRoutes) {
                    window.DashboardApp.modules.routes.loadRoutes();
                }
            },
            'topology': () => {
                if (window.DashboardApp?.modules?.topology?.init) {
                    window.DashboardApp.modules.topology.init();
                }
            },
            'stats': () => {
                if (window.DashboardApp?.modules?.stats?.loadStats) {
                    window.DashboardApp.modules.stats.loadStats();
                }
            },
            'logs': () => {
                if (window.DashboardApp?.modules?.logs?.loadLogs) {
                    window.DashboardApp.modules.logs.loadLogs();
                }
            },
            'circuits': () => {
                if (window.DashboardApp?.modules?.circuits?.loadCircuits) {
                    window.DashboardApp.modules.circuits.loadCircuits();
                }
            },
            'alerts': () => {
                if (window.DashboardApp?.modules?.alerts?.loadAlerts) {
                    window.DashboardApp.modules.alerts.loadAlerts();
                }
            },
            'security': () => {
                if (window.DashboardApp?.modules?.security?.loadSecurity) {
                    window.DashboardApp.modules.security.loadSecurity();
                }
            },
            'healthcheck': () => {
                if (window.DashboardApp?.modules?.healthcheck?.loadData) {
                    window.DashboardApp.modules.healthcheck.loadData();
                }
            },
            'history': () => {
                if (window.DashboardApp?.modules?.history?.loadHistory) {
                    window.DashboardApp.modules.history.loadHistory();
                }
            },
            'policies': () => {
                if (window.DashboardApp?.modules?.policies?.loadPolicies) {
                    window.DashboardApp.modules.policies.loadPolicies();
                }
            },
            'plugins': () => {
                if (window.DashboardApp?.modules?.plugins?.loadPlugins) {
                    window.DashboardApp.modules.plugins.loadPlugins();
                }
            },
            'audit': () => {
                if (window.DashboardApp?.modules?.audit?.loadAudit) {
                    window.DashboardApp.modules.audit.loadAudit();
                }
            },
            'settings': () => {
                if (window.DashboardApp?.modules?.settings?.loadSettings) {
                    window.DashboardApp.modules.settings.loadSettings();
                }
            }
        };

        // Execute module initializer if found
        if (moduleMap[pageName]) {
            try {
                moduleMap[pageName]();
            } catch (err) {
                console.error('[Router] Module init error:', err);
            }
        }

        // Also dispatch a custom event that modules can listen for
        document.dispatchEvent(new CustomEvent('dashboard:init:' + pageName));
    }

    // ===== Load External Script =====
    function loadScript(src) {
        // Check if script already exists
        if (document.querySelector('script[src="' + src + '"]')) {
            return Promise.resolve();
        }

        return new Promise((resolve, reject) => {
            const script = document.createElement('script');
            script.src = src;
            script.async = true;
            script.onload = resolve;
            script.onerror = reject;
            document.body.appendChild(script);
        });
    }

    // ===== Cache Page =====
    function cachePage(path, content) {
        // Remove oldest entries if cache is full
        while (_pageCache.size >= _maxCacheSize) {
            const firstKey = _pageCache.keys().next().value;
            _pageCache.delete(firstKey);
        }

        _pageCache.set(path, {
            content: content,
            timestamp: Date.now()
        });
    }

    // ===== Update Active Menu Item =====
    function updateActiveMenuItem(path) {
        // Remove active from all nav links
        document.querySelectorAll('.sidebar .nav-link').forEach(link => {
            link.classList.remove('active');
        });

        // Find and activate the matching link
        const links = document.querySelectorAll('.sidebar .nav-link');
        links.forEach(link => {
            const href = link.getAttribute('href');
            if (href && path === href) {
                link.classList.add('active');
            }
        });
    }

    // ===== Show/Hide Loading =====
    function showLoading() {
        // Create loading overlay if not exists
        let loader = document.getElementById('spa-loading-indicator');
        if (!loader) {
            loader = document.createElement('div');
            loader.id = 'spa-loading-indicator';
            loader.innerHTML = '<div class="spinner"></div>';
            loader.style.cssText = `
                position: fixed;
                top: 0;
                left: 240px;
                right: 0;
                bottom: 0;
                background: rgba(241, 245, 249, 0.8);
                display: flex;
                align-items: center;
                justify-content: center;
                z-index: 9999;
                opacity: 0;
                transition: opacity 0.2s ease;
            `;
            document.body.appendChild(loader);

            // Add spinner styles
            const style = document.createElement('style');
            style.textContent = `
                #spa-loading-indicator .spinner {
                    width: 40px;
                    height: 40px;
                    border: 3px solid #e2e8f0;
                    border-top-color: #6366f1;
                    border-radius: 50%;
                    animation: spin 0.8s linear infinite;
                }
                @keyframes spin {
                    to { transform: rotate(360deg); }
                }
            `;
            document.head.appendChild(style);
        }

        // Show with animation
        requestAnimationFrame(() => {
            loader.style.opacity = '1';
        });
    }

    function hideLoading() {
        const loader = document.getElementById('spa-loading-indicator');
        if (loader) {
            loader.style.opacity = '0';
            setTimeout(() => {
                if (loader.parentElement) {
                    loader.remove();
                }
            }, 200);
        }
    }

    // ===== Prefetch Page (for hover) =====
    window.DashboardRouter.prefetch = function(path) {
        if (_pageCache.has(path)) return;

        // Use requestIdleCallback if available
        const schedule = window.requestIdleCallback || window.setTimeout;
        schedule(() => {
            fetch(path, {
                headers: {
                    'X-Requested-With': 'XMLHttpRequest',
                    'Accept': 'text/html'
                }
            })
            .then(r => r.text())
            .then(html => {
                const content = extractMainContent(html);
                cachePage(path, content);
            })
            .catch(() => {
                // Silent fail for prefetch
            });
        }, { timeout: 2000 });
    };

    // Auto-initialize
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', () => window.DashboardRouter.init());
    } else {
        window.DashboardRouter.init();
    }

})();
