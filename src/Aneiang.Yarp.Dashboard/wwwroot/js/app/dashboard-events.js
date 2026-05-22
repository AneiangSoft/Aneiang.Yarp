/**
 * Dashboard Events - Centralized event handling
 */
(function() {
    'use strict';

    window.DashboardEvents = window.DashboardEvents || {};

    // ===== Event Handlers Storage =====
    const handlers = {};

    // ===== Setup =====
    window.DashboardEvents.setup = function() {
        this.setupGlobalHandlers();
        this.setupKeyboardShortcuts();
        this.setupCustomEvents();
        
        console.log('[Events] Setup complete');
    };

    // ===== Global Event Handlers =====
    window.DashboardEvents.setupGlobalHandlers = function() {
        // Prevent accidental navigation with unsaved changes
        window.addEventListener('beforeunload', function(e) {
            const hasDraft = window.DashboardStorage?.getDraft('cluster') || 
                            window.DashboardStorage?.getDraft('route');
            
            if (hasDraft) {
                e.preventDefault();
                e.returnValue = '';
            }
        });

        // Handle online/offline
        window.addEventListener('online', function() {
            console.log('[Events] Connection restored');
            document.dispatchEvent(new CustomEvent('dashboard:online'));
        });

        window.addEventListener('offline', function() {
            console.warn('[Events] Connection lost');
            document.dispatchEvent(new CustomEvent('dashboard:offline'));
        });
    };

    // ===== Keyboard Shortcuts =====
    window.DashboardEvents.setupKeyboardShortcuts = function() {
        document.addEventListener('keydown', function(e) {
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
        });
    };

    // ===== Custom Events =====
    window.DashboardEvents.setupCustomEvents = function() {
        // Listen for locale changes
        document.addEventListener('dashboard:localeChange', function(e) {
            console.log('[Events] Locale changed:', e.detail.locale);
            // Re-render all visible content
            if (window.DashboardState) {
                window.DashboardState.saveState();
            }
        });

        // Listen for data changes
        document.addEventListener('dashboard:dataChanged', function(e) {
            console.log('[Events] Data changed:', e.detail.type);
            if (window.DashboardState) {
                window.DashboardState.saveState();
            }
        });

        // Load module data on tab switch
        document.addEventListener('dashboard:tabChanged', function(e) {
            var tab = e.detail.tab;
            if (tab === 'overview' && window.StatsModule) {
                window.StatsModule.loadStats();
            } else if (tab === 'history' && window.HistoryModule) {
                window.HistoryModule.loadHistory();
            }
        });
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
        Object.keys(handlers).forEach(event => {
            handlers[event].forEach(h => {
                h.target.removeEventListener(event, h.handler);
            });
        });
        
        Object.keys(handlers).forEach(key => delete handlers[key]);
        
        console.log('[Events] Cleanup complete');
    };

})();
