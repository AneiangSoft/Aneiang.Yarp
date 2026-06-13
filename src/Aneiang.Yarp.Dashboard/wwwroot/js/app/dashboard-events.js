/**
 * Dashboard Events - Centralized event handling
 */
(function() {
    'use strict';

    window.DashboardEvents = window.DashboardEvents || {};

    // ===== Event Handlers Storage =====
    const handlers = {};

    // ===== Named Global Handlers for Cleanup =====
    const _globalHandlers = {
        beforeunload: null,
        online: null,
        offline: null,
        keydown: null,
        localeChange: null,
        dataChanged: null
    };

    // ===== Setup =====
    window.DashboardEvents.setup = function() {
        // Only setup once
        if (_globalHandlers.beforeunload) return;

        this.setupGlobalHandlers();
        this.setupKeyboardShortcuts();
        this.setupCustomEvents();

        console.log('[Events] Setup complete');
    };

    // ===== Global Event Handlers =====
    window.DashboardEvents.setupGlobalHandlers = function() {
        // Prevent accidental navigation with unsaved changes
        _globalHandlers.beforeunload = function(e) {
            const hasDraft = window.DashboardStorage?.getDraft('cluster') ||
                            window.DashboardStorage?.getDraft('route');

            if (hasDraft) {
                e.preventDefault();
                e.returnValue = '';
            }
        };
        window.addEventListener('beforeunload', _globalHandlers.beforeunload);

        // Handle online/offline
        _globalHandlers.online = function() {
            console.log('[Events] Connection restored');
            document.dispatchEvent(new CustomEvent('dashboard:online'));
        };
        window.addEventListener('online', _globalHandlers.online);

        _globalHandlers.offline = function() {
            console.warn('[Events] Connection lost');
            document.dispatchEvent(new CustomEvent('dashboard:offline'));
        };
        window.addEventListener('offline', _globalHandlers.offline);
    };

    // ===== Keyboard Shortcuts =====
    window.DashboardEvents.setupKeyboardShortcuts = function() {
        _globalHandlers.keydown = function(e) {
            // Skip shortcuts when typing in input/textarea/select or contentEditable
            const tag = e.target.tagName.toLowerCase();
            const isEditable = tag === 'input' || tag === 'textarea' || tag === 'select' || e.target.isContentEditable;

            // Ctrl/Cmd + S: Save draft (always active, even in editors)
            if ((e.ctrlKey || e.metaKey) && e.key === 's') {
                e.preventDefault();
                document.dispatchEvent(new CustomEvent('dashboard:shortcut:save'));
                return;
            }

            // Ctrl/Cmd + F: Focus search (only when not in an input field)
            if ((e.ctrlKey || e.metaKey) && e.key === 'f' && !isEditable) {
                e.preventDefault();
                document.dispatchEvent(new CustomEvent('dashboard:shortcut:search'));
                return;
            }

            // Ctrl/Cmd + Shift + R: Refresh (Shift required to avoid browser refresh conflict)
            if ((e.ctrlKey || e.metaKey) && e.shiftKey && e.key === 'R') {
                e.preventDefault();
                document.dispatchEvent(new CustomEvent('dashboard:shortcut:refresh'));
                return;
            }

            // Escape: Close modal (only when not in an input that might need Escape)
            if (e.key === 'Escape') {
                document.dispatchEvent(new CustomEvent('dashboard:shortcut:close'));
                return;
            }

            // Ctrl/Cmd + Z: Undo (only in non-editable contexts to avoid interfering with editors)
            if ((e.ctrlKey || e.metaKey) && e.key === 'z' && !e.shiftKey && !isEditable) {
                document.dispatchEvent(new CustomEvent('dashboard:shortcut:undo'));
                return;
            }

            // Ctrl/Cmd + Shift + Z: Redo
            if ((e.ctrlKey || e.metaKey) && e.key === 'z' && e.shiftKey && !isEditable) {
                document.dispatchEvent(new CustomEvent('dashboard:shortcut:redo'));
                return;
            }
        };
        document.addEventListener('keydown', _globalHandlers.keydown);
    };

    // ===== Custom Events =====
    window.DashboardEvents.setupCustomEvents = function() {
        _globalHandlers.localeChange = function(e) {
            console.log('[Events] Locale changed:', e.detail.locale);
            if (window.DashboardState) {
                window.DashboardState.saveState();
            }
        };
        document.addEventListener('dashboard:localeChange', _globalHandlers.localeChange);

        _globalHandlers.dataChanged = function(e) {
            console.log('[Events] Data changed:', e.detail.type);
            if (window.DashboardState) {
                window.DashboardState.saveState();
            }
        };
        document.addEventListener('dashboard:dataChanged', _globalHandlers.dataChanged);
    };

    // ===== Event Registration =====
    window.DashboardEvents.on = function(event, handler, options = {}) {
        const {
            once = false,
            debounce = 0,
            target = document
        } = options;

        let wrappedHandler = handler;

        // Add debounce if needed
        if (debounce > 0) {
            wrappedHandler = this.debounce(handler, debounce);
        }

        // Add once behavior
        if (once) {
            const originalHandler = wrappedHandler;
            wrappedHandler = function(...args) {
                target.removeEventListener(event, wrappedHandler);
                originalHandler.apply(this, args);
            };
        }

        // Store handler for cleanup
        if (!handlers[event]) {
            handlers[event] = [];
        }
        handlers[event].push({ handler: wrappedHandler, target, options });

        target.addEventListener(event, wrappedHandler);

        // Return unsubscribe function
        return () => {
            target.removeEventListener(event, wrappedHandler);
            handlers[event] = handlers[event].filter(h => h.handler !== wrappedHandler);
        };
    };

    // ===== Event Removal =====
    window.DashboardEvents.off = function(event, handler, target = document) {
        if (handler) {
            target.removeEventListener(event, handler);
        } else if (handlers[event]) {
            handlers[event].forEach(h => {
                h.target.removeEventListener(event, h.handler);
            });
            delete handlers[event];
        }
    };

    // ===== Event Emission =====
    window.DashboardEvents.emit = function(event, detail, target = document) {
        const customEvent = new CustomEvent(event, {
            detail: detail,
            bubbles: true,
            cancelable: true
        });
        target.dispatchEvent(customEvent);
    };

    // ===== Debounce Utility =====
    window.DashboardEvents.debounce = function(func, wait) {
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

    // ===== Cleanup =====
    window.DashboardEvents.cleanup = function() {
        // Remove named global handlers
        if (_globalHandlers.beforeunload) {
            window.removeEventListener('beforeunload', _globalHandlers.beforeunload);
            _globalHandlers.beforeunload = null;
        }
        if (_globalHandlers.online) {
            window.removeEventListener('online', _globalHandlers.online);
            _globalHandlers.online = null;
        }
        if (_globalHandlers.offline) {
            window.removeEventListener('offline', _globalHandlers.offline);
            _globalHandlers.offline = null;
        }
        if (_globalHandlers.keydown) {
            document.removeEventListener('keydown', _globalHandlers.keydown);
            _globalHandlers.keydown = null;
        }
        if (_globalHandlers.localeChange) {
            document.removeEventListener('dashboard:localeChange', _globalHandlers.localeChange);
            _globalHandlers.localeChange = null;
        }
        if (_globalHandlers.dataChanged) {
            document.removeEventListener('dashboard:dataChanged', _globalHandlers.dataChanged);
            _globalHandlers.dataChanged = null;
        }

        // Remove dynamically registered handlers
        Object.keys(handlers).forEach(event => {
            handlers[event].forEach(h => {
                h.target.removeEventListener(event, h.handler);
            });
        });
        Object.keys(handlers).forEach(key => delete handlers[key]);

        console.log('[Events] Cleanup complete');
    };

})();
