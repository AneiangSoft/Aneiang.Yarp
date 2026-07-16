// dashboard-layout.js - Global layout functions extracted from _DashboardLayout.cshtml
// Depends on: window.__dashboard (set inline in layout), DashboardModals (loaded via dashboard-modals.js)

(function() {
    'use strict';

    var routePrefix = (window.__dashboard && window.__dashboard.routePrefix) || 'apigateway';
    var I18N = (window.__dashboard && window.__dashboard.I18N) || {};

    function __(key) { return I18N[key] || key; }

    window.switchLocale = function() {
        var current = (window.__dashboard && window.__dashboard.locale) || 'zh-CN';
        var next = current === 'en-US' ? 'zh-CN' : 'en-US';
        document.cookie = 'dashboard_locale=' + next + ';path=/;max-age=' + (365 * 86400);
        localStorage.setItem('dashboard_locale', next);
        location.reload();
    };

    window.dashboardLogout = function() {
        if (window.DashboardModals) {
            DashboardModals.showConfirm(__('layout.user.logoutConfirm'), function() {
                fetch('/' + routePrefix + '/logout', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' }
                }).then(function(res) { return res.json(); }).then(function(data) {
                    if (data.code === 200) {
                        localStorage.removeItem('dashboard_token');
                        window.location.href = '/' + routePrefix + '/login';
                    }
                }).catch(function(err) {
                    console.error('Logout failed:', err);
                    localStorage.removeItem('dashboard_token');
                    window.location.href = '/' + routePrefix + '/login';
                });
            });
        } else {
            localStorage.removeItem('dashboard_token');
            window.location.href = '/' + routePrefix + '/login';
        }
    };

    window.getJwtUsername = function() {
        var token = localStorage.getItem('dashboard_token');
        if (!token) return null;
        try {
            var payload = JSON.parse(atob(token.split('.')[1]));
            return payload.username || payload.sub || null;
        } catch (e) { return null; }
    };

    window.toggleMobileSidebar = function(forceState) {
        var sidebar = document.getElementById('sidebar');
        var overlay = document.getElementById('sidebar-overlay');
        var btn = document.getElementById('hamburger-btn');
        if (!sidebar) return;
        var isOpen = sidebar.classList.contains('open');
        var newState = typeof forceState === 'boolean' ? forceState : !isOpen;
        if (newState) {
            sidebar.classList.add('open');
            overlay && overlay.classList.add('active');
            btn && btn.classList.add('active');
        } else {
            sidebar.classList.remove('open');
            overlay && overlay.classList.remove('active');
            btn && btn.classList.remove('active');
        }
    };

    window.showToast = function(message, type, duration) {
        type = type || 'info';
        duration = duration || 4000;
        var icons = { success: 'bi-check-circle-fill', error: 'bi-x-circle-fill', warning: 'bi-exclamation-triangle-fill', info: 'bi-info-circle-fill' };
        var container = document.getElementById('toast-container');
        if (!container) return;
        var el = document.createElement('div');
        el.className = 'toast-item ' + type;
        el.innerHTML = '<i class="bi ' + (icons[type] || icons.info) + '"></i>' +
            '<span class="toast-msg">' + message + '</span>' +
            '<button class="toast-close" onclick="hideToast(this)"><i class="bi bi-x"></i></button>';
        container.appendChild(el);
        setTimeout(function() { hideToast(el.querySelector('.toast-close')); }, duration);
    };

    window.hideToast = function(closeBtn) {
        if (!closeBtn) return;
        var item = closeBtn.closest('.toast-item');
        if (!item || item.classList.contains('hiding')) return;
        item.classList.add('hiding');
        setTimeout(function() { item.remove(); }, 300);
    };

    window.animateValue = function(el, start, end, duration, suffix) {
        suffix = suffix || '';
        if (start === end) { el.textContent = end + suffix; return; }
        var range = end - start;
        var startTime = null;
        function step(timestamp) {
            if (!startTime) startTime = timestamp;
            var progress = Math.min((timestamp - startTime) / duration, 1);
            var eased = 1 - Math.pow(1 - progress, 3);
            var current = Math.round(start + range * eased);
            el.textContent = current + suffix;
            if (progress < 1) requestAnimationFrame(step);
        }
        requestAnimationFrame(step);
    };

    document.addEventListener('DOMContentLoaded', function() {
        // i18n
        document.querySelectorAll('[data-i18n]').forEach(function(el) {
            var key = el.getAttribute('data-i18n');
            if (key && I18N[key]) el.textContent = I18N[key];
        });
        document.querySelectorAll('[data-i18n-placeholder]').forEach(function(el) {
            var key = el.getAttribute('data-i18n-placeholder');
            if (key && I18N[key]) el.placeholder = I18N[key];
        });
        document.querySelectorAll('[data-i18n-title]').forEach(function(el) {
            var key = el.getAttribute('data-i18n-title');
            if (key && I18N[key]) el.title = I18N[key];
        });

        // Show username in sidebar
        var username = getJwtUsername();
        var usernameEl = document.getElementById('sidebar-username');
        if (usernameEl && username) usernameEl.textContent = username;

        // Initialize group collapse/expand toggles
        document.querySelectorAll('[data-toggle="collapse"]').forEach(function(header) {
            header.addEventListener('click', function(e) {
                e.preventDefault();
                e.stopPropagation();
                var collapse = this.nextElementSibling;
                if (!collapse || !collapse.classList.contains('nav-collapse')) return;
                var isExpanded = this.classList.contains('expanded');
                if (isExpanded) {
                    this.classList.remove('expanded');
                    collapse.classList.remove('expanded');
                } else {
                    this.classList.add('expanded');
                    collapse.classList.add('expanded');
                }
            });
        });

        // Close mobile sidebar on nav link click
        document.querySelectorAll('#sidebar .nav-link[data-page]').forEach(function(link) {
            link.addEventListener('click', function() {
                if (window.innerWidth <= 768) toggleMobileSidebar(false);
            });
        });
    });
})();
