/**
 * Dashboard Config Module - Configuration import/export and history management
 */
(function() {
    'use strict';

    window.DashboardConfig = window.DashboardConfig || {};

    // ===== Export Configuration =====
    window.DashboardConfig.exportConfig = async function() {
        try {
            window.DashboardModals.showInfo(__('config.exporting'));

            const response = await window.DashboardApi.endpoints.exportConfig();

            // Create download
            const blob = new Blob(
                [JSON.stringify(response, null, 2)],
                { type: 'application/json' }
            );
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = 'yarp-config-' + new Date().toISOString().slice(0, 10) + '.json';
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            URL.revokeObjectURL(url);

            window.DashboardModals.showSuccess(__('config.exported'));
        } catch (error) {
            console.error('[Config] Export failed:', error);
            window.DashboardModals.showError(__('config.exportFailed') + error.message);
        }
    };

    // ===== Show Import Modal =====
    window.DashboardConfig.showImportModal = function() {
        const self = this;

        // Create import modal with file upload
        const modalId = 'dashboard-import-modal';
        const existing = document.getElementById(modalId);
        if (existing) existing.remove();

        const modalHtml = `
            <div class="modal fade" id="${modalId}" tabindex="-1">
                <div class="modal-dialog modal-dialog-centered modal-lg">
                    <div class="modal-content" style="border-radius:16px;border:none;box-shadow:0 25px 50px rgba(0,0,0,0.25);overflow:hidden;">
                        <div class="modal-header" style="background:linear-gradient(135deg,#f8fafc 0%,#e2e8f0 100%);border-bottom:1px solid #e2e8f0;padding:18px 24px;">
                            <h5 class="modal-title" style="font-weight:600;font-size:16px;display:flex;align-items:center;gap:10px;">
                                <span style="display:inline-flex;align-items:center;justify-content:center;width:32px;height:32px;border-radius:8px;background:linear-gradient(135deg,#6366f1,#818cf8);color:#fff;font-size:16px;">
                                    <i class="bi bi-upload"></i>
                                </span>
                                ${__('config.import')}
                            </h5>
                            <button type="button" class="btn-close" data-bs-dismiss="modal"></button>
                        </div>
                        <div class="modal-body" style="padding:24px;">
                            <div id="import-drop-zone" style="border:2px dashed #cbd5e1;border-radius:12px;padding:40px;text-align:center;background:#f8fafc;cursor:pointer;transition:all 0.2s ease;">
                                <i class="bi bi-file-earmark-arrow-up" style="font-size:48px;color:#6366f1;"></i>
                                <p style="margin-top:12px;color:#64748b;font-size:14px;">${__('config.dropFile')}</p>
                                <input type="file" id="import-file-input" accept=".json" style="display:none;">
                            </div>
                            <div id="import-preview" style="display:none;margin-top:16px;">
                                <h6 style="font-weight:600;margin-bottom:8px;">${__('config.selectFile')}: <span id="import-file-name"></span></h6>
                                <div id="import-json-preview" style="max-height:300px;overflow:auto;background:#1e293b;border-radius:8px;padding:12px;font-size:12px;color:#e2e8f0;font-family:monospace;"></div>
                            </div>
                            <div id="import-errors" style="display:none;margin-top:16px;">
                                <div class="alert alert-danger" style="border-radius:8px;"></div>
                            </div>
                        </div>
                        <div class="modal-footer" style="background:#f8fafc;border-top:1px solid #e2e8f0;padding:14px 24px;gap:8px;">
                            <button type="button" class="btn btn-secondary btn-sm" data-bs-dismiss="modal" style="min-width:70px;">
                                ${__('modal.cancelBtn')}
                            </button>
                            <button type="button" class="btn btn-primary btn-sm" id="import-btn" disabled style="min-width:80px;">
                                <i class="bi bi-upload me-1"></i>${__('config.import')}
                            </button>
                        </div>
                    </div>
                </div>
            </div>
        `;

        document.body.insertAdjacentHTML('beforeend', modalHtml);

        const modalEl = document.getElementById(modalId);
        const bsModal = new bootstrap.Modal(modalEl);
        let importData = null;

        // Setup file drop zone
        const dropZone = document.getElementById('import-drop-zone');
        const fileInput = document.getElementById('import-file-input');
        const preview = document.getElementById('import-preview');
        const previewContent = document.getElementById('import-json-preview');
        const fileName = document.getElementById('import-file-name');
        const errors = document.getElementById('import-errors');
        const importBtn = document.getElementById('import-btn');

        // Click to select file
        dropZone.addEventListener('click', function() {
            fileInput.click();
        });

        // Handle file selection
        fileInput.addEventListener('change', function(e) {
            if (e.target.files && e.target.files[0]) {
                handleFile(e.target.files[0]);
            }
        });

        // Drag and drop
        dropZone.addEventListener('dragover', function(e) {
            e.preventDefault();
            dropZone.style.borderColor = '#6366f1';
            dropZone.style.background = 'linear-gradient(135deg,#eff6ff,#f0f9ff)';
            dropZone.style.boxShadow = '0 0 0 3px rgba(99,102,241,0.1)';
        });

        dropZone.addEventListener('dragleave', function(e) {
            e.preventDefault();
            dropZone.style.borderColor = '#cbd5e1';
            dropZone.style.background = '#f8fafc';
            dropZone.style.boxShadow = 'none';
        });

        dropZone.addEventListener('drop', function(e) {
            e.preventDefault();
            dropZone.style.borderColor = '#cbd5e1';
            dropZone.style.background = '#f8fafc';
            dropZone.style.boxShadow = 'none';

            if (e.dataTransfer.files && e.dataTransfer.files[0]) {
                handleFile(e.dataTransfer.files[0]);
            }
        });

        // Handle file
        function handleFile(file) {
            if (!file.name.endsWith('.json')) {
                errors.style.display = 'block';
                errors.querySelector('.alert').textContent = __('config.selectJsonFile');
                return;
            }

            errors.style.display = 'none';

            const reader = new FileReader();
            reader.onload = function(e) {
                try {
                    importData = JSON.parse(e.target.result);

                    // Validate structure
                    if (!importData.ReverseProxy || !importData.ReverseProxy.Routes || !importData.ReverseProxy.Clusters) {
                        errors.style.display = 'block';
                        errors.querySelector('.alert').textContent = __('config.importInvalid');
                        importBtn.disabled = true;
                        return;
                    }

                    // Show preview
                    fileName.textContent = file.name;
                    previewContent.textContent = JSON.stringify(importData, null, 2).slice(0, 500) + '...';
                    preview.style.display = 'block';
                    importBtn.disabled = false;

                } catch (err) {
                    errors.style.display = 'block';
                    errors.querySelector('.alert').textContent = __('modal.invalidJson') + err.message;
                    importBtn.disabled = true;
                }
            };
            reader.readAsText(file);
        }

        // Import button
        importBtn.addEventListener('click', async function() {
            if (!importData) return;

            try {
                window.DashboardModals.showInfo(__('config.importing'));
                bsModal.hide();

                const response = await window.DashboardApi.endpoints.importConfig(importData);

                window.DashboardModals.showSuccess(__('config.imported'));

                // Reload page to reflect changes
                setTimeout(function() {
                    window.location.reload();
                }, 1500);

            } catch (error) {
                console.error('[Config] Import failed:', error);
                window.DashboardModals.showError(__('config.importFailed') + error.message);
            }
        });

        // Remove modal on hide
        modalEl.addEventListener('hidden.bs.modal', function() {
            modalEl.remove();
        });

        bsModal.show();
    };

    // ===== Show History Modal =====
    window.DashboardConfig.showHistoryModal = async function() {
        const self = this;

        const modalId = 'dashboard-history-modal';
        const existing = document.getElementById(modalId);
        if (existing) existing.remove();

        // Show modal with loading state first
        const loadingHtml = `
            <div class="modal fade" id="${modalId}" tabindex="-1">
                <div class="modal-dialog modal-dialog-centered modal-lg">
                    <div class="modal-content" style="border-radius:16px;border:none;box-shadow:0 25px 50px rgba(0,0,0,0.25);overflow:hidden;">
                        <div class="modal-header" style="background:linear-gradient(135deg,#f8fafc 0%,#e2e8f0 100%);border-bottom:1px solid #e2e8f0;padding:18px 24px;">
                            <h5 class="modal-title" style="font-weight:600;font-size:16px;display:flex;align-items:center;gap:10px;">
                                <span style="display:inline-flex;align-items:center;justify-content:center;width:32px;height:32px;border-radius:8px;background:linear-gradient(135deg,#6366f1,#818cf8);color:#fff;font-size:16px;">
                                    <i class="bi bi-clock-history"></i>
                                </span>
                                ${__('config.history')}
                            </h5>
                            <button type="button" class="btn-close" data-bs-dismiss="modal"></button>
                        </div>
                        <div class="modal-body" id="${modalId}-body" style="padding:0;max-height:500px;overflow-y:auto;">
                            <div style="display:flex;align-items:center;justify-content:center;padding:60px 0;gap:10px;color:#64748b;">
                                <div class="spinner-border spinner-border-sm" role="status"></div>
                                <span>${__('config.loading')}</span>
                            </div>
                        </div>
                        <div class="modal-footer" style="background:#f8fafc;border-top:1px solid #e2e8f0;padding:14px 24px;gap:8px;">
                            <button type="button" class="btn btn-secondary btn-sm" data-bs-dismiss="modal" style="min-width:70px;">
                                ${__('config.close')}
                            </button>
                        </div>
                    </div>
                </div>
            </div>
        `;

        document.body.insertAdjacentHTML('beforeend', loadingHtml);
        const modalEl = document.getElementById(modalId);
        const bsModal = new bootstrap.Modal(modalEl);
        bsModal.show();

        // Fetch history
        try {
            const history = await window.DashboardApi.endpoints.getConfigHistory();

            // Build history list HTML
            let historyHtml = '';
            if (history && history.length > 0) {
                history.forEach(function(item) {
                    const time = new Date(item.timestamp).toLocaleString();
                    historyHtml += `
                        <div class="history-item" style="display:flex;align-items:center;padding:14px 20px;border-bottom:1px solid #e2e8f0;transition:background 0.15s ease;">
                            <div style="flex:1;">
                                <div style="font-weight:500;font-size:14px;color:#1e293b;">${item.description || __('config.manualSnapshot')}</div>
                                <div style="font-size:12px;color:#64748b;margin-top:4px;">${__('config.time')}: ${time}</div>
                                <div style="font-size:12px;color:#64748b;">${__('config.operatorIp')}: ${item.clientIp || '-'}</div>
                                <div style="font-size:12px;color:#94a3b8;">${__('config.version')}: ${item.versionId}</div>
                            </div>
                            <button class="btn btn-sm btn-outline-warning" onclick="DashboardConfig.rollbackTo('${item.versionId}')" style="padding:6px 12px;border-radius:8px;">
                                <i class="bi bi-arrow-counterclockwise me-1"></i>${__('config.rollback')}
                            </button>
                        </div>
                    `;
                });
            } else {
                historyHtml = `<div class="text-center py-4 text-muted">${__('config.noHistory')}</div>`;
            }

            // Update modal body with loaded content
            const body = document.getElementById(modalId + '-body');
            if (body) body.innerHTML = historyHtml;

        } catch (error) {
            console.error('[Config] Failed to get history:', error);
            // Update modal body with error state
            const body = document.getElementById(modalId + '-body');
            if (body) {
                body.innerHTML = `<div class="text-center py-4 text-danger"><i class="bi bi-exclamation-circle me-2"></i>${__('config.getHistoryFailed')}: ${window.DashboardUtils.escapeHtml(error.message)}</div>`;
            } else {
                window.DashboardModals.showError(__('config.getHistoryFailed') + ': ' + error.message);
            }
        }
    };

    // ===== Rollback to Version =====
    window.DashboardConfig.rollbackTo = async function(versionId) {
        const self = this;

        window.DashboardModals.showConfirm(
            __('config.rollbackConfirm') || `确认回滚到版本 ${versionId}？`,
            async function() {
                try {
                    window.DashboardModals.showInfo(__('config.rollbacking'));

                    // Close history modal
                    const historyModal = document.getElementById('dashboard-history-modal');
                    if (historyModal) {
                        bootstrap.Modal.getInstance(historyModal)?.hide();
                    }

                    await window.DashboardApi.endpoints.rollbackConfig(versionId);

                    window.DashboardModals.showSuccess(__('config.rollbacked'));

                    // Reload page
                    setTimeout(function() {
                        window.location.reload();
                    }, 1500);

                } catch (error) {
                    console.error('[Config] Rollback failed:', error);
                    window.DashboardModals.showError(__('config.rollbackFailed') + error.message);
                }
            },
            null,
            { title: __('config.rollback'), danger: true }
        );
    };

})();