/**
 * Dashboard DOM Helpers - Safe DOM manipulation utilities
 */
(function() {
    'use strict';

    window.DashboardDOM = window.DashboardDOM || {};

    // ===== Safe Element Selection =====
    window.DashboardDOM.safe = function(selector, context) {
        context = context || document;
        const element = context.querySelector(selector);
        if (!element) {
            console.warn('[DOM] Element not found:', selector);
        }
        return element;
    };

    window.DashboardDOM.safeAll = function(selector, context) {
        context = context || document;
        return Array.from(context.querySelectorAll(selector));
    };

    // ===== Element Creation =====
    window.DashboardDOM.create = function(tag, options = {}) {
        const {
            className = '',
            id = '',
            attributes = {},
            events = {},
            style = {},
            textContent = '',
            innerHTML = '',
            children = []
        } = options;

        const element = document.createElement(tag);

        if (id) element.id = id;
        if (className) {
            if (Array.isArray(className)) {
                element.classList.add(...className);
            } else {
                element.className = className;
            }
        }
        if (textContent) element.textContent = textContent;
        if (innerHTML) element.innerHTML = innerHTML;

        // Set attributes
        Object.keys(attributes).forEach(key => {
            element.setAttribute(key, attributes[key]);
        });

        // Set inline styles
        Object.keys(style).forEach(key => {
            element.style[key] = style[key];
        });

        // Add event listeners
        Object.keys(events).forEach(event => {
            element.addEventListener(event, events[event]);
        });

        // Append children
        children.forEach(child => {
            if (child) {
                element.appendChild(typeof child === 'string' ? document.createTextNode(child) : child);
            }
        });

        return element;
    };

    // ===== DOM Manipulation =====
    window.DashboardDOM.setText = function(selector, text) {
        const el = this.safe(selector);
        if (el) el.textContent = text;
    };

    window.DashboardDOM.setHtml = function(selector, html) {
        const el = this.safe(selector);
        if (el) el.innerHTML = html;
    };

    window.DashboardDOM.clear = function(element) {
        if (!element) return;
        while (element.firstChild) {
            element.removeChild(element.firstChild);
        }
    };

    window.DashboardDOM.replace = function(oldElement, newElement) {
        if (!oldElement || !oldElement.parentNode) return;
        oldElement.parentNode.replaceChild(newElement, oldElement);
    };

    window.DashboardDOM.prepend = function(parent, child) {
        if (!parent || !child) return;
        parent.insertBefore(child, parent.firstChild);
    };

    window.DashboardDOM.append = function(parent, child) {
        if (!parent || !child) return;
        parent.appendChild(child);
    };

    // ===== Class Manipulation =====
    window.DashboardDOM.addClass = function(element, className) {
        if (!element) return;
        if (Array.isArray(className)) {
            element.classList.add(...className);
        } else {
            element.classList.add(className);
        }
    };

    window.DashboardDOM.removeClass = function(element, className) {
        if (!element) return;
        if (Array.isArray(className)) {
            element.classList.remove(...className);
        } else {
            element.classList.remove(className);
        }
    };

    window.DashboardDOM.toggleClass = function(element, className, force) {
        if (!element) return;
        if (force !== undefined) {
            element.classList.toggle(className, force);
        } else {
            element.classList.toggle(className);
        }
    };

    window.DashboardDOM.hasClass = function(element, className) {
        if (!element) return false;
        return element.classList.contains(className);
    };

    // ===== Visibility =====
    window.DashboardDOM.show = function(element) {
        if (!element) return;
        element.style.display = '';
    };

    window.DashboardDOM.hide = function(element) {
        if (!element) return;
        element.style.display = 'none';
    };

    window.DashboardDOM.isVisible = function(element) {
        if (!element) return false;
        return element.offsetParent !== null;
    };

    // ===== Loading States =====
    window.DashboardDOM.showLoading = function(container, message) {
        if (!container) return;
        this.clear(container);
        // Skeleton loading
        const skeleton = this.create('div', { className: 'p-3' });
        for (var i = 0; i < 5; i++) {
            skeleton.appendChild(this.create('div', {
                className: 'skeleton skeleton-row',
                children: [
                    this.create('div', { className: 'skeleton skeleton-circle' }),
                    this.create('div', { style: { flex: 1 }, children: [
                        this.create('div', { className: 'skeleton skeleton-line skeleton-line-medium' }),
                        this.create('div', { className: 'skeleton skeleton-line skeleton-line-short' })
                    ]})
                ]
            }));
        }
        container.appendChild(skeleton);
    };

    window.DashboardDOM.showError = function(container, message) {
        if (!container) return;
        this.clear(container);
        const error = this.create('div', {
            className: 'alert alert-danger',
            children: [
                this.create('i', { className: 'bi bi-exclamation-triangle me-2' }),
                this.create('span', { textContent: message || '加载失败' })
            ]
        });
        container.appendChild(error);
    };

    window.DashboardDOM.showEmpty = function(container, message, icon) {
        if (!container) return;
        this.clear(container);
        const empty = this.create('div', {
            className: 'empty-state',
            children: [
                this.create('div', {
                    className: 'empty-state-icon',
                    children: [this.create('i', { className: icon || 'bi bi-inbox' })]
                }),
                this.create('div', {
                    className: 'empty-state-title',
                    textContent: message || '暂无数据'
                })
            ]
        });
        container.appendChild(empty);
    };

    // ===== Debounced Resize Handler =====
    let resizeTimeout;
    window.DashboardDOM.onResize = function(callback) {
        window.addEventListener('resize', () => {
            clearTimeout(resizeTimeout);
            resizeTimeout = setTimeout(callback, 250);
        });
    };

    // ===== Scroll Utilities =====
    window.DashboardDOM.scrollTo = function(element, behavior = 'smooth') {
        if (!element) return;
        element.scrollIntoView({ behavior, block: 'start' });
    };

    window.DashboardDOM.scrollToTop = function(element, behavior = 'smooth') {
        if (!element) return;
        element.scrollTo({ top: 0, behavior });
    };

})();
