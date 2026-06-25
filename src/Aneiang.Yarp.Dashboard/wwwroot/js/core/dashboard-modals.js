/**
 * Dashboard Modals - Unified modal and notification system
 * Provides: showToast, showConfirm, showFormModal, showJsonModal
 */
(function() {
    'use strict';

    window.DashboardModals = window.DashboardModals || {};

    let toastContainer = null;

    function ensureToastContainer() {
        if (!toastContainer) {
            toastContainer = document.createElement('div');
            toastContainer.id = 'toast-container';
            toastContainer.style.cssText = 'position:fixed;top:20px;right:20px;z-index:9999;max-width:400px;';
            document.body.appendChild(toastContainer);
        }
        return toastContainer;
    }

    /**
     * Show toast notification
     * @param {string} message - Message to display
     * @param {string} type - 'success'|'error'|'warning'|'info'
     * @param {number} duration - Duration in ms (default 4000)
     */
    window.DashboardModals.showToast = function(message, type, duration) {
        type = type || 'info';
        duration = duration || 4000;

        const container = ensureToastContainer();

        const typeConfig = {
            success: { bg: '#10b981', icon: 'bi-check-circle-fill' },
            error: { bg: '#ef4444', icon: 'bi-x-circle-fill' },
            warning: { bg: '#f59e0b', icon: 'bi-exclamation-triangle-fill' },
            info: { bg: '#6366f1', icon: 'bi-info-circle-fill' }
        };

        const config = typeConfig[type] || typeConfig.info;

        const toast = document.createElement('div');
        toast.className = 'toast-notification';
        toast.style.cssText = `
            background:${config.bg};
            color:#fff;
            padding:12px 20px;
            border-radius:8px;
            margin-bottom:8px;
            display:flex;
            align-items:center;
            gap:10px;
            box-shadow:0 4px 12px rgba(0,0,0,0.15);
            animation:slideIn 0.3s ease;
            font-size:14px;
            font-weight:500;
        `;

        toast.innerHTML = `
            <i class="bi ${config.icon}" style="font-size:18px;"></i>
            <span style="flex:1;">${window.DashboardUtils?.escapeHtml?.(message) || message}</span>
            <button onclick="this.parentElement.remove()" class="toast-close-btn">
                <i class="bi bi-x"></i>
            </button>
        `;

        container.appendChild(toast);

        // Auto remove
        setTimeout(function() {
            if (toast.parentElement) {
                toast.style.animation = 'slideOut 0.3s ease forwards';
                setTimeout(function() { toast.remove(); }, 300);
            }
        }, duration);

        return toast;
    };

    // Convenience methods
    window.DashboardModals.showSuccess = function(message) {
        return this.showToast(message, 'success');
    };

    window.DashboardModals.showError = function(message) {
        return this.showToast(message, 'error', 6000);
    };

    window.DashboardModals.showWarning = function(message) {
        return this.showToast(message, 'warning', 5000);
    };

    window.DashboardModals.showInfo = function(message) {
        return this.showToast(message, 'info');
    };

    let confirmModalId = 'dashboard-confirm-modal';

    /**
     * Show confirm dialog
     * @param {string} message - Message to display
     * @param {function} onConfirm - Callback when confirmed
     * @param {function} onCancel - Callback when cancelled
     * @param {object} options - Additional options {title, confirmText, cancelText, danger}
     */
    window.DashboardModals.showConfirm = function(message, onConfirm, onCancel, options) {
        options = options || {};
        const title = options.title || (window.__('modal.confirm'));
        const confirmText = options.confirmText || (window.__('modal.confirmBtn'));
        const cancelText = options.cancelText || (window.__('modal.cancelBtn'));
        const danger = options.danger || false;

        // Remove existing modal
        const existing = document.getElementById(confirmModalId);
        if (existing) existing.remove();

        const modalHtml = `
            <div class="modal fade" id="${confirmModalId}" tabindex="-1" data-bs-backdrop="static" data-bs-keyboard="false">
                <div class="modal-dialog modal-dialog-centered">
                    <div class="modal-content" style="border-radius:16px;border:none;box-shadow:0 25px 50px rgba(0,0,0,0.25);overflow:hidden;">
                        <div class="modal-header" style="background:linear-gradient(135deg,#f8fafc 0%,#e2e8f0 100%);border-bottom:1px solid #e2e8f0;padding:18px 24px;">
                            <h5 class="modal-title" style="font-weight:600;font-size:16px;display:flex;align-items:center;gap:10px;">
                                <span style="display:inline-flex;align-items:center;justify-content:center;width:32px;height:32px;border-radius:8px;background:${danger ? 'linear-gradient(135deg,#ef4444,#f87171)' : 'linear-gradient(135deg,#6366f1,#818cf8)'};color:#fff;font-size:16px;">
                                    <i class="bi ${danger ? 'bi-exclamation-triangle' : 'bi-question-circle'}"></i>
                                </span>
                                ${title}
                            </h5>
                            <button type="button" class="btn-close" data-bs-dismiss="modal"></button>
                        </div>
                        <div class="modal-body" style="padding:24px;font-size:14px;line-height:1.7;color:#334155;">
                            ${window.DashboardUtils?.escapeHtml?.(message) || message}
                        </div>
                        <div class="modal-footer" style="background:#f8fafc;border-top:1px solid #e2e8f0;padding:14px 24px;gap:8px;">
                            <button type="button" class="btn btn-secondary btn-sm" data-bs-dismiss="modal">
                                ${cancelText}
                            </button>
                            <button type="button" class="btn ${danger ? 'btn-danger' : 'btn-primary'} btn-sm" id="confirm-btn">
                                ${confirmText}
                            </button>
                        </div>
                    </div>
                </div>
            </div>
        `;

        document.body.insertAdjacentHTML('beforeend', modalHtml);

        const modalEl = document.getElementById(confirmModalId);
        const bsModal = new bootstrap.Modal(modalEl, { backdrop: 'static', keyboard: false });

        // Confirm button handler
        document.getElementById('confirm-btn').addEventListener('click', function() {
            bsModal.hide();
            if (onConfirm) onConfirm();
        });

        // Cancel handler
        modalEl.addEventListener('hidden.bs.modal', function() {
            if (onCancel && !modalEl._confirmed) onCancel();
            modalEl.remove();
        });

        bsModal.show();
        return bsModal;
    };

    /**
     * Show form modal for add/edit
     * @param {object} config - {title, fields, data, onSave, onCancel, size}
     * fields: [{name, label, type, required, placeholder, options, value}]
     */
    window.DashboardModals.showFormModal = function(config) {
        const modalId = 'dashboard-form-modal-' + Date.now();
        const title = config.title || (window.__('modal.edit'));
        const size = config.size || 'lg';
        const fields = config.fields || [];
        const data = config.data || {};

        // Build form HTML
        let formHtml = '';
        fields.forEach(function(field) {
            const required = field.required ? ' required' : '';
            const requiredMark = field.required ? '<span class="text-danger ms-1">*</span>' : '';
            const value = data[field.name] || field.value || '';

            if (field.type === 'select') {
                let optionsHtml = '';
                (field.options || []).forEach(function(opt) {
                    const optValue = typeof opt === 'object' ? opt.value : opt;
                    const optLabel = typeof opt === 'object' ? opt.label : opt;
                    const selected = value === optValue ? ' selected' : '';
                    optionsHtml += `<option value="${optValue}"${selected}>${optLabel}</option>`;
                });
                formHtml += `
                    <div class="mb-3">
                        <label class="form-label" style="font-weight:500;font-size:13px;">${field.label}${requiredMark}</label>
                        <select class="form-select" name="${field.name}"${required} style="border-radius:8px;">
                            ${optionsHtml}
                        </select>
                    </div>
                `;
            } else if (field.type === 'textarea') {
                formHtml += `
                    <div class="mb-3">
                        <label class="form-label" style="font-weight:500;font-size:13px;">${field.label}${requiredMark}</label>
                        <textarea class="form-control" name="${field.name}" rows="${field.rows || 3}"${required}
                            placeholder="${field.placeholder || ''}" style="border-radius:8px;">${value}</textarea>
                    </div>
                `;
            } else if (field.type === 'checkbox') {
                const checked = value ? ' checked' : '';
                formHtml += `
                    <div class="mb-3 form-check">
                        <input type="checkbox" class="form-check-input" name="${field.name}" id="${field.name}"${checked}>
                        <label class="form-check-label" for="${field.name}">${field.label}</label>
                    </div>
                `;
            } else if (field.type === 'number') {
                formHtml += `
                    <div class="mb-3">
                        <label class="form-label" style="font-weight:500;font-size:13px;">${field.label}${requiredMark}</label>
                        <input type="number" class="form-control" name="${field.name}" value="${value}"${required}
                            placeholder="${field.placeholder || ''}" style="border-radius:8px;"
                            ${field.min ? 'min="' + field.min + '"' : ''} ${field.max ? 'max="' + field.max + '"' : ''}>
                    </div>
                `;
            } else {
                // Default: text input
                formHtml += `
                    <div class="mb-3">
                        <label class="form-label" style="font-weight:500;font-size:13px;">${field.label}${requiredMark}</label>
                        <input type="${field.type || 'text'}" class="form-control" name="${field.name}" value="${value}"${required}
                            placeholder="${field.placeholder || ''}" style="border-radius:8px;">
                    </div>
                `;
            }
        });

        // Build modal
        const modalHtml = `
            <div class="modal fade" id="${modalId}" tabindex="-1" data-bs-backdrop="static" data-bs-keyboard="false">
                <div class="modal-dialog modal-dialog-centered modal-${size}">
                    <div class="modal-content" style="border-radius:16px;border:none;box-shadow:0 25px 50px rgba(0,0,0,0.25);overflow:hidden;">
                        <div class="modal-header" style="background:linear-gradient(135deg,#f8fafc 0%,#e2e8f0 100%);border-bottom:1px solid #e2e8f0;padding:18px 24px;">
                            <h5 class="modal-title" style="font-weight:600;font-size:16px;display:flex;align-items:center;gap:10px;">
                                <span style="display:inline-flex;align-items:center;justify-content:center;width:32px;height:32px;border-radius:8px;background:linear-gradient(135deg,#6366f1,#818cf8);color:#fff;font-size:16px;">
                                    <i class="bi ${config.icon || 'bi-pencil'}"></i>
                                </span>
                                ${title}
                            </h5>
                            <button type="button" class="btn-close" data-bs-dismiss="modal"></button>
                        </div>
                        <div class="modal-body" style="padding:24px;">
                            <form id="${modalId}-form">
                                ${formHtml}
                            </form>
                        </div>
                        <div class="modal-footer" style="background:#f8fafc;border-top:1px solid #e2e8f0;padding:14px 24px;gap:8px;justify-content:space-between;">
                            <div>
                                ${config.jsonModeCallback ? `<button type="button" class="btn btn-outline-secondary btn-sm" id="${modalId}-json-switch" style="font-size:12px;"><i class="bi bi-braces me-1"></i>${window.__('modal.jsonMode') || 'JSON'}</button>` : ''}
                            </div>
                            <div style="display:flex;gap:8px;">
                                <button type="button" class="btn btn-secondary btn-sm" data-bs-dismiss="modal">
                                    ${window.__('modal.cancelBtn')}
                                </button>
                                <button type="button" class="btn btn-primary btn-sm" id="${modalId}-save">
                                    <i class="bi bi-check-lg me-1"></i>${window.__('modal.saveBtn')}
                                </button>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        `;

        document.body.insertAdjacentHTML('beforeend', modalHtml);

        const modalEl = document.getElementById(modalId);
        const bsModal = new bootstrap.Modal(modalEl, { backdrop: 'static', keyboard: false });

        // Save button handler
        const saveBtn = document.getElementById(modalId + '-save');
        saveBtn.addEventListener('click', async function() {
            const form = document.getElementById(modalId + '-form');
            const formData = new FormData(form);
            const result = {};

            formData.forEach(function(value, key) {
                // Handle multiple values (arrays)
                if (result[key]) {
                    if (Array.isArray(result[key])) {
                        result[key].push(value);
                    } else {
                        result[key] = [result[key], value];
                    }
                } else {
                    result[key] = value;
                }
            });

            // Handle checkboxes
            fields.forEach(function(field) {
                if (field.type === 'checkbox') {
                    result[field.name] = form.querySelector('[name="' + field.name + '"]')?.checked || false;
                }
            });

            // Validate required fields
            let valid = true;
            fields.forEach(function(field) {
                if (field.required) {
                    const input = form.querySelector('[name="' + field.name + '"]');
                    if (!input.value.trim()) {
                        valid = false;
                        input.classList.add('is-invalid');
                    } else {
                        input.classList.remove('is-invalid');
                    }
                }
            });

            if (!valid) {
                DashboardModals.showWarning(window.__('modal.validationError'));
                return;
            }

            if (config.onSave) {
                // Show loading state while the (possibly async) save runs.
                if (window.DashboardLoading) window.DashboardLoading.setButton(saveBtn, true, '保存中...');
                try {
                    const saveResult = config.onSave(result);
                    const resolved = saveResult && typeof saveResult.then === 'function' ? await saveResult : saveResult;
                    if (resolved !== false) {
                        bsModal.hide();
                    }
                } catch (e) {
                    console.error('[FormModal] onSave threw:', e);
                } finally {
                    if (window.DashboardLoading) window.DashboardLoading.setButton(saveBtn, false);
                }
            } else {
                bsModal.hide();
            }
        });

        // JSON mode switch handler
        if (config.jsonModeCallback) {
            const jsonSwitchBtn = document.getElementById(modalId + '-json-switch');
            if (jsonSwitchBtn) {
                jsonSwitchBtn.addEventListener('click', function(e) {
                    e.preventDefault();
                    bsModal.hide();
                    config.jsonModeCallback();
                });
            }
        }

        // Remove modal on hide
        modalEl.addEventListener('hidden.bs.modal', function() {
            modalEl.remove();
        });

        bsModal.show();
        return bsModal;
    };

    const schemaCache = {
        cluster: null,
        route: null,
        full: null
    };
    
    /**
     * Register a specific schema for the current editor
     * This is called each time a modal opens with a schemaType
     */
    function registerSchemaForType(schemaType, schema) {
        if (typeof monaco === 'undefined' || !monaco.languages || !monaco.languages.json) {
            console.warn('[Modals] Monaco not available for schema registration');
            return;
        }
        
        const schemaUri = 'http://aneiang.yarp/schema/' + schemaType;
        
        // Only register the specific schema for this editor
        monaco.languages.json.jsonDefaults.setDiagnosticsOptions({
            validate: true,
            allowComments: true,
            trailingCommas: 'ignore',
            schemas: [{
                uri: schemaUri,
                fileMatch: ['*'],
                schema: schema
            }]
        });
    }

    window.DashboardModals.loadSchema = function(type) {
        const self = this;
        
        if (schemaCache[type]) {
            return Promise.resolve(schemaCache[type]);
        }

        const schemaPaths = {
            cluster: '/_content/Aneiang.Yarp.Dashboard/ClusterSchema.json',
            route: '/_content/Aneiang.Yarp.Dashboard/RouteSchema.json',
            full: '/_content/Aneiang.Yarp.Dashboard/ConfigurationSchema.json'
        }; 

        const path = schemaPaths[type] || schemaPaths.full;

        return new Promise(function(resolve, reject) {
            const xhr = new XMLHttpRequest();
            xhr.open('GET', path, true);
            xhr.onreadystatechange = function() {
                if (xhr.readyState === 4) {
                    if (xhr.status === 200) {
                        try {
                            const schema = JSON.parse(xhr.responseText);
                            schemaCache[type] = schema;
                            resolve(schema);
                        } catch (e) {
                            console.error('[Modals] Schema parse failed:', e);
                            reject(e);
                        }
                    } else {
                        console.error('[Modals] Schema load failed:', xhr.status);
                        reject(new Error('Failed to load schema: ' + xhr.status));
                    }
                }
            };
            xhr.onerror = function() {
                reject(new Error('Network error loading schema'));
            };
            xhr.send();
        });
    };

    /**
     * Show JSON editor modal with schema validation
     * @param {object} config - {title, data, onSave, readOnly, schemaType, schemaUri}
     * schemaType: 'cluster' | 'route' | 'full' or null for no schema
     */
    window.DashboardModals.showJsonModal = function(config) {
        const modalId = 'dashboard-json-modal-' + Date.now();
        const title = config.title || (window.__('modal.jsonEdit'));
        const readOnly = config.readOnly || false;
        const data = config.data || {};
        const schemaType = config.schemaType || null;
        const editableId = config.editableId || null;

        let jsonContent = '';
        try {
            jsonContent = JSON.stringify(data, null, 2);
        } catch (e) {
            jsonContent = '{}';
        }

        // Build editable ID input HTML if provided. ID fields can be locked to prevent accidental rename.
        const idReadOnly = editableId && editableId.readOnly === true;
        const idInputHtml = editableId ? `
            <div style="padding:14px 24px 0 24px;background:#fff;">
                <div style="display:flex;align-items:center;gap:10px;">
                    <label style="font-weight:500;font-size:13px;color:#334155;white-space:nowrap;min-width:60px;">${editableId.label || 'ID'}</label>
                    <input type="text" class="form-control" id="${modalId}-id-input"
                           value="${editableId.value || ''}"
                           placeholder="${editableId.placeholder || ''}"
                           ${idReadOnly ? 'readonly aria-readonly="true"' : ''}
                           style="border-radius:8px;padding:8px 12px;font-size:14px;border:1.5px solid #e2e8f0;transition:border-color 0.2s,box-shadow 0.2s;${idReadOnly ? 'background:#f8fafc;color:#64748b;cursor:not-allowed;' : ''}"
                           onfocus="this.style.borderColor='#6366f1';this.style.boxShadow='0 0 0 3px rgba(99,102,241,0.1)'"
                           onblur="this.style.borderColor='#e2e8f0';this.style.boxShadow='none'">
                    ${idReadOnly ? `<span style="font-size:12px;color:#64748b;white-space:nowrap;"><i class="bi bi-lock"></i> ID 修改请使用专用重命名功能</span>` : (editableId.original ? `<span style="font-size:12px;color:#94a3b8;white-space:nowrap;">${window.__('modal.renameHint')}</span>` : '')}
                </div>
            </div>
        ` : '';

        const modalHtml = `
            <div class="modal fade" id="${modalId}" tabindex="-1" data-bs-backdrop="static" data-bs-keyboard="false">
                <div class="modal-dialog modal-dialog-centered modal-xl">
                    <div class="modal-content" style="border-radius:16px;border:none;box-shadow:0 25px 50px rgba(0,0,0,0.25);overflow:hidden;">
                        <div class="modal-header" style="background:linear-gradient(135deg,#f8fafc 0%,#e2e8f0 100%);border-bottom:1px solid #e2e8f0;padding:18px 24px;">
                            <h5 class="modal-title" style="font-weight:600;font-size:16px;display:flex;align-items:center;gap:10px;">
                                <span style="display:inline-flex;align-items:center;justify-content:center;width:32px;height:32px;border-radius:8px;background:linear-gradient(135deg,#6366f1,#818cf8);color:#fff;font-size:16px;">
                                    <i class="bi bi-file-code"></i>
                                </span>
                                ${title}
                            </h5>
                            <button type="button" class="btn-close" data-bs-dismiss="modal"></button>
                        </div>
                        <div class="modal-body" style="padding:0;">
                            ${idInputHtml}
                            ${config.hint ? `<div style="margin:10px 24px 0 24px;padding:10px 14px;background:#f0f9ff;border:1px solid #bae6fd;border-radius:8px;font-size:12px;color:#0369a1;display:flex;align-items:flex-start;gap:8px;"><i class="bi bi-info-circle" style="font-size:14px;margin-top:1px;flex-shrink:0;"></i><span>${config.hint}</span></div>` : ''}
                            <div id="${modalId}-editor" style="height:${editableId ? (config.hint ? '430' : '460') : (config.hint ? '470' : '500')}px;border:none;"></div>
                        </div>
                        <div class="modal-footer" style="background:#f8fafc;border-top:1px solid #e2e8f0;padding:14px 24px;gap:8px;">
                            <div style="flex:1;display:flex;align-items:center;gap:8px;">
                                <button type="button" class="btn btn-outline-secondary btn-sm" onclick="DashboardModals.formatJson('${modalId}')" title="${window.__('modal.format')}">
                                    <i class="bi bi-code-slash me-1"></i>${window.__('modal.format')}
                                </button>
                                <button type="button" class="btn btn-outline-secondary btn-sm" onclick="DashboardModals.copyJsonFromEditor('${modalId}')" title="${window.__('modal.copy')}">
                                    <i class="bi bi-clipboard me-1"></i>${window.__('modal.copy')}
                                </button>
                            </div>
                            <div style="display:flex;align-items:center;gap:8px;">
                                ${readOnly ? '' : `
                                <button type="button" class="btn btn-outline-warning btn-sm" onclick="DashboardModals.validateJson('${modalId}')">
                                    <i class="bi bi-check-circle me-1"></i>${window.__('modal.validate')}
                                </button>
                                `}
                                <button type="button" class="btn btn-secondary btn-sm" data-bs-dismiss="modal">
                                    ${window.__('modal.cancelBtn')}
                                </button>
                                ${readOnly ? '' : `
                                <button type="button" class="btn btn-primary btn-sm" id="${modalId}-save">
                                    <i class="bi bi-check-lg me-1"></i>${window.__('modal.saveBtn')}
                                </button>
                                `}
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        `;

        document.body.insertAdjacentHTML('beforeend', modalHtml);

        const modalEl = document.getElementById(modalId);
        const bsModal = new bootstrap.Modal(modalEl, { backdrop: 'static', keyboard: false });

        // Initialize Monaco Editor with Schema
        let editor = null;
        const self = this;

        // Load schema for this specific type
        let schemaPromise = schemaType ? this.loadSchema(schemaType) : Promise.resolve(null);

        // Wait for Monaco and Schema to be ready (uses lazy loader if available)
        var monacoReadyPromise = window.LazyMonacoLoader
            ? window.LazyMonacoLoader.ensure()
            : (window.__monacoReady || Promise.resolve());
        Promise.all([monacoReadyPromise, schemaPromise]).then(function(results) {
            const monacoReady = typeof monaco !== 'undefined' && monaco.editor;
            const schema = results[1];

            if (monacoReady && window.DashboardMonacoEditor) {
                // Register the specific schema before creating editor
                if (schema) {
                    registerSchemaForType(schemaType, schema);
                }

                const editorOptions = {
                    value: jsonContent,
                    language: 'json',
                    readOnly: readOnly,
                    theme: 'vs-light',
                    minimap: { enabled: false },
                    scrollBeyondLastLine: false,
                    automaticLayout: true,
                    tabSize: 2,
                    formatOnPaste: true,
                    formatOnType: true,
                    wordWrap: 'on',
                    suggestOnTriggerCharacters: true,
                    quickSuggestions: { other: true, comments: 'off', strings: 'on' }
                }; 

                return window.DashboardMonacoEditor.init(modalId + '-editor', editorOptions).then(function(editorInstance) {
                    editor = editorInstance;
                });
            } else {
                // Fallback to textarea
                console.warn('[Modals] Monaco not available, using textarea fallback');
                const editorContainer = document.getElementById(modalId + '-editor');
                editorContainer.innerHTML = `
                    <textarea class="form-control" id="${modalId}-textarea" style="width:100%;height:100%;border:none;font-family:monospace;font-size:13px;padding:16px;resize:none;background:#f8f9fa;"
                        ${readOnly ? 'readonly' : ''}>${jsonContent}</textarea>
                `;
                return Promise.resolve();
            }
        }).catch(function(err) {
            console.warn('[Modals] Monaco/Schema init failed:', err);
            // Fallback to textarea
            const editorContainer = document.getElementById(modalId + '-editor');
            if (editorContainer) {
                editorContainer.innerHTML = `
                    <textarea class="form-control" id="${modalId}-textarea" style="width:100%;height:100%;border:none;font-family:monospace;font-size:13px;padding:16px;resize:none;background:#f8f9fa;"
                        ${readOnly ? 'readonly' : ''}>${jsonContent}</textarea>
                `;
            }
        });

        // Save button handler
        if (!readOnly) {
            const saveBtn = document.getElementById(modalId + '-save');
            saveBtn.addEventListener('click', async function() {
                let newValue;
                if (editor && window.DashboardMonacoEditor) {
                    newValue = window.DashboardMonacoEditor.getValue(modalId + '-editor');
                } else {
                    newValue = document.getElementById(modalId + '-textarea')?.value || '';
                }

                // Validate JSON (tolerates comments and trailing commas)
                try {
                    const parsed = window.DashboardUtils.parseJsonLenient(newValue);

                    // Get editable ID value if present
                    let newId = null;
                    if (editableId) {
                        const idInput = document.getElementById(modalId + '-id-input');
                        newId = idInput ? idInput.value.trim() : null;
                        if (editableId && !newId) {
                            DashboardModals.showError(window.__('modal.idRequired'));
                            return;
                        }
                    }

                    if (config.onSave) {
                        // Show loading state on the save button while the async save runs.
                        // The onSave callback may be sync (return boolean) or async (return Promise<boolean>).
                        if (window.DashboardLoading) window.DashboardLoading.setButton(saveBtn, true, '保存中...');
                        try {
                            const saveResult = config.onSave(parsed, newId);
                            const resolved = saveResult && typeof saveResult.then === 'function' ? await saveResult : saveResult;
                            if (resolved !== false) {
                                bsModal.hide();
                            }
                        } catch (e) {
                            // onSave threw - leave modal open so user can fix and retry
                            console.error('[JsonModal] onSave threw:', e);
                        } finally {
                            if (window.DashboardLoading) window.DashboardLoading.setButton(saveBtn, false);
                        }
                    } else {
                        bsModal.hide();
                    }
                } catch (e) {
                    DashboardModals.showError(window.__('modal.invalidJson') + e.message);
                }
            });
        }

        // Remove modal on hide
        modalEl.addEventListener('hidden.bs.modal', function() {
            modalEl.remove();
        });

        bsModal.show();
        return bsModal;
    };

    window.DashboardModals.copyJsonFromEditor = function(modalId) {
        let value;
        if (window.DashboardMonacoEditor) {
            value = window.DashboardMonacoEditor.getValue(modalId + '-editor');
        } else {
            value = document.getElementById(modalId + '-textarea')?.value || '';
        }

        window.DashboardUtils.copyToClipboard(value).then(function(success) {
            if (success) {
                DashboardModals.showSuccess(window.__('modal.copied'));
            } else {
                DashboardModals.showError(window.__('modal.copyFailed'));
            }
        });
    };

    window.DashboardModals.formatJson = function(modalId) {
        let value;
        const editorContainerId = modalId + '-editor';
        if (window.DashboardMonacoEditor && window.DashboardMonacoEditor.instances.has(editorContainerId)) {
            value = window.DashboardMonacoEditor.getValue(editorContainerId);
        } else {
            const textarea = document.getElementById(modalId + '-textarea');
            value = textarea ? textarea.value : '';
        }

        try {
            const parsed = window.DashboardUtils.parseJsonLenient(value);
            const formatted = JSON.stringify(parsed, null, 2);
            if (window.DashboardMonacoEditor && window.DashboardMonacoEditor.instances.has(editorContainerId)) {
                window.DashboardMonacoEditor.setValue(editorContainerId, formatted);
            } else {
                const textarea = document.getElementById(modalId + '-textarea');
                if (textarea) textarea.value = formatted;
            }
            DashboardModals.showSuccess(window.__('modal.formatted'));
        } catch (e) {
            DashboardModals.showError(window.__('modal.invalidJson') + e.message);
        }
    };

    window.DashboardModals.validateJson = function(modalId) {
        let value;
        if (window.DashboardMonacoEditor) {
            value = window.DashboardMonacoEditor.getValue(modalId + '-editor');
        } else {
            value = document.getElementById(modalId + '-textarea')?.value || '';
        }

        try {
            window.DashboardUtils.parseJsonLenient(value);
            DashboardModals.showSuccess(window.__('modal.validJson'));
        } catch (e) {
            DashboardModals.showError(window.__('modal.invalidJson') + e.message);
        }
    };

    const style = document.createElement('style');
    style.textContent = `
        @keyframes slideIn {
            from { transform: translateX(100%); opacity: 0; }
            to { transform: translateX(0); opacity: 1; }
        }
        @keyframes slideOut {
            from { transform: translateX(0); opacity: 1; }
            to { transform: translateX(100%); opacity: 0; }
        }
        .form-control:focus, .form-select:focus {
            border-color: #6366f1;
            box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.1);
        }
        .is-invalid {
            border-color: #ef4444 !important;
        }
    `;
    document.head.appendChild(style);

    window.DashboardModals.init = function() {};

    window.showToast = window.DashboardModals.showToast;
    window.showConfirm = window.DashboardModals.showConfirm;
    window.showSuccess = window.DashboardModals.showSuccess;
    window.showError = window.DashboardModals.showError;

})();