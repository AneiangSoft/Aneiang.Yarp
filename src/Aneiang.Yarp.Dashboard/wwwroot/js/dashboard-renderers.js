/**
 * Dashboard Renderers - Aneiang.Yarp Gateway Dashboard
 * Common rendering utilities for badges, JSON, empty states, copy buttons, etc.
 */
(function() {
    'use strict';

    var __ = window.__ || function(key) { return key; };

    // ===== Badge Renderers =====
    var BadgeRenderers = {
        /**
         * Render health status badge
         */
        health(status) {
            var s = (status || '').toLowerCase();
            if (s === 'healthy') {
                return '<span class="badge bg-success">Healthy</span>';
            } else if (s === 'unhealthy') {
                return '<span class="badge bg-danger">Unhealthy</span>';
            } else {
                return '<span class="badge bg-warning text-dark">Unknown</span>';
            }
        },

        /**
         * Render log level badge
         */
        logLevel(level) {
            var l = (level || '').toUpperCase();
            var colors = {
                'TRACE': 'bg-secondary',
                'DEBUG': 'bg-info',
                'INFORMATION': 'bg-primary',
                'INFO': 'bg-primary',
                'WARNING': 'bg-warning text-dark',
                'ERROR': 'bg-danger',
                'CRITICAL': 'bg-dark',
                'FATAL': 'bg-dark'
            };
            var color = colors[l] || 'bg-secondary';
            return '<span class="badge ' + color + '">' + l + '</span>';
        },

        /**
         * Render route order badge
         */
        routeOrder(order) {
            var color = order <= 5 ? 'bg-primary' : order <= 20 ? 'bg-info' : 'bg-secondary';
            return '<span class="badge ' + color + '">' + order + '</span>';
        },

        /**
         * Render source badge
         */
        source(source) {
            if (source === 'dynamic') {
                return '<span class="badge bg-success">Dynamic</span>';
            } else if (source === 'static') {
                return '<span class="badge bg-secondary">Static</span>';
            } else {
                return '<span class="badge bg-light text-dark">' + window.escapeHtml(source || 'Unknown') + '</span>';
            }
        },

        /**
         * Render status code badge
         */
        statusCode(code) {
            if (!code) return '<span class="text-muted">-</span>';
            var color = code < 300 ? 'bg-success' : code < 400 ? 'bg-info' : code < 500 ? 'bg-warning text-dark' : 'bg-danger';
            return '<span class="badge ' + color + '">' + code + '</span>';
        },

        /**
         * Render editable badge
         */
        editable(isEditable) {
            if (isEditable) {
                return '<span class="badge bg-primary">Editable</span>';
            } else {
                return '<span class="badge bg-secondary">Read-Only</span>';
            }
        }
    };

    // ===== JSON Renderers =====
    var JsonRenderers = {
        /**
         * Render JSON with syntax highlighting
         */
        highlight(obj, maxDepth) {
            if (maxDepth === undefined) maxDepth = 10;
            try {
                var json = JSON.stringify(obj, null, 2);
                if (json.length > 50000) {
                    return '<div class="alert alert-warning">JSON too large to display (' + json.length + ' chars)</div>';
                }
                return '<pre class="json-highlight">' + this.syntaxHighlight(json) + '</pre>';
            } catch (e) {
                return '<pre class="text-muted">' + window.escapeHtml(String(obj)) + '</pre>';
            }
        },

        /**
         * Add syntax highlighting to JSON string
         */
        syntaxHighlight(json) {
            json = window.escapeHtml(json);
            json = json.replace(/&quot;/g, '"');
            json = json.replace(/("(\\u[a-zA-Z0-9]{4}|\\[^u]|[^\\"])*"(\s*:)?|\b(true|false|null)\b|-?\d+(?:\.\d*)?(?:[eE][+\-]?\d+)?)/g, function(match) {
                var cls = 'json-number';
                if (/^"/.test(match)) {
                    if (/:$/.test(match)) {
                        cls = 'json-key';
                    } else {
                        cls = 'json-string';
                    }
                } else if (/true|false/.test(match)) {
                    cls = 'json-boolean';
                } else if (/null/.test(match)) {
                    cls = 'json-null';
                }
                return '<span class="' + cls + '">' + match + '</span>';
            });
            return json;
        },

        /**
         * Render collapsible JSON
         */
        collapsible(obj, title, collapsed) {
            var id = 'json-' + Math.random().toString(36).substr(2, 9);
            var html = '<div class="json-collapsible">';
            html += '<button class="btn btn-sm btn-outline-secondary" type="button" data-bs-toggle="collapse" data-bs-target="#' + id + '" aria-expanded="' + (!collapsed) + '">';
            html += '<i class="bi bi-code-square"></i> ' + window.escapeHtml(title || 'JSON');
            html += '</button>';
            html += '<div class="collapse ' + (collapsed ? '' : 'show') + '" id="' + id + '">';
            html += this.highlight(obj);
            html += '</div>';
            html += '</div>';
            return html;
        }
    };

    // ===== UI Renderers =====
    var UiRenderers = {
        /**
         * Render empty state
         */
        empty(message, icon) {
            message = message || 'No data available';
            icon = icon || 'bi-inbox';
            return '<div class="text-center text-muted py-5">' +
                   '<i class="bi ' + icon + '" style="font-size:48px;"></i>' +
                   '<p class="mt-3">' + window.escapeHtml(message) + '</p>' +
                   '</div>';
        },

        /**
         * Render loading state
         */
        loading(message) {
            message = message || 'Loading...';
            return '<div class="text-center py-5">' +
                   '<div class="spinner-border text-primary" role="status"></div>' +
                   '<p class="mt-3 text-muted">' + window.escapeHtml(message) + '</p>' +
                   '</div>';
        },

        /**
         * Render copy button
         */
        copyButton(text, label) {
            label = label || 'Copy';
            var id = 'copy-' + Math.random().toString(36).substr(2, 9);
            return '<button class="btn btn-sm btn-outline-secondary copy-btn" data-copy-id="' + id + '" data-copy-text="' + window.escapeHtml(text) + '" title="' + window.escapeHtml(label) + '">' +
                   '<i class="bi bi-clipboard"></i> ' + window.escapeHtml(label) +
                   '</button>';
        },

        /**
         * Render expand/collapse icon
         */
        expandIcon(expanded) {
            return '<span class="expand-icon" style="display:inline-block;width:16px;transition:transform .2s;">' + 
                   (expanded ? '&#9660;' : '&#9654;') + 
                   '</span>';
        },

        /**
         * Render action buttons (edit/delete)
         */
        actionButtons(id, isEditable, editHandler, deleteHandler) {
            if (!isEditable) {
                return '<span class="text-muted">-</span>';
            }
            var html = '';
            html += '<button class="btn btn-sm btn-outline-primary me-1" onclick="' + editHandler + '" title="Edit">';
            html += '<i class="bi bi-pencil"></i></button>';
            html += '<button class="btn btn-sm btn-outline-danger" onclick="' + deleteHandler + '" title="Delete">';
            html += '<i class="bi bi-trash"></i></button>';
            return html;
        },

        /**
         * Render toast notification
         */
        toast(message, type, duration) {
            type = type || 'info';
            duration = duration || 3000;
            var icons = {
                'success': 'bi-check-circle',
                'error': 'bi-x-circle',
                'warning': 'bi-exclamation-triangle',
                'info': 'bi-info-circle'
            };
            var id = 'toast-' + Date.now();
            var html = '<div id="' + id + '" class="toast align-items-center text-white bg-' + type + ' border-0 position-fixed" role="alert" style="top:20px;right:20px;z-index:9999;">';
            html += '<div class="d-flex">';
            html += '<div class="toast-body">';
            html += '<i class="bi ' + icons[type] + '"></i> ' + window.escapeHtml(message);
            html += '</div>';
            html += '<button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast"></button>';
            html += '</div>';
            html += '</div>';
            
            document.body.insertAdjacentHTML('beforeend', html);
            var toastEl = document.getElementById(id);
            var toast = new bootstrap.Toast(toastEl, { delay: duration });
            toast.show();
            toastEl.addEventListener('hidden.bs.toast', function() {
                toastEl.remove();
            });
        }
    };

    // ===== Export to global =====
    window.dashboardRenderers = {
        badge: BadgeRenderers,
        json: JsonRenderers,
        ui: UiRenderers
    };

    // Add CSS for JSON highlighting
    if (!document.getElementById('dashboard-json-styles')) {
        var style = document.createElement('style');
        style.id = 'dashboard-json-styles';
        style.textContent = `
            .json-highlight { 
                background: #f8f9fa; 
                border: 1px solid #dee2e6; 
                border-radius: 4px; 
                padding: 12px; 
                overflow-x: auto; 
                font-size: 12px; 
                line-height: 1.5;
            }
            .json-key { color: #d63384; }
            .json-string { color: #198754; }
            .json-number { color: #0d6efd; }
            .json-boolean { color: #fd7e14; }
            .json-null { color: #6c757d; }
            .json-collapsible { margin: 8px 0; }
            .expand-icon { transition: transform 0.2s; }
            .expand-icon.expanded { transform: rotate(90deg); }
        `;
        document.head.appendChild(style);
    }

})();
