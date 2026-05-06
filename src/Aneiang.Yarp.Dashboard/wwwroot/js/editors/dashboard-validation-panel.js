/**
 * Validation Panel - Display validation results
 */
(function() {
    'use strict';

    window.DashboardValidationPanel = {
        /**
         * Show validation results
         */
        show: function(validationResult) {
            var panelId = 'validationPanel';
            var panel = document.getElementById(panelId);
            
            if (!panel) {
                panel = this._createPanel(panelId);
                var container = document.querySelector('.editor-container') || document.body;
                container.appendChild(panel);
            }

            if (validationResult.valid) {
                this._renderSuccess(panel);
            } else {
                this._renderErrors(panel, validationResult.errors);
            }

            panel.style.display = 'block';
        },

        /**
         * Hide validation panel
         */
        hide: function() {
            var panel = document.getElementById('validationPanel');
            if (panel) {
                panel.style.display = 'none';
            }
        },

        /**
         * Render success state
         */
        _renderSuccess: function(panel) {
            panel.className = 'validation-panel alert alert-success';
            panel.innerHTML = 
                '<div class="d-flex align-items-center">' +
                '  <i class="bi bi-check-circle me-2"></i>' +
                '  <strong>Validation passed</strong>' +
                '</div>';
        },

        /**
         * Render error list
         */
        _renderErrors: function(panel, errors) {
            panel.className = 'validation-panel alert alert-danger';
            
            var html = 
                '<div class="d-flex align-items-center mb-2">' +
                '  <i class="bi bi-exclamation-triangle me-2"></i>' +
                '  <strong>Validation failed (' + errors.length + ' errors)</strong>' +
                '</div>' +
                '<div class="validation-errors" style="max-height: 200px; overflow-y: auto;">';
            
            errors.forEach(function(error, index) {
                html += 
                    '<div class="validation-error p-2 mb-1 border-start border-3 border-danger rounded" ' +
                    '     style="cursor: pointer; background: #fff5f5;" ' +
                    '     onclick="window.DashboardValidationPanel.jumpToField(\'' + this.escapeHtml(error.path) + '\')">' +
                    '  <div class="small text-muted">' + this.escapeHtml(error.path) + '</div>' +
                    '  <div>' + this.escapeHtml(error.message) + '</div>' +
                    '</div>';
            }.bind(this));
            
            html += '</div>';
            panel.innerHTML = html;
        },

        /**
         * Jump to field in form
         */
        jumpToField: function(path) {
            var input = document.querySelector('[data-path="' + path + '"]');
            if (input) {
                input.scrollIntoView({ behavior: 'smooth', block: 'center' });
                input.focus();
                input.classList.add('is-invalid');
                setTimeout(function() {
                    input.classList.remove('is-invalid');
                }, 2000);
            }
        },

        /**
         * Create validation panel HTML
         */
        _createPanel: function(panelId) {
            var panel = document.createElement('div');
            panel.id = panelId;
            panel.style.display = 'none';
            return panel;
        },

        /**
         * Escape HTML
         */
        escapeHtml: function(text) {
            if (text === null || text === undefined) return '';
            var str = String(text);
            var map = {
                '&': '&amp;',
                '<': '&lt;',
                '>': '&gt;',
                '"': '&quot;',
                "'": '&#039;'
            };
            return str.replace(/[&<>"']/g, function(m) { return map[m]; });
        }
    };
})();
