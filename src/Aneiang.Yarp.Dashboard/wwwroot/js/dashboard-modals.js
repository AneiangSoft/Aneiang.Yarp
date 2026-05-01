/**
 * Dashboard Modals Module - Aneiang.Yarp Gateway Dashboard
 * Quick Add Modal and JSON Editor Modal
 */
(function() {
    'use strict';

    // ===== Quick Add Modal =====
    window.showQuickAddModal = function(title, defaultData, onSubmit) {
        var modalId = 'quickAddModal_' + Date.now();
        var textareaId = 'quickAddTextarea_' + Date.now();
        var __ = window.__;

        // Generate form fields
        var fields = Object.keys(defaultData);
        var formHtml = '';

        fields.forEach(function(field) {
            var value = typeof defaultData[field] === 'object'
                ? JSON.stringify(defaultData[field], null, 2)
                : defaultData[field];

            var isTextarea = field === 'destinations' || field === 'transforms' || field === 'healthCheck';
            var label = field.replace(/([A-Z])/g, ' $1').replace(/^./, function(s) { return s.toUpperCase(); });

            if (isTextarea) {
                formHtml += '<div class="mb-3"><label class="form-label">' + label + ':</label>' +
                    '<textarea id="form_' + field + '" class="form-control" rows="3" style="font-family: monospace; font-size: 12px;">' +
                    window.escapeHtml(value) + '</textarea></div>';
            } else {
                formHtml += '<div class="mb-3"><label class="form-label">' + label + ':</label>' +
                    '<input type="text" id="form_' + field + '" class="form-control" value="' + window.escapeHtml(value) + '" />' +
                    '</div>';
            }
        });

        var jsonStr = JSON.stringify(defaultData, null, 2);

        var modalHtml = '<div class="modal fade" id="' + modalId + '" tabindex="-1">' +
            '<div class="modal-dialog modal-lg"><div class="modal-content">' +
            '<div class="modal-header"><h5 class="modal-title">' + window.escapeHtml(title) + '</h5>' +
            '<button type="button" class="btn-close" data-bs-dismiss="modal"></button></div>' +
            '<div class="modal-body">' +
            // Mode Toggle
            '<div class="btn-group mb-3" role="group">' +
            '<input type="radio" class="btn-check" name="addMode" id="modeForm" value="form" checked>' +
            '<label class="btn btn-outline-primary" for="modeForm"><i class="bi bi-card-list me-1"></i>' + __('modal.formMode') + '</label>' +
            '<input type="radio" class="btn-check" name="addMode" id="modeJson" value="json">' +
            '<label class="btn btn-outline-primary" for="modeJson"><i class="bi bi-code-square me-1"></i>' + __('modal.jsonMode') + '</label>' +
            '</div>' +
            // Form Mode
            '<div id="formModePanel">' + formHtml + '</div>' +
            // JSON Mode
            '<div id="jsonModePanel" style="display:none;">' +
            '<div class="mb-2">' +
            '<button type="button" class="btn btn-sm btn-outline-primary me-1" onclick="formatJsonInTextarea(\'' + textareaId + '\')"><i class="bi bi-braces"></i> ' + __('modal.format') + '</button>' +
            '<button type="button" class="btn btn-sm btn-outline-secondary me-1" onclick="minifyJsonInTextarea(\'' + textareaId + '\')"><i class="bi bi-braces-asterisk"></i> ' + __('modal.minify') + '</button>' +
            '<span class="text-muted small ms-2">' + __('modal.tabHint') + '</span>' +
            '</div>' +
            '<label class="form-label small text-muted">' + __('modal.jsonInput') + '</label>' +
            '<textarea id="' + textareaId + '" class="form-control" rows="16" style="font-family: \'Consolas\', \'Monaco\', monospace; font-size: 13px; background: #1e1e1e; color: #d4d4d4; border: 1px solid #28a745;">' +
            window.escapeHtml(jsonStr) + '</textarea>' +
            '</div>' +
            '<div id="quickAddError" class="alert alert-danger mt-3" style="display:none;"></div>' +
            '</div>' +
            '<div class="modal-footer">' +
            '<button type="button" class="btn btn-secondary" data-bs-dismiss="modal">' + __('modal.cancel') + '</button>' +
            '<button type="button" class="btn btn-success" onclick="submitQuickAdd(\'' + modalId + '\', \'' + textareaId + '\')"><i class="bi bi-plus-circle me-1"></i>' + __('modal.add') + '</button>' +
            '</div></div></div></div>';

        var existingModal = document.getElementById(modalId);
        if (existingModal) existingModal.remove();

        document.body.insertAdjacentHTML('beforeend', modalHtml);
        window._quickAddOnSubmit = onSubmit;
        window._jsonEditorInitialized = false;
        window._quickAddFields = fields; // Store fields for sync

        var modalEl = document.getElementById(modalId);
        var modal = new bootstrap.Modal(modalEl);

        // Reset flag when modal is hidden
        modalEl.addEventListener('hidden.bs.modal', function() {
            window._jsonEditorInitialized = false;
            modalEl.remove();
        });

        // Mode toggle with data sync
        document.getElementById('modeForm').addEventListener('change', function() {
            document.getElementById('formModePanel').style.display = 'block';
            document.getElementById('jsonModePanel').style.display = 'none';
            // Sync JSON -> Form: parse JSON and update form fields
            try {
                var textarea = document.getElementById(textareaId);
                if (textarea && textarea.value) {
                    var jsonData = JSON.parse(textarea.value);
                    window._quickAddFields.forEach(function(field) {
                        var formField = document.getElementById('form_' + field);
                        if (formField && jsonData[field] !== undefined) {
                            var value = typeof jsonData[field] === 'object'
                                ? JSON.stringify(jsonData[field], null, 2)
                                : jsonData[field];
                            formField.value = value;
                        }
                    });
                }
            } catch (e) {
                // Invalid JSON, keep form as is
            }
        });

        document.getElementById('modeJson').addEventListener('change', function() {
            document.getElementById('formModePanel').style.display = 'none';
            document.getElementById('jsonModePanel').style.display = 'block';
            // Sync Form -> JSON: collect form data and update JSON editor
            try {
                var formData = {};
                window._quickAddFields.forEach(function(field) {
                    var formField = document.getElementById('form_' + field);
                    if (formField) {
                        var value = formField.value;
                        if (value.startsWith('{') || value.startsWith('[')) {
                            try { value = JSON.parse(value); } catch (e) { /* keep as string */ }
                        }
                        formData[field] = value;
                    }
                });
                var jsonStr = JSON.stringify(formData, null, 2);
                var textarea = document.getElementById(textareaId);
                if (textarea) {
                    if (textarea._codeMirror) {
                        textarea._codeMirror.setValue(jsonStr);
                    } else {
                        textarea.value = jsonStr;
                    }
                }
            } catch (e) {
                // Error syncing, keep JSON as is
            }
            // Only initialize JSON editor once per modal instance
            if (!window._jsonEditorInitialized) {
                setTimeout(function() { window.initJsonEditor(textareaId); }, 100);
                window._jsonEditorInitialized = true;
            } else {
                // Already initialized, just refresh
                setTimeout(function() {
                    var textarea = document.getElementById(textareaId);
                    if (textarea && textarea._codeMirror) {
                        textarea._codeMirror.refresh();
                    }
                }, 50);
            }
        });

        modal.show();
    };

    // ===== Submit Quick Add =====
    window.submitQuickAdd = async function(modalId, textareaId) {
        var modal = document.getElementById(modalId);
        var errorDiv = document.getElementById('quickAddError');
        var mode = document.querySelector('input[name="addMode"]:checked').value;
        var data;

        try {
            if (mode === 'form') {
                // Form mode: collect form fields
                var fields = modal.querySelectorAll('[id^="form_"]');
                data = {};
                fields.forEach(function(field) {
                    var fieldName = field.id.replace('form_', '');
                    var value = field.value;
                    if (value.startsWith('{') || value.startsWith('[')) {
                        try { value = JSON.parse(value); } catch (e) { /* keep as string */ }
                    }
                    data[fieldName] = value;
                });
            } else {
                // JSON mode: parse from textarea
                var textarea = document.getElementById(textareaId);
                data = JSON.parse(textarea.value);
            }

            var success = await window._quickAddOnSubmit(data);
            if (success) {
                var bsModal = bootstrap.Modal.getInstance(modal);
                if (bsModal) bsModal.hide();
            }
        } catch (e) {
            errorDiv.textContent = 'Error: ' + e.message;
            errorDiv.style.display = 'block';
        }
    };

    // ===== JSON Editor Modal =====
    window.showJsonEditor = function(title, data, onSave) {
        var jsonStr = typeof data === 'string' ? data : JSON.stringify(data, null, 2);
        var textareaId = 'jsonEditorTextarea_' + Date.now();
        var __ = window.__;

        var modalHtml = '<div class="modal fade" id="jsonEditorModal" tabindex="-1">' +
            '<div class="modal-dialog modal-lg"><div class="modal-content">' +
            '<div class="modal-header"><h5 class="modal-title">' + window.escapeHtml(title) + '</h5>' +
            '<button type="button" class="btn-close" data-bs-dismiss="modal"></button></div>' +
            '<div class="modal-body">' +
            '<div class="mb-2">' +
            '<button type="button" class="btn btn-sm btn-outline-primary me-1" onclick="formatJsonInTextarea(\'' + textareaId + '\')"><i class="bi bi-braces"></i> ' + __('modal.format') + '</button>' +
            '<button type="button" class="btn btn-sm btn-outline-secondary me-1" onclick="minifyJsonInTextarea(\'' + textareaId + '\')"><i class="bi bi-braces-asterisk"></i> ' + __('modal.minify') + '</button>' +
            '<button type="button" class="btn btn-sm btn-outline-info me-1" onclick="copyJsonToClipboard(\'' + textareaId + '\')"><i class="bi bi-clipboard"></i> ' + __('modal.copy') + '</button>' +
            '<span class="text-muted small ms-2">' + __('modal.tabHint') + '</span>' +
            '</div>' +
            '<label class="form-label small text-muted">' + __('modal.jsonInput') + '</label>' +
            '<textarea id="' + textareaId + '" class="form-control" rows="16" style="font-family: \'Consolas\', \'Monaco\', monospace; font-size: 13px; background: #1e1e1e; color: #d4d4d4; border: 1px solid #28a745;">' +
            window.escapeHtml(jsonStr) + '</textarea>' +
            '</div>' +
            '<div class="modal-footer">' +
            '<button type="button" class="btn btn-secondary" data-bs-dismiss="modal">' + __('modal.cancel') + '</button>' +
            '<button type="button" class="btn btn-primary" id="jsonSaveBtn">' + __('modal.saveChanges') + '</button>' +
            '</div></div></div></div>';

        var existingModal = document.getElementById('jsonEditorModal');
        if (existingModal) existingModal.remove();

        document.body.insertAdjacentHTML('beforeend', modalHtml);
        window._jsonEditorOnSave = onSave;

        var modalEl = document.getElementById('jsonEditorModal');
        var modal = new bootstrap.Modal(modalEl);

        modalEl.addEventListener('hidden.bs.modal', function() {
            modalEl.remove();
        });

        modalEl.addEventListener('shown.bs.modal', function() {
            window.initJsonEditor(textareaId);

            document.getElementById('jsonSaveBtn').onclick = function() {
                try {
                    var json = JSON.parse(document.getElementById(textareaId).value);
                    modal.hide();
                    modalEl.addEventListener('hidden.bs.modal', function() { modalEl.remove(); });
                    onSave(json);
                } catch(e) {
                    alert('Invalid JSON: ' + e.message);
                }
            };
        });

        modal.show();
    };
})();
