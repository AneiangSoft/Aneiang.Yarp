/**
 * Dashboard Modals Module - Aneiang.Yarp Gateway Dashboard
 * Quick Add Modal and JSON Editor Modal
 */
(function() {
    'use strict';

    // ===== Quick Add Modal =====
    window.showQuickAddModal = function(title, defaultData, onSubmit, options) {
        options = options || {};
        var fieldTypes = options.fieldTypes || {}; // { fieldName: 'textarea' | 'select' | 'keyvalue' }
        var selectOptions = options.selectOptions || {}; // { fieldName: [{value, label}] }
        var requiredFields = options.requiredFields || []; // ['field1', 'field2']
        var conditionalFields = options.conditionalFields || {}; // { fieldName: { showWhen: 'otherField', equals: 'value' } }
        
        var modalId = 'quickAddModal_' + Date.now();
        var textareaId = 'quickAddTextarea_' + Date.now();
        var __ = window.__;

        // Generate form fields
        var fields = Object.keys(defaultData);
        var formHtml = '';

        fields.forEach(function(field) {
            // Check conditional display
            if (conditionalFields[field]) {
                var condition = conditionalFields[field];
                var showFieldValue = defaultData[condition.showWhen];
                if (showFieldValue !== condition.equals) {
                    return; // Skip this field
                }
            }
            
            var value = typeof defaultData[field] === 'object'
                ? JSON.stringify(defaultData[field], null, 2)
                : defaultData[field];

            var fieldType = fieldTypes[field] || 'text';
            var label = field.replace(/([A-Z])/g, ' $1').replace(/^./, function(s) { return s.toUpperCase(); });
            var isRequired = requiredFields.includes(field);
            var labelHtml = isRequired ? label + ' <span class="text-danger">*</span>' : label;

            if (fieldType === 'keyvalue') {
                // Key-Value pairs editor (for destinations, transforms, etc.)
                formHtml += '<div class="mb-3"><label class="form-label">' + labelHtml + ':</label>';
                formHtml += '<div id="kv_' + field + '" class="kv-editor">';
                
                // Parse existing key-value pairs
                var kvData = {};
                try {
                    if (typeof defaultData[field] === 'object') {
                        kvData = defaultData[field];
                    }
                } catch(e) {}
                
                // Add existing pairs
                Object.keys(kvData).forEach(function(key) {
                    formHtml += '<div class="kv-row mb-2">';
                    formHtml += '<input type="text" class="form-control form-control-sm kv-key mb-1" placeholder="' + __('modal.kvKeyPlaceholder') + '" value="' + window.escapeHtml(key) + '" />';
                    formHtml += '<input type="text" class="form-control form-control-sm kv-value" placeholder="' + __('modal.kvValuePlaceholder') + '" value="' + window.escapeHtml(kvData[key]) + '" />';
                    formHtml += '<button type="button" class="btn btn-sm btn-outline-danger mt-1" onclick="this.parentElement.remove()"><i class="bi bi-trash"></i></button>';
                    formHtml += '</div>';
                });
                
                formHtml += '</div>';
                formHtml += '<button type="button" class="btn btn-sm btn-outline-primary mt-2" onclick="addKVRow(\'kv_' + field + '\')"><i class="bi bi-plus"></i> ' + __('modal.add') + '</button>';
                formHtml += '<input type="hidden" id="form_' + field + '" value="' + window.escapeHtml(value) + '" />';
                formHtml += '</div>';
            } else if (fieldType === 'select') {
                // Select dropdown
                var opts = selectOptions[field] || [];
                formHtml += '<div class="mb-3"><label class="form-label">' + labelHtml + ':</label>';
                formHtml += '<select id="form_' + field + '" class="form-select">';
                opts.forEach(function(opt) {
                    var selected = value === opt.value ? 'selected' : '';
                    formHtml += '<option value="' + window.escapeHtml(opt.value) + '" ' + selected + '>' + window.escapeHtml(opt.label) + '</option>';
                });
                formHtml += '</select></div>';
            } else if (fieldType === 'textarea') {
                formHtml += '<div class="mb-3"><label class="form-label">' + labelHtml + ':</label>' +
                    '<textarea id="form_' + field + '" class="form-control" rows="3" style="font-family: monospace; font-size: 12px;">' +
                    window.escapeHtml(value) + '</textarea></div>';
            } else {
                formHtml += '<div class="mb-3"><label class="form-label">' + labelHtml + ':</label>' +
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
        window._quickAddFieldTypes = fieldTypes; // Store field types

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
                            
                            // Handle key-value editors
                            if (window._quickAddFieldTypes[field] === 'keyvalue') {
                                updateKVEditor('kv_' + field, jsonData[field]);
                            } else {
                                formField.value = value;
                            }
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
                var formData = collectFormData(window._quickAddFields, window._quickAddFieldTypes);
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
        
        // Setup conditional field visibility
        if (Object.keys(conditionalFields).length > 0) {
            Object.keys(conditionalFields).forEach(function(targetField) {
                var condition = conditionalFields[targetField];
                var triggerField = document.getElementById('form_' + condition.showWhen);
                
                if (triggerField) {
                    // Initial visibility
                    updateFieldVisibility(targetField, triggerField.value, condition);
                    
                    // Listen for changes
                    triggerField.addEventListener('change', function() {
                        updateFieldVisibility(targetField, this.value, condition);
                    });
                }
            });
        }
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
                data = collectFormData(window._quickAddFields, window._quickAddFieldTypes);
                // Sync form -> JSON before submit
                var jsonStr = JSON.stringify(data, null, 2);
                var textarea = document.getElementById(textareaId);
                if (textarea) {
                    if (textarea._codeMirror) {
                        textarea._codeMirror.setValue(jsonStr);
                    } else {
                        textarea.value = jsonStr;
                    }
                }
            } else {
                // JSON mode: parse from textarea (CodeMirror or plain textarea)
                var textarea = document.getElementById(textareaId);
                var jsonValue = textarea._codeMirror ? textarea._codeMirror.getValue() : textarea.value;
                data = JSON.parse(jsonValue);
                // Sync JSON -> form before submit
                window._quickAddFields.forEach(function(field) {
                    var formField = document.getElementById('form_' + field);
                    if (formField && data[field] !== undefined) {
                        var value = typeof data[field] === 'object'
                            ? JSON.stringify(data[field], null, 2)
                            : data[field];
                        
                        if (window._quickAddFieldTypes[field] === 'keyvalue') {
                            updateKVEditor('kv_' + field, data[field]);
                        } else {
                            formField.value = value;
                        }
                    }
                });
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

    // ===== Helper: Update Field Visibility =====
    function updateFieldVisibility(targetField, triggerValue, condition) {
        var formGroup = document.getElementById('form_' + targetField);
        if (!formGroup) return;
        
        // Find parent div.mb-3
        var parentDiv = formGroup.closest('.mb-3');
        if (!parentDiv) return;
        
        var shouldShow = (triggerValue === condition.equals);
        parentDiv.style.display = shouldShow ? 'block' : 'none';
    }

    // ===== Helper: Collect Form Data =====
    function collectFormData(fields, fieldTypes) {
        var data = {};
        fields.forEach(function(field) {
            var formField = document.getElementById('form_' + field);
            if (formField) {
                var value = formField.value;
                
                // Handle key-value editors
                if (fieldTypes[field] === 'keyvalue') {
                    value = collectKVData('kv_' + field);
                } else if (value.startsWith('{') || value.startsWith('[')) {
                    try { value = JSON.parse(value); } catch (e) { /* keep as string */ }
                }
                data[field] = value;
            }
        });
        return data;
    }

    // ===== Helper: Add Key-Value Row =====
    window.addKVRow = function(containerId) {
        var container = document.getElementById(containerId);
        var row = document.createElement('div');
        row.className = 'kv-row mb-2';
        row.innerHTML = '<input type="text" class="form-control form-control-sm kv-key mb-1" placeholder="Key (e.g., d1)" />' +
            '<input type="text" class="form-control form-control-sm kv-value" placeholder="Value (e.g., http://localhost:8080)" />' +
            '<button type="button" class="btn btn-sm btn-outline-danger mt-1" onclick="this.parentElement.remove()"><i class="bi bi-trash"></i></button>';
        container.appendChild(row);
    };

    // ===== Helper: Collect Key-Value Data =====
    function collectKVData(containerId) {
        var container = document.getElementById(containerId);
        var rows = container.querySelectorAll('.kv-row');
        var data = {};
        rows.forEach(function(row) {
            var key = row.querySelector('.kv-key').value.trim();
            var value = row.querySelector('.kv-value').value.trim();
            if (key && value) {
                data[key] = value;
            }
        });
        return data;
    }

    // ===== Helper: Update Key-Value Editor =====
    function updateKVEditor(containerId, data) {
        var container = document.getElementById(containerId);
        if (!container) return;
        
        container.innerHTML = '';
        Object.keys(data).forEach(function(key) {
            var row = document.createElement('div');
            row.className = 'kv-row mb-2';
            row.innerHTML = '<input type="text" class="form-control form-control-sm kv-key mb-1" placeholder="Key" value="' + window.escapeHtml(key) + '" />' +
                '<input type="text" class="form-control form-control-sm kv-value" placeholder="Value" value="' + window.escapeHtml(data[key]) + '" />' +
                '<button type="button" class="btn btn-sm btn-outline-danger mt-1" onclick="this.parentElement.remove()"><i class="bi bi-trash"></i></button>';
            container.appendChild(row);
        });
    }

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
