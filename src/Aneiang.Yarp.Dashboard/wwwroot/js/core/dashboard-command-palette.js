/**
 * Command Palette (Ctrl+K)
 * Global search and quick navigation
 */
(function() {
    'use strict';

    var palette = null;
    var input = null;
    var results = null;
    var items = [];
    var selectedIndex = 0;
    var recent = [];

    function t(key, fallback) {
        return (window.__ && window.__(key)) || fallback;
    }

    function getNavItems() {
        var navLinks = document.querySelectorAll('.sidebar .nav-link[data-page]');
        var result = [];
        navLinks.forEach(function(link) {
            var page = link.getAttribute('data-page');
            var title = link.getAttribute('data-title') || page;
            var icon = link.getAttribute('data-icon') || 'bi-app-indicator';
            var href = link.getAttribute('href');
            if (page && href && href !== 'javascript:void(0)') {
                result.push({ type: 'page', page: page, title: title, icon: icon, href: href });
            }
        });
        return result;
    }

    function getQuickActions() {
        var prefix = (window.__dashboard && window.__dashboard.routePrefix) || 'apigateway';
        return [
            { type: 'action', title: t('cmd.newCluster', 'New Cluster'), icon: 'bi-diagram-3', href: '/' + prefix + '/clusters' },
            { type: 'action', title: t('cmd.newRoute', 'New Route'), icon: 'bi-signpost-split', href: '/' + prefix + '/routes' },
            { type: 'action', title: t('cmd.viewLogs', 'View Logs'), icon: 'bi-journal-text', href: '/' + prefix + '/logs' },
            { type: 'action', title: t('cmd.pluginManager', 'Plugin Manager'), icon: 'bi-puzzle', href: '/' + prefix + '/plugins' }
        ];
    }

    function loadRecent() {
        try {
            recent = JSON.parse(localStorage.getItem('cmd_recent') || '[]');
        } catch (_) { recent = []; }
    }

    function saveRecent(page) {
        recent = recent.filter(function(r) { return r !== page; });
        recent.unshift(page);
        recent = recent.slice(0, 5);
        try { localStorage.setItem('cmd_recent', JSON.stringify(recent)); } catch (_) {}
    }

    function buildItems() {
        items = [];
        loadRecent();
        var navItems = getNavItems();
        var quickActions = getQuickActions();

        if (recent.length > 0) {
            items.push({ type: 'header', title: t('cmd.recent', 'Recent') });
            recent.forEach(function(page) {
                var found = navItems.find(function(n) { return n.page === page; });
                if (found) items.push(found);
            });
        }

        items.push({ type: 'header', title: t('cmd.quickActions', 'Quick Actions') });
        items = items.concat(quickActions);

        items.push({ type: 'header', title: t('cmd.pages', 'Pages') });
        items = items.concat(navItems);

        selectedIndex = 0;
        renderItems('');
    }

    function fuzzyMatch(query, text) {
        if (!query) return true;
        query = query.toLowerCase();
        text = text.toLowerCase();
        var qi = 0;
        for (var ti = 0; ti < text.length && qi < query.length; ti++) {
            if (text[ti] === query[qi]) qi++;
        }
        return qi === query.length;
    }

    function renderItems(query) {
        results.innerHTML = '';
        var matched = items.filter(function(item) {
            if (item.type === 'header') return true;
            return fuzzyMatch(query, item.title);
        });

        var visibleIndex = 0;
        matched.forEach(function(item) {
            if (item.type === 'header') {
                var header = document.createElement('div');
                header.className = 'cmd-header';
                header.textContent = item.title;
                results.appendChild(header);
                return;
            }
            var el = document.createElement('div');
            el.className = 'cmd-item';
            if (visibleIndex === selectedIndex) el.classList.add('selected');
            el.innerHTML = '<i class="bi ' + item.icon + ' cmd-item-icon"></i>' +
                '<span class="cmd-item-title">' + escapeHtml(item.title) + '</span>';
            el.addEventListener('click', function() { execute(item); });
            el.addEventListener('mouseenter', function() {
                selectedIndex = visibleIndex;
                updateSelection();
            });
            results.appendChild(el);
            visibleIndex++;
        });

        if (visibleIndex === 0) {
            results.innerHTML = '<div class="cmd-empty">' + t('cmd.noResults', 'No results') + '</div>';
        }
    }

    function updateSelection() {
        var els = results.querySelectorAll('.cmd-item');
        els.forEach(function(el, i) {
            el.classList.toggle('selected', i === selectedIndex);
        });
        if (els[selectedIndex]) {
            els[selectedIndex].scrollIntoView({ block: 'nearest' });
        }
    }

    function getVisibleItems() {
        return Array.from(results.querySelectorAll('.cmd-item'));
    }

    function execute(item) {
        if (!item || !item.href) return;
        saveRecent(item.page || item.title);
        window.location.href = item.href;
        close();
    }

    function escapeHtml(s) {
        return String(s || '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    }

    function open() {
        if (!palette) create();
        palette.classList.add('show');
        palette.style.display = 'flex';
        input.value = '';
        buildItems();
        setTimeout(function() { input.focus(); }, 50);
    }

    function close() {
        if (!palette) return;
        palette.classList.remove('show');
        palette.style.display = 'none';
    }

    function create() {
        palette = document.createElement('div');
        palette.className = 'cmd-palette-overlay';
        palette.style.display = 'none';
        palette.innerHTML =
            '<div class="cmd-palette">' +
                '<div class="cmd-input-wrap">' +
                    '<i class="bi bi-search cmd-search-icon"></i>' +
                    '<input type="text" class="cmd-input" placeholder="' + t('cmd.placeholder', 'Search pages, routes, clusters...') + '">' +
                    '<kbd class="cmd-esc">ESC</kbd>' +
                '</div>' +
                '<div class="cmd-results"></div>' +
            '</div>';
        document.body.appendChild(palette);

        input = palette.querySelector('.cmd-input');
        results = palette.querySelector('.cmd-results');

        input.addEventListener('input', function() {
            selectedIndex = 0;
            renderItems(input.value);
        });

        input.addEventListener('keydown', function(e) {
            var visible = getVisibleItems();
            if (e.key === 'ArrowDown') {
                e.preventDefault();
                selectedIndex = Math.min(selectedIndex + 1, visible.length - 1);
                updateSelection();
            } else if (e.key === 'ArrowUp') {
                e.preventDefault();
                selectedIndex = Math.max(selectedIndex - 1, 0);
                updateSelection();
            } else if (e.key === 'Enter') {
                e.preventDefault();
                var visibleItems = items.filter(function(item) {
                    if (item.type === 'header') return false;
                    return fuzzyMatch(input.value, item.title);
                });
                if (visibleItems[selectedIndex]) execute(visibleItems[selectedIndex]);
            } else if (e.key === 'Escape') {
                close();
            }
        });

        palette.addEventListener('click', function(e) {
            if (e.target === palette) close();
        });
    }

    // Global keyboard listener
    document.addEventListener('keydown', function(e) {
        if ((e.ctrlKey || e.metaKey) && e.key === 'k') {
            e.preventDefault();
            if (palette && palette.style.display !== 'none') close();
            else open();
        }
    });

    window.DashboardCommandPalette = { open: open, close: close };
})();
