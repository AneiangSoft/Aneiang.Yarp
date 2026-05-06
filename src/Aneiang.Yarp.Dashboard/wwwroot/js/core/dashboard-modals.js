/**
 * Dashboard Modals - Unified modal and notification system
 * Provides: showToast, showConfirm, showFormModal, showJsonModal
 */
(function() {
    'use strict';

    window.DashboardModals = window.DashboardModals || {};

    // ===== Toast Container =====
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
            <button onclick="this.parentElement.remove()" style="background:none;border:none;color:#fff;cursor:pointer;padding:0;margin-left:8px;">
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

    // ===== Confirm Modal =====
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
        const title = options.title || (window.__('modal.confirm') || '确认');
        const confirmText = options.confirmText || (window.__('modal.confirmBtn') || '确认');
        const cancelText = options.cancelText || (window.__('modal.cancelBtn') || '取消');
        const danger = options.danger || false;

        // Remove existing modal
        const existing = document.getElementById(confirmModalId);
        if (existing) existing.remove();

        const modalHtml = `
            <div class="modal fade" id="${confirmModalId}" tabindex="-1">
                <div class="modal-dialog modal-dialog-centered">
                    <div class="modal-content" style="border-radius:12px;border:none;box-shadow:0 20px 40px rgba(0,0,0,0.2);">
                        <div class="modal-header" style="border-bottom:1px solid #e2e8f0;padding:16px 20px;">
                            <h5 class="modal-title" style="font-weight:600;font-size:16px;">
                                <i class="bi ${danger ? 'bi-exclamation-triangle text-danger' : 'bi-question-circle text-primary'} me-2"></i>
                                ${title}
                            </h5>
                            <button type="button" class="btn-close" data-bs-dismiss="modal"></button>
                        </div>
                        <div class="modal-body" style="padding:20px;font-size:14px;">
                            ${window.DashboardUtils?.escapeHtml?.(message) || message}
                        </div>
                        <div class="modal-footer" style="border-top:1px solid #e2e8f0;padding:16px 20px;">
                            <button type="button" class="btn btn-secondary" data-bs-dismiss="modal" style="padding:8px 16px;">
                                ${cancelText}
                            </button>
                            <button type="button" class="btn ${danger ? 'btn-danger' : 'btn-primary'}" id="confirm-btn" style="padding:8px 16px;">
                                ${confirmText}
                            </button>
                        </div>
                    </div>
                </div>
            </div>
        `;

        document.body.insertAdjacentHTML('beforeend', modalHtml);

        const modalEl = document.getElementById(confirmModalId);
        const bsModal = new bootstrap.Modal(modalEl);

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

    // ===== Form Modal =====
    /**
     * Show form modal for add/edit
     * @param {object} config - {title, fields, data, onSave, onCancel, size}
     * fields: [{name, label, type, required, placeholder, options, value}]
     */
    window.DashboardModals.showFormModal = function(config) {
        const modalId = 'dashboard-form-modal-' + Date.now();
        const title = config.title || (window.__('modal.edit') || '编辑');
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
            <div class="modal fade" id="${modalId}" tabindex="-1">
                <div class="modal-dialog modal-dialog-centered modal-${size}">
                    <div class="modal-content" style="border-radius:12px;border:none;box-shadow:0 20px 40px rgba(0,0,0,0.2);">
                        <div class="modal-header" style="border-bottom:1px solid #e2e8f0;padding:16px 20px;">
                            <h5 class="modal-title" style="font-weight:600;font-size:16px;display:flex;align-items:center;gap:8px;">
                                <i class="bi ${config.icon || 'bi-pencil'} text-primary"></i>
                                ${title}
                            </h5>
                            <button type="button" class="btn-close" data-bs-dismiss="modal"></button>
                        </div>
                        <div class="modal-body" style="padding:20px;">
                            <form id="${modalId}-form">
                                ${formHtml}
                            </form>
                        </div>
                        <div class="modal-footer" style="border-top:1px solid #e2e8f0;padding:16px 20px;">
                            <button type="button" class="btn btn-secondary" data-bs-dismiss="modal" style="padding:8px 16px;">
                                ${window.__('modal.cancelBtn') || '取消'}
                            </button>
                            <button type="button" class="btn btn-primary" id="${modalId}-save" style="padding:8px 20px;">
                                <i class="bi bi-check-lg me-1"></i>${window.__('modal.saveBtn') || '保存'}
                            </button>
                        </div>
                    </div>
                </div>
            </div>
        `;

        document.body.insertAdjacentHTML('beforeend', modalHtml);

        const modalEl = document.getElementById(modalId);
        const bsModal = new bootstrap.Modal(modalEl);

        // Save button handler
        document.getElementById(modalId + '-save').addEventListener('click', function() {
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
                DashboardModals.showWarning(window.__('modal.validationError') || '请填写所有必填字段');
                return;
            }

            if (config.onSave) {
                const saveResult = config.onSave(result);
                if (saveResult !== false) {
                    bsModal.hide();
                }
            } else {
                bsModal.hide();
            }
        });

        // Remove modal on hide
        modalEl.addEventListener('hidden.bs.modal', function() {
            modalEl.remove();
        });

        bsModal.show();
        return bsModal;
    };

    // ===== Schema Cache =====
    const schemaCache = {
        cluster: null,
        route: null,
        full: null
    };

    // ===== Load Schema =====
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
                            console.log('[Modals] Schema loaded:', type);
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

    // ===== JSON Modal =====
    /**
     * Show JSON editor modal with schema validation
     * @param {object} config - {title, data, onSave, readOnly, schemaType, schemaUri}
     * schemaType: 'cluster' | 'route' | 'full' or null for no schema
     */
    window.DashboardModals.showJsonModal = function(config) {
        const modalId = 'dashboard-json-modal-' + Date.now();
        const title = config.title || (window.__('modal.jsonEdit') || 'JSON编辑');
        const readOnly = config.readOnly || false;
        const data = config.data || {};
        const schemaType = config.schemaType || null;

        let jsonContent = '';
        try {
            jsonContent = JSON.stringify(data, null, 2);
        } catch (e) {
            jsonContent = '{}';
        }

        const modalHtml = `
            <div class="modal fade" id="${modalId}" tabindex="-1">
                <div class="modal-dialog modal-dialog-centered modal-xl">
                    <div class="modal-content" style="border-radius:12px;border:none;box-shadow:0 20px 40px rgba(0,0,0,0.2);">
                        <div class="modal-header" style="border-bottom:1px solid #e2e8f0;padding:16px 20px;">
                            <h5 class="modal-title" style="font-weight:600;font-size:16px;display:flex;align-items:center;gap:8px;">
                                <i class="bi bi-file-code text-primary"></i>
                                ${title}
                            </h5>
                            <button type="button" class="btn-close" data-bs-dismiss="modal"></button>
                        </div>
                        <div class="modal-body" style="padding:0;">
                            <div id="${modalId}-editor" style="height:500px;border:none;"></div>
                        </div>
                        <div class="modal-footer" style="border-top:1px solid #e2e8f0;padding:16px 20px;">
                            <button type="button" class="btn btn-outline-secondary" onclick="DashboardModals.copyJsonFromEditor('${modalId}')">
                                <i class="bi bi-copy me-1"></i>${window.__('modal.copy') || '复制'}
                            </button>
                            ${readOnly ? '' : `
                            <button type="button" class="btn btn-warning" onclick="DashboardModals.validateJson('${modalId}')">
                                <i class="bi bi-check-circle me-1"></i>${window.__('modal.validate') || '验证'}
                            </button>
                            `}
                            <button type="button" class="btn btn-secondary" data-bs-dismiss="modal" style="padding:8px 16px;">
                                ${window.__('modal.cancelBtn') || '取消'}
                            </button>
                            ${readOnly ? '' : `
                            <button type="button" class="btn btn-primary" id="${modalId}-save" style="padding:8px 20px;">
                                <i class="bi bi-check-lg me-1"></i>${window.__('modal.saveBtn') || '保存'}
                            </button>
                            `}
                        </div>
                    </div>
                </div>
            </div>
        `;

        document.body.insertAdjacentHTML('beforeend', modalHtml);

        const modalEl = document.getElementById(modalId);
        const bsModal = new bootstrap.Modal(modalEl);

        // Initialize Monaco Editor with Schema
        let editor = null;
        const self = this;

        // Schema promise
        let schemaPromise = schemaType ? this.loadSchema(schemaType) : Promise.resolve(null);

        // Wait for Monaco and Schema to be ready
        Promise.all([window.__monacoReady || Promise.resolve(), schemaPromise]).then(function(results) {
            const monacoReady = results[0] !== false;
            const schema = results[1];

            if (monacoReady && window.DashboardMonacoEditor) {
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
                    wordWrap: 'on'
                }; 

                editor = window.DashboardMonacoEditor.init(modalId + '-editor', editorOptions);

                // Register schema for validation and hints
                if (schema && typeof monaco !== 'undefined') {
                    try {
                        const schemaUri = config.schemaUri || ('http://aneiang.yarp/schema/' + schemaType);
                        monaco.languages.json.jsonDefaults.setDiagnosticsOptions({
                            validate: true,
                            allowComments: true,
                            schemas: [{
                                uri: schemaUri,
                                fileMatch: [modalId + '-editor'],
                                schema: schema
                            }]
                        });
                        console.log('[Modals] Schema registered for:', modalId);
                    } catch (e) {
                        console.warn('[Modals] Schema registration failed:', e);
                    }
                }
            } else {
                // Fallback to textarea
                const editorContainer = document.getElementById(modalId + '-editor');
                editorContainer.innerHTML = `
                    <textarea class="form-control" id="${modalId}-textarea" style="width:100%;height:100%;border:none;font-family:monospace;font-size:13px;padding:16px;resize:none;background:#f8f9fa;"
                        ${readOnly ? 'readonly' : ''}>${jsonContent}</textarea>
                `;
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
            document.getElementById(modalId + '-save').addEventListener('click', function() {
                let newValue;
                if (editor && window.DashboardMonacoEditor) {
                    newValue = window.DashboardMonacoEditor.getValue(modalId + '-editor');
                } else {
                    newValue = document.getElementById(modalId + '-textarea')?.value || '';
                }

                // Validate JSON
                try {
                    const parsed = JSON.parse(newValue);
                    if (config.onSave) {
                        const saveResult = config.onSave(parsed);
                        if (saveResult !== false) {
                            bsModal.hide();
                        }
                    } else {
                        bsModal.hide();
                    }
                } catch (e) {
                    DashboardModals.showError(window.__('modal.invalidJson') || 'JSON格式错误: ' + e.message);
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

    // ===== Helper: Copy JSON from Editor =====
    window.DashboardModals.copyJsonFromEditor = function(modalId) {
        let value;
        if (window.DashboardMonacoEditor) {
            value = window.DashboardMonacoEditor.getValue(modalId + '-editor');
        } else {
            value = document.getElementById(modalId + '-textarea')?.value || '';
        }

        if (window.DashboardUtils?.copyToClipboard) {
            window.DashboardUtils.copyToClipboard(value).then(function(success) {
                if (success) {
                    DashboardModals.showSuccess(window.__('modal.copied') || '已复制到剪贴板');
                } else {
                    DashboardModals.showError(window.__('modal.copyFailed') || '复制失败');
                }
            });
        } else {
            // Fallback
            navigator.clipboard.writeText(value).then(function() {
                DashboardModals.showSuccess(window.__('modal.copied') || '已复制到剪贴板');
            }).catch(function() {
                DashboardModals.showError(window.__('modal.copyFailed') || '复制失败');
            });
        }
    };

    // ===== Helper: Validate JSON =====
    window.DashboardModals.validateJson = function(modalId) {
        let value;
        if (window.DashboardMonacoEditor) {
            value = window.DashboardMonacoEditor.getValue(modalId + '-editor');
        } else {
            value = document.getElementById(modalId + '-textarea')?.value || '';
        }

        try {
            JSON.parse(value);
            DashboardModals.showSuccess(window.__('modal.validJson') || 'JSON格式有效');
        } catch (e) {
            DashboardModals.showError(window.__('modal.invalidJson') || 'JSON格式错误: ' + e.message);
        }
    };

    // ===== Add CSS animations =====
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

    // ===== Global shortcuts =====
    window.showToast = window.DashboardModals.showToast;
    window.showConfirm = window.DashboardModals.showConfirm;
    window.showSuccess = window.DashboardModals.showSuccess;
    window.showError = window.DashboardModals.showError;

})();