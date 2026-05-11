/**
 * Dashboard Storage - LocalStorage wrapper with expiration
 */
(function() {
    'use strict';

    window.DashboardStorage = window.DashboardStorage || {};

    // ===== Configuration =====
    const config = {
        prefix: 'dashboard_',
        defaultExpiration: 24 * 60 * 60 * 1000 // 24 hours
    };

    // ===== Initialization =====
    window.DashboardStorage.init = function() {
        console.log('[Storage] Initialized');
    };

    // ===== Core Storage Methods =====
    window.DashboardStorage.set = function(key, value, expiration) {
        try {
            const item = {
                value: value,
                timestamp: Date.now(),
                expiration: expiration || config.defaultExpiration
            };
            
            localStorage.setItem(config.prefix + key, JSON.stringify(item));
            return true;
        } catch (error) {
            console.error('[Storage] Set failed:', error);
            return false;
        }
    };

    window.DashboardStorage.get = function(key, defaultValue) {
        try {
            const itemStr = localStorage.getItem(config.prefix + key);
            if (!itemStr) return defaultValue;

            const item = JSON.parse(itemStr);
            const now = Date.now();

            // Check expiration
            if (now - item.timestamp > item.expiration) {
                this.remove(key);
                return defaultValue;
            }

            return item.value;
        } catch (error) {
            console.error('[Storage] Get failed:', error);
            return defaultValue;
        }
    };

    window.DashboardStorage.remove = function(key) {
        try {
            localStorage.removeItem(config.prefix + key);
            return true;
        } catch (error) {
            console.error('[Storage] Remove failed:', error);
            return false;
        }
    };

    window.DashboardStorage.clear = function() {
        try {
            const keys = Object.keys(localStorage).filter(key => 
                key.startsWith(config.prefix)
            );
            
            keys.forEach(key => {
                localStorage.removeItem(key);
            });
            
            return true;
        } catch (error) {
            console.error('[Storage] Clear failed:', error);
            return false;
        }
    };

    // ===== Specific Storage Keys =====
    window.DashboardStorage.getToken = function() {
        return this.get('token', null);
    };

    window.DashboardStorage.setToken = function(token) {
        // Tokens don't expire automatically
        return this.set('token', token, 365 * 24 * 60 * 60 * 1000); // 1 year
    };

    window.DashboardStorage.removeToken = function() {
        return this.remove('token');
    };

    window.DashboardStorage.getLocale = function() {
        return this.get('locale', 'zh-CN');
    };

    window.DashboardStorage.setLocale = function(locale) {
        return this.set('locale', locale, 365 * 24 * 60 * 60 * 1000);
    };

    window.DashboardStorage.getFilters = function(page) {
        return this.get(`filters_${page}`, {});
    };

    window.DashboardStorage.setFilters = function(page, filters) {
        return this.set(`filters_${page}`, filters);
    };

    window.DashboardStorage.getExpandedItems = function(page) {
        return this.get(`expanded_${page}`, []);
    };

    window.DashboardStorage.setExpandedItems = function(page, items) {
        return this.set(`expanded_${page}`, items);
    };

    window.DashboardStorage.getDraft = function(type) {
        return this.get(`draft_${type}`, null);
    };

    window.DashboardStorage.setDraft = function(type, draft) {
        // Drafts expire in 1 hour
        return this.set(`draft_${type}`, draft, 60 * 60 * 1000);
    };

    window.DashboardStorage.removeDraft = function(type) {
        return this.remove(`draft_${type}`);
    };

    // ===== Session Storage (cleared on tab close) =====
    window.DashboardStorage.sessionSet = function(key, value) {
        try {
            sessionStorage.setItem(config.prefix + key, JSON.stringify(value));
            return true;
        } catch (error) {
            console.error('[Storage] Session set failed:', error);
            return false;
        }
    };

    window.DashboardStorage.sessionGet = function(key, defaultValue) {
        try {
            const itemStr = sessionStorage.getItem(config.prefix + key);
            if (!itemStr) return defaultValue;
            return JSON.parse(itemStr);
        } catch (error) {
            console.error('[Storage] Session get failed:', error);
            return defaultValue;
        }
    };

    window.DashboardStorage.sessionRemove = function(key) {
        try {
            sessionStorage.removeItem(config.prefix + key);
            return true;
        } catch (error) {
            console.error('[Storage] Session remove failed:', error);
            return false;
        }
    };

    // ===== Storage Info =====
    window.DashboardStorage.getInfo = function() {
        try {
            let totalSize = 0;
            let itemCount = 0;
            
            for (let key in localStorage) {
                if (key.startsWith(config.prefix)) {
                    totalSize += localStorage[key].length;
                    itemCount++;
                }
            }
            
            return {
                totalSize: totalSize,
                itemCount: itemCount,
                usagePercent: (totalSize / (5 * 1024 * 1024) * 100).toFixed(2) // 5MB limit
            };
        } catch (error) {
            console.error('[Storage] Get info failed:', error);
            return { totalSize: 0, itemCount: 0, usagePercent: 0 };
        }
    };

})();
