/**
 * Dashboard i18n Helper - Internationalization utilities
 */
(function() {
    'use strict';

    window.DashboardI18n = window.DashboardI18n || {};

    // ===== i18n Storage =====
    let currentLocale = 'zh-CN';
    let translations = {};

    // ===== Initialization =====
    window.DashboardI18n.init = function() {
        // Get locale from dashboard config or localStorage
        const dashboard = window.__dashboard;
        if (dashboard && dashboard.CURRENT_LOCALE) {
            currentLocale = dashboard.CURRENT_LOCALE;
        } else {
            currentLocale = localStorage.getItem('dashboard_locale') || 'zh-CN';
        }

        // Load translations
        if (dashboard && dashboard.I18N) {
            translations = dashboard.I18N;
        }

        console.log('[i18n] Initialized with locale:', currentLocale);
    };

    // ===== Translate Function =====
    window.__ = function(key, params) {
        // Get translation
        let text = translations[key];
        
        // Fallback to key if not found
        if (!text) {
            console.warn('[i18n] Missing translation:', key);
            text = key;
        }

        // Replace parameters
        if (params) {
            Object.keys(params).forEach(param => {
                text = text.replace(`{${param}}`, params[param]);
            });
        }

        return text;
    };

    // ===== Locale Management =====
    window.DashboardI18n.setLocale = function(locale) {
        currentLocale = locale;
        localStorage.setItem('dashboard_locale', locale);
        
        // Update dashboard config
        if (window.__dashboard) {
            window.__dashboard.CURRENT_LOCALE = locale;
        }

        // Trigger re-render
        document.dispatchEvent(new CustomEvent('dashboard:localeChange', {
            detail: { locale }
        }));

        console.log('[i18n] Locale changed to:', locale);
    };

    window.DashboardI18n.getLocale = function() {
        return currentLocale;
    };

    window.DashboardI18n.toggleLocale = function() {
        const newLocale = currentLocale === 'zh-CN' ? 'en-US' : 'zh-CN';
        this.setLocale(newLocale);
        return newLocale;
    };

    // ===== Date/Time Formatting =====
    window.DashboardI18n.formatDate = function(date, options = {}) {
        const d = typeof date === 'string' ? new Date(date) : date;
        const defaultOptions = {
            year: 'numeric',
            month: '2-digit',
            day: '2-digit',
            hour12: false
        };
        
        return d.toLocaleString(currentLocale, { ...defaultOptions, ...options });
    };

    window.DashboardI18n.formatTime = function(date, options = {}) {
        const d = typeof date === 'string' ? new Date(date) : date;
        const defaultOptions = {
            hour: '2-digit',
            minute: '2-digit',
            second: '2-digit',
            hour12: false
        };
        
        return d.toLocaleString(currentLocale, { ...defaultOptions, ...options });
    };

    // ===== Number Formatting =====
    window.DashboardI18n.formatNumber = function(number, options = {}) {
        const defaultOptions = {
            minimumFractionDigits: 0,
            maximumFractionDigits: 2
        };
        
        return new Intl.NumberFormat(currentLocale, { ...defaultOptions, ...options }).format(number);
    };

    window.DashboardI18n.formatBytes = function(bytes, decimals = 2) {
        if (bytes === 0 || bytes === null) return '0 B';
        
        const k = 1024;
        const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
        const i = Math.floor(Math.log(bytes) / Math.log(k));
        
        return this.formatNumber(Math.round(bytes / Math.pow(k, i) * Math.pow(10, decimals)) / Math.pow(10, decimals)) + ' ' + sizes[i];
    };

    // ===== Text Direction =====
    window.DashboardI18n.isRTL = function() {
        // Currently only support LTR languages
        return false;
    };

})();
