/**
 * Diff Panel - Visual comparison of JSON objects
 */
(function() {
    'use strict';

    window.DashboardDiffPanel = {
        /**
         * Show diff between old and new data
         * @param {Object} oldData - old/snapshot data
         * @param {Object} newData - new/current data (optional, defaults to empty)
         * @param {Object} options - { title, loading, summary }
         */
        show: function(oldData, newData, options) {
            options = options || {};
            newData = newData || {};

            // Render summary header if provided
            var title = options.title || __('diff.title') || 'Configuration Diff';
            var summaryHtml = '';
            if (options.summary) {
                summaryHtml = '<div class="alert alert-info small mb-3">' +
                    '<strong>' + (options.summary.description || '') + '</strong><br>' +
                    '<span class="text-muted">' + (options.summary.routesChanged || 0) + ' route(s), ' +
                    (options.summary.clustersChanged || 0) + ' cluster(s) changed</span>' +
                    '</div>';
            }

            var diffs = this.computeDiff(oldData, newData);
            this.render(diffs, title, summaryHtml);
        },

        /**
         * Show structured diff (routes + clusters) from backend diff API response
         */
        showStructured: function(diffData, options) {
            options = options || {};
            var title = options.title || __('diff.title') || 'Configuration Diff';
            var summaryHtml = '';
            if (options.summary) {
                summaryHtml = '<div class="alert alert-info small mb-3">' +
                    '<strong>' + window.DashboardUtils.escapeHtml(options.summary.description || '') + '</strong><br>' +
                    '<span class="text-muted">' + (options.summary.routesChanged || 0) + ' route(s), ' +
                    (options.summary.clustersChanged || 0) + ' cluster(s) changed</span>' +
                    '</div>';
            }

            // Render route diffs
            var routeDiffs = (diffData.routes || []).map(function(d) { return d; });
            var clusterDiffs = (diffData.clusters || []).map(function(d) { return d; });
            var allDiffs = routeDiffs.concat(clusterDiffs);

            this._renderStructured(allDiffs, title, summaryHtml);
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
        render: function(diffs, title, summaryHtml) {
            var modalId = 'diffModal';
            title = title || __('diff.title') || 'Configuration Diff';
            
            // Create modal if not exists
            var modal = document.getElementById(modalId);
            if (!modal) {
                modal = this._createModal(modalId);
                document.body.appendChild(modal);
            }

            // Update title
            var titleEl = modal.querySelector('.modal-title');
            if (titleEl) titleEl.textContent = title;

            // Update close button text
            var closeBtn = modal.querySelector('.modal-footer .btn-secondary');
            if (closeBtn) closeBtn.textContent = __('diff.close') || 'Close';

            var body = modal.querySelector('.modal-body');
            
            var contentHtml = (summaryHtml || '');
            if (diffs.length === 0) {
                contentHtml += '<div class="alert alert-success">' + (__('diff.noChanges') || 'No changes detected') + '</div>';
            } else {
                contentHtml += '<div class="diff-list">';
                diffs.forEach(function(diff) {
                    contentHtml += this._renderDiffItem(diff);
                }.bind(this));
                contentHtml += '</div>';
            }
            body.innerHTML = contentHtml;

            // Show modal
            var bsModal = new bootstrap.Modal(modal, { backdrop: 'static', keyboard: false });
            bsModal.show();
        },

        /**
         * Render structured diff (routes + clusters) to modal
         */
        _renderStructured: function(diffs, title, summaryHtml) {
            var modalId = 'diffModal';
            title = title || __('diff.title') || 'Configuration Diff';

            var modal = document.getElementById(modalId);
            if (!modal) {
                modal = this._createModal(modalId);
                document.body.appendChild(modal);
            }

            var titleEl = modal.querySelector('.modal-title');
            if (titleEl) titleEl.textContent = title;

            var closeBtn = modal.querySelector('.modal-footer .btn-secondary');
            if (closeBtn) closeBtn.textContent = __('diff.close') || 'Close';

            var body = modal.querySelector('.modal-body');
            var contentHtml = (summaryHtml || '');

            if (diffs.length === 0) {
                contentHtml += '<div class="alert alert-success">' + (__('diff.noChanges') || 'No changes detected') + '</div>';
            } else {
                contentHtml += '<div class="diff-list" style="max-height:60vh;overflow-y:auto;">';
                diffs.forEach(function(diff) {
                    contentHtml += this._renderDiffItem(diff);
                }.bind(this));
                contentHtml += '</div>';
            }

            body.innerHTML = contentHtml;

            var bsModal = new bootstrap.Modal(modal, { backdrop: 'static', keyboard: false });
            bsModal.show();
        },

        /**
         * Render single diff item
         */
        _renderDiffItem: function(diff) {
            var className = 'diff-' + diff.type;
            var icon = diff.type === 'added' ? '+' : diff.type === 'removed' ? '-' : '~';
            var labelMap = {
                'added': __('diff.added') || 'Added',
                'removed': __('diff.removed') || 'Removed',
                'modified': __('diff.modified') || 'Modified'
            };
            var label = labelMap[diff.type] || diff.type;
            var oldLabel = __('diff.old') || 'Old';
            var newLabel = __('diff.new') || 'New';

            var oldValStr = diff.oldValue !== undefined ? window.DashboardUtils.escapeHtml(JSON.stringify(diff.oldValue, null, 2)) : '<span class="text-muted">—</span>';
            var newValStr = diff.newValue !== undefined ? window.DashboardUtils.escapeHtml(JSON.stringify(diff.newValue, null, 2)) : '<span class="text-muted">—</span>';

            var html = '<div class="' + className + ' p-2 mb-2 border-start border-3 rounded">';
            html += '<div class="d-flex align-items-center justify-content-between">';
            html += '<div><span class="fw-bold">' + icon + ' ' + label + '</span> <code class="text-muted small">' + window.DashboardUtils.escapeHtml(diff.path) + '</code></div>';
            html += '<button class="btn btn-sm btn-outline-secondary btn-icon-only diff-toggle" onclick="this.closest(\'.diff-' + diff.type + '\').classList.toggle(\'diff-collapsed\')"><i class="bi bi-chevron-down"></i></button>';
            html += '</div>';

            // Side-by-side view
            html += '<div class="diff-body mt-2">';
            html += '<div class="row g-2">';
            html += '<div class="col-6"><div class="small text-muted mb-1">' + oldLabel + '</div><pre class="diff-pre diff-old">' + oldValStr + '</pre></div>';
            html += '<div class="col-6"><div class="small text-muted mb-1">' + newLabel + '</div><pre class="diff-pre diff-new">' + newValStr + '</pre></div>';
            html += '</div>';
            html += '</div>';

            html += '</div>';
            return html;
        },

        /**
         * Create diff modal HTML
         */
        _createModal: function(modalId) {
            var title = __('diff.title') || 'Configuration Diff';
            var closeText = __('diff.close') || 'Close';
            
            var modal = document.createElement('div');
            modal.className = 'modal fade';
            modal.id = modalId;
            modal.setAttribute('tabindex', '-1');
            
            modal.innerHTML = 
                '<div class="modal-dialog modal-lg modal-dialog-scrollable">' +
                '  <div class="modal-content">' +
                '    <div class="modal-header">' +
                '      <h5 class="modal-title">' + title + '</h5>' +
                '      <button type="button" class="btn-close" data-bs-dismiss="modal"></button>' +
                '    </div>' +
                '    <div class="modal-body"></div>' +
                '    <div class="modal-footer">' +
                '      <button type="button" class="btn btn-secondary" data-bs-dismiss="modal">' + closeText + '</button>' +
                '    </div>' +
                '  </div>' +
                '</div>';
            
            return modal;
        },

    };

})();
