/**
 * Diff Panel - Visual comparison of JSON objects
 */
(function() {
    'use strict';

    window.DashboardDiffPanel = {
        /**
         * Show diff between old and new data
         */
        show: function(oldData, newData) {
            var diffs = this.computeDiff(oldData, newData);
            this.render(diffs);
        },

        /**
         * Compute diff between two objects
         */
        computeDiff: function(oldObj, newObj, path) {
            path = path || '';
            var diffs = [];

            if (!oldObj || !newObj) return diffs;

            var allKeys = new Set();
            Object.keys(oldObj).forEach(function(k) { return allKeys.add(k); });
            Object.keys(newObj).forEach(function(k) { return allKeys.add(k); });

            allKeys.forEach(function(key) {
                var currentPath = path ? path + '.' + key : key;
                var oldValue = oldObj[key];
                var newValue = newObj[key];

                if (!(key in oldObj)) {
                    // Added
                    diffs.push({
                        path: currentPath,
                        type: 'added',
                        newValue: newValue
                    });
                } else if (!(key in newObj)) {
                    // Removed
                    diffs.push({
                        path: currentPath,
                        type: 'removed',
                        oldValue: oldValue
                    });
                } else if (typeof oldValue !== typeof newValue) {
                    // Type changed
                    diffs.push({
                        path: currentPath,
                        type: 'modified',
                        oldValue: oldValue,
                        newValue: newValue
                    });
                } else if (typeof oldValue === 'object' && oldValue !== null) {
                    // Recursive diff for objects
                    var nestedDiffs = this.computeDiff(oldValue, newValue, currentPath);
                    diffs = diffs.concat(nestedDiffs);
                } else if (oldValue !== newValue) {
                    // Modified
                    diffs.push({
                        path: currentPath,
                        type: 'modified',
                        oldValue: oldValue,
                        newValue: newValue
                    });
                }
            }.bind(this));

            return diffs;
        },

        /**
         * Render diff to modal
         */
        render: function(diffs) {
            var modalId = 'diffModal';
            
            // Create modal if not exists
            var modal = document.getElementById(modalId);
            if (!modal) {
                modal = this._createModal(modalId);
                document.body.appendChild(modal);
            }

            var body = modal.querySelector('.modal-body');
            
            if (diffs.length === 0) {
                body.innerHTML = '<div class="alert alert-success">No changes detected</div>';
            } else {
                var html = '<div class="diff-list">';
                diffs.forEach(function(diff) {
                    html += this._renderDiffItem(diff);
                }.bind(this));
                html += '</div>';
                body.innerHTML = html;
            }

            // Show modal
            var bsModal = new bootstrap.Modal(modal);
            bsModal.show();
        },

        /**
         * Render single diff item
         */
        _renderDiffItem: function(diff) {
            var className = 'diff-' + diff.type;
            var icon = diff.type === 'added' ? '+' : diff.type === 'removed' ? '-' : '~';
            var label = diff.type === 'added' ? 'Added' : diff.type === 'removed' ? 'Removed' : 'Modified';
            
            var html = '<div class="' + className + ' p-2 mb-2 border-start border-3 rounded">';
            html += '<div class="fw-bold">' + icon + ' ' + label + '</div>';
            html += '<div class="text-muted small">' + window.DashboardUtils.escapeHtml(diff.path) + '</div>';
            
            if (diff.oldValue !== undefined) {
                html += '<div class="text-danger small">Old: ' + window.DashboardUtils.escapeHtml(JSON.stringify(diff.oldValue)) + '</div>';
            }
            
            if (diff.newValue !== undefined) {
                html += '<div class="text-success small">New: ' + window.DashboardUtils.escapeHtml(JSON.stringify(diff.newValue)) + '</div>';
            }
            
            html += '</div>';
            return html;
        },

        /**
         * Create diff modal HTML
         */
        _createModal: function(modalId) {
            var modal = document.createElement('div');
            modal.className = 'modal fade';
            modal.id = modalId;
            modal.setAttribute('tabindex', '-1');
            
            modal.innerHTML = 
                '<div class="modal-dialog modal-lg">' +
                '  <div class="modal-content">' +
                '    <div class="modal-header">' +
                '      <h5 class="modal-title">Configuration Diff</h5>' +
                '      <button type="button" class="btn-close" data-bs-dismiss="modal"></button>' +
                '    </div>' +
                '    <div class="modal-body"></div>' +
                '    <div class="modal-footer">' +
                '      <button type="button" class="btn btn-secondary" data-bs-dismiss="modal">Close</button>' +
                '    </div>' +
                '  </div>' +
                '</div>';
            
            return modal;
        },

    };

})();
