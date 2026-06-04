/**
 * Dashboard Utilities - Common helper functions
 */
(function() {
    'use strict';

    window.DashboardUtils = window.DashboardUtils || {};

    // ===== Initialization =====
    window.DashboardUtils.init = function() {
        console.log('[Utils] Initialized');
    };

    // ===== DOM Utilities =====
    window.DashboardUtils.$ = function(selector, context) {
        context = context || document;
        return context.querySelector(selector);
    };

    window.DashboardUtils.$$ = function(selector, context) {
        context = context || document;
        return Array.from(context.querySelectorAll(selector));
    };

    window.DashboardUtils.createElement = function(tag, className, attributes) {
        const el = document.createElement(tag);
        if (className) {
            if (Array.isArray(className)) {
                el.classList.add(...className);
            } else {
                el.className = className;
            }
        }
        if (attributes) {
            Object.keys(attributes).forEach(function(key) {
                if (key === 'textContent') {
                    el.textContent = attributes[key];
                } else if (key === 'innerHTML') {
                    el.innerHTML = attributes[key];
                } else if (key.startsWith('on')) {
                    el.addEventListener(key.substring(2).toLowerCase(), attributes[key]);
                } else {
                    el.setAttribute(key, attributes[key]);
                }
            });
        }
        return el;
    };

    // ===== Safe Access =====
    window.DashboardUtils.safeGet = function(obj, path, defaultValue) {
        if (!obj || !path) return defaultValue;
        
        const keys = path.split('.');
        let result = obj;
        
        for (const key of keys) {
            if (result === null || result === undefined) {
                return defaultValue;
            }
            result = result[key];
        }
        
        return result !== undefined ? result : defaultValue;
    };

    // ===== String Utilities =====
    window.DashboardUtils.escapeHtml = function(text) {
        if (!text) return '';
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    };

    window.DashboardUtils.truncate = function(text, maxLength, suffix) {
        maxLength = maxLength || 100;
        suffix = suffix !== undefined ? suffix : '...';
        
        if (!text || text.length <= maxLength) return text;
        return text.substring(0, maxLength) + suffix;
    };

    window.DashboardUtils.formatBytes = function(bytes, decimals) {
        if (bytes === 0 || bytes === null) return '0 B';
        
        const k = 1024;
        const dm = decimals || 2;
        const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
        const i = Math.floor(Math.log(bytes) / Math.log(k));
        
        return parseFloat((bytes / Math.pow(k, i)).toFixed(dm)) + ' ' + sizes[i];
    };

    // ===== Time Utilities =====
    window.DashboardUtils.formatTime = function(date, locale) {
        locale = locale || window.__dashboard?.CURRENT_LOCALE || 'zh-CN';
        const d = typeof date === 'string' ? new Date(date) : date;
        return d.toLocaleTimeString(locale, { hour12: false });
    };

    window.DashboardUtils.formatDateTime = function(date, locale) {
        locale = locale || window.__dashboard?.CURRENT_LOCALE || 'zh-CN';
        const d = typeof date === 'string' ? new Date(date) : date;
        return d.toLocaleString(locale, { hour12: false });
    };

    window.DashboardUtils.timeAgo = function(date) {
        const now = new Date();
        const d = typeof date === 'string' ? new Date(date) : date;
        const seconds = Math.floor((now - d) / 1000);

        if (seconds < 60) return '刚刚';
        if (seconds < 3600) return Math.floor(seconds / 60) + ' 分钟前';
        if (seconds < 86400) return Math.floor(seconds / 3600) + ' 小时前';
        return Math.floor(seconds / 86400) + ' 天前';
    };

    // ===== JSON Utilities =====
    window.DashboardUtils.safeJsonParse = function(text, defaultValue) {
        try {
            return JSON.parse(text);
        } catch (e) {
            return defaultValue !== undefined ? defaultValue : null;
        }
    };

    window.DashboardUtils.safeJsonStringify = function(obj, space, defaultValue) {
        try {
            return JSON.stringify(obj, null, space || 2);
        } catch (e) {
            return defaultValue !== undefined ? defaultValue : '';
        }
    };

    // ===== Validation =====
    window.DashboardUtils.isValidUrl = function(string) {
        try {
            new URL(string);
            return true;
        } catch (_) {
            return false;
        }
    };

    window.DashboardUtils.isValidJson = function(text) {
        try {
            JSON.parse(text);
            return true;
        } catch (e) {
            return false;
        }
    };

    // ===== Array Utilities =====
    window.DashboardUtils.unique = function(array, key) {
        if (!key) return [...new Set(array)];
        
        const seen = new Set();
        return array.filter(item => {
            const value = item[key];
            if (seen.has(value)) return false;
            seen.add(value);
            return true;
        });
    };

    window.DashboardUtils.groupBy = function(array, key) {
        return array.reduce((groups, item) => {
            const group = item[key];
            groups[group] = groups[group] || [];
            groups[group].push(item);
            return groups;
        }, {});
    };

    // ===== Debounce & Throttle =====
    window.DashboardUtils.debounce = function(func, wait) {
        let timeout;
        return function executedFunction(...args) {
            const later = () => {
                clearTimeout(timeout);
                func(...args);
            };
            clearTimeout(timeout);
            timeout = setTimeout(later, wait);
        };
    };

    window.DashboardUtils.throttle = function(func, limit) {
        let inThrottle;
        return function(...args) {
            if (!inThrottle) {
                func.apply(this, args);
                inThrottle = true;
                setTimeout(() => inThrottle = false, limit);
            }
        };
    };

    // ===== Clipboard =====
    window.DashboardUtils.copyToClipboard = async function(text) {
        try {
            await navigator.clipboard.writeText(text);
            return true;
        } catch (err) {
            // Fallback for older browsers
            const textarea = document.createElement('textarea');
            textarea.value = text;
            textarea.style.position = 'fixed';
            textarea.style.opacity = '0';
            document.body.appendChild(textarea);
            textarea.select();
            try {
                document.execCommand('copy');
                document.body.removeChild(textarea);
                return true;
            } catch (e) {
                document.body.removeChild(textarea);
                return false;
            }
        }
    };

    // ===== Render JSON Block (shared across modules) =====
    window.DashboardUtils.renderJsonBlock = function(obj, title) {
        const json = JSON.stringify(obj, null, 2);
        return `<details style="margin:4px 0 0;">
            <summary style="cursor:pointer;color:#0ea5e9;font-weight:500;">${title}</summary>
            <pre style="background:#f1f5f9;border:1px solid #cbd5e1;border-radius:4px;padding:8px;margin:4px 0 0;overflow-x:auto;white-space:pre-wrap;word-break:break-all;font-size:12px;color:#334155;line-height:1.6;">${window.DashboardUtils.escapeHtml(json)}</pre>
        </details>`;
    };

    // ===== Create Source Badge HTML (shared across modules) =====
    window.DashboardUtils.createSourceBadge = function(source) {
        const sourceMap = {
            'config': { css: 'bg-secondary', icon: 'bi-file-earmark-code', label: __('index.source.config') },
            'dynamic': { css: 'bg-success', icon: 'bi-lightning-charge-fill', label: __('index.source.dynamic') },
            'dashboard': { css: 'bg-primary', icon: 'bi-speedometer2', label: __('index.source.dashboard') },
            'auto-register': { css: 'bg-info', icon: 'bi-cloud-arrow-up-fill', label: __('index.source.autoRegister') }
        };
        const s = source || 'config';
        const cfg = sourceMap[s] || { css: 'bg-secondary', icon: 'bi-question-circle-fill', label: s };
        return `<span class="badge ${cfg.css}" style="font-size:12px;display:inline-flex;align-items:center;gap:4px;"><i class="bi ${cfg.icon}"></i>${cfg.label}</span>`;
    };

    // ===== LocalStorage =====
    window.DashboardUtils.storage = {
        get: function(key, defaultValue) {
            try {
                const item = localStorage.getItem(key);
                return item ? JSON.parse(item) : defaultValue;
            } catch (e) {
                return defaultValue;
            }
        },
        set: function(key, value) {
            try {
                localStorage.setItem(key, JSON.stringify(value));
                return true;
            } catch (e) {
                console.error('[Utils] Storage set failed:', e);
                return false;
            }
        },
        remove: function(key) {
            try {
                localStorage.removeItem(key);
                return true;
            } catch (e) {
                return false;
            }
        }
    };

    // ===== Performance Utilities =====
    
    /**
     * Diff render - only update changed rows
     * @param {HTMLElement} container - Container element
     * @param {Array} items - New data items
     * @param {string} keyProp - Property name for unique key
     * @param {Function} createFn - Function to create new element (item) => HTMLElement
     * @param {Function} updateFn - Optional function to update existing element (el, item) => void
     */
    window.DashboardUtils.diffRender = function(container, items, keyProp, createFn, updateFn) {
        if (!container) return;
        
        const startTime = performance.now();
        
        // Get existing rows
        const existingRows = new Map();
        container.querySelectorAll('[data-key]').forEach(row => {
            existingRows.set(row.dataset.key, row);
        });
        
        // Track new keys
        const newKeys = new Set(items.map(item => String(item[keyProp])));
        
        // Remove rows that no longer exist
        existingRows.forEach((row, key) => {
            if (!newKeys.has(key)) {
                row.remove();
            }
        });
        
        // Update or create rows
        const fragment = document.createDocumentFragment();
        const movedRows = [];
        
        items.forEach((item, index) => {
            const key = String(item[keyProp]);
            const existingRow = existingRows.get(key);
            
            if (existingRow) {
                // Update existing row
                if (updateFn) {
                    updateFn(existingRow, item);
                }
                // Store for reordering
                movedRows.push(existingRow);
            } else {
                // Create new row
                const newRow = createFn(item);
                if (newRow) {
                    newRow.dataset.key = key;
                    fragment.appendChild(newRow);
                }
            }
        });
        
        // Append new rows
        if (fragment.childNodes.length > 0) {
            container.appendChild(fragment);
        }
        
        // Reorder rows to match new order (using efficient reordering)
        const currentRows = Array.from(container.querySelectorAll('[data-key]'));
        if (currentRows.length === items.length) {
            items.forEach((item, index) => {
                const key = String(item[keyProp]);
                const row = existingRows.get(key) || container.querySelector(`[data-key="${key}"]`);
                if (row && container.children[index] !== row) {
                    container.insertBefore(row, container.children[index] || null);
                }
            });
        }
        
        const endTime = performance.now();
        console.log(`[DiffRender] Updated ${items.length} items in ${(endTime - startTime).toFixed(2)}ms`);
    };

    /**
     * Batch render using requestAnimationFrame
     * @param {Array} items - Items to render
     * @param {Function} renderFn - Function to render each batch
     * @param {number} batchSize - Items per batch (default: 50)
     */
    window.DashboardUtils.batchRender = async function(items, renderFn, batchSize) {
        batchSize = batchSize || 50;
        const total = items.length;
        let index = 0;
        
        return new Promise((resolve) => {
            const renderBatch = () => {
                const start = index;
                const end = Math.min(index + batchSize, total);
                
                for (; index < end; index++) {
                    renderFn(items[index], index);
                }
                
                if (index < total) {
                    requestAnimationFrame(renderBatch);
                } else {
                    resolve();
                }
            };
            
            renderBatch();
        });
    };

    /**
     * Create element with children efficiently
     * @param {string} tag - Tag name
     * @param {Object} options - Options
     * @param {Array} children - Child elements or strings
     */
    window.DashboardUtils.createEl = function(tag, options, children) {
        const el = document.createElement(tag);
        
        if (options) {
            if (options.className) el.className = options.className;
            if (options.style) Object.assign(el.style, options.style);
            if (options.dataset) Object.assign(el.dataset, options.dataset);
            if (options.attrs) {
                Object.keys(options.attrs).forEach(key => {
                    el.setAttribute(key, options.attrs[key]);
                });
            }
            if (options.text) el.textContent = options.text;
            if (options.html) el.innerHTML = options.html;
            if (options.on) {
                Object.keys(options.on).forEach(event => {
                    el.addEventListener(event, options.on[event]);
                });
            }
        }
        
        if (children) {
            children.forEach(child => {
                if (typeof child === 'string') {
                    el.appendChild(document.createTextNode(child));
                } else if (child instanceof Node) {
                    el.appendChild(child);
                }
            });
        }
        
        return el;
    };

    /**
     * Event delegation helper
     * @param {HTMLElement} container - Container element
     * @param {string} selector - CSS selector for target elements
     * @param {string} event - Event name
     * @param {Function} handler - Event handler
     */
    window.DashboardUtils.delegate = function(container, selector, event, handler) {
        container.addEventListener(event, function(e) {
            const target = e.target.closest(selector);
            if (target && container.contains(target)) {
                handler.call(target, e, target);
            }
        });
    };

})();
