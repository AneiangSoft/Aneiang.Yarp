/**
 * Dashboard Config Module - Configuration import/export and history management
 */
(function() {
    'use strict';

    window.DashboardConfig = window.DashboardConfig || {};

    // ===== Export Configuration =====
    window.DashboardConfig.exportConfig = async function() {
        try {
            window.DashboardModals.showInfo(__('config.exporting') || '正在导出配置...');

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

            window.DashboardModals.showSuccess(__('config.exported') || '配置已导出');
        } catch (error) {
            console.error('[Config] Export failed:', error);
            window.DashboardModals.showError(__('config.exportFailed') || '导出失败: ' + error.message);
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
                    <div class="modal-content" style="border-radius:12px;border:none;box-shadow:0 20px 40px rgba(0,0,0,0.2);">
                        <div class="modal-header" style="border-bottom:1px solid #e2e8f0;padding:16px 20px;">
                            <h5 class="modal-title" style="font-weight:600;font-size:16px;display:flex;align-items:center;gap:8px;">
                                <i class="bi bi-upload text-primary"></i>
                                ${__('config.import') || '导入配置'}
                            </h5>
                            <button type="button" class="btn-close" data-bs-dismiss="modal"></button>
                        </div>
                        <div class="modal-body" style="padding:20px;">
                            <div id="import-drop-zone" style="border:2px dashed #e2e8f0;border-radius:12px;padding:40px;text-align:center;background:#f8fafc;cursor:pointer;">
                                <i class="bi bi-file-earmark-arrow-up" style="font-size:48px;color:#6366f1;"></i>
                                <p style="margin-top:12px;color:#64748b;font-size:14px;">${__('config.dropFile') || '拖拽文件到这里或点击选择'}</p>
                                <input type="file" id="import-file-input" accept=".json" style="display:none;">
                            </div>
                            <div id="import-preview" style="display:none;margin-top:16px;">
                                <h6 style="font-weight:600;margin-bottom:8px;">${__('config.selectFile') || '选择的文件'}: <span id="import-file-name"></span></h6>
                                <div id="import-json-preview" style="max-height:300px;overflow:auto;background:#1e293b;border-radius:8px;padding:12px;font-size:12px;color:#e2e8f0;font-family:monospace;"></div>
                            </div>
                            <div id="import-errors" style="display:none;margin-top:16px;">
                                <div class="alert alert-danger" style="border-radius:8px;"></div>
                            </div>
                        </div>
                        <div class="modal-footer" style="border-top:1px solid #e2e8f0;padding:16px 20px;">
                            <button type="button" class="btn btn-secondary" data-bs-dismiss="modal" style="padding:8px 16px;">
                                ${__('modal.cancelBtn') || '取消'}
                            </button>
                            <button type="button" class="btn btn-primary" id="import-btn" disabled style="padding:8px 20px;">
                                <i class="bi bi-upload me-1"></i>${__('config.import') || '导入'}
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
            dropZone.style.background = '#eff6ff';
        });

        dropZone.addEventListener('dragleave', function(e) {
            e.preventDefault();
            dropZone.style.borderColor = '#e2e8f0';
            dropZone.style.background = '#f8fafc';
        });

        dropZone.addEventListener('drop', function(e) {
            e.preventDefault();
            dropZone.style.borderColor = '#e2e8f0';
            dropZone.style.background = '#f8fafc';

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
                        errors.querySelector('.alert').textContent = __('config.importInvalid') || '配置格式无效，需要包含 ReverseProxy.Routes 和 ReverseProxy.Clusters';
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
                    errors.querySelector('.alert').textContent = __('modal.invalidJson') || 'JSON格式错误: ' + err.message;
                    importBtn.disabled = true;
                }
            };
            reader.readAsText(file);
        }

        // Import button
        importBtn.addEventListener('click', async function() {
            if (!importData) return;

            try {
                window.DashboardModals.showInfo(__('config.importing') || '正在导入配置...');
                bsModal.hide();

                const response = await window.DashboardApi.endpoints.importConfig(importData);

                window.DashboardModals.showSuccess(__('config.imported') || '配置已导入');

                // Reload page to reflect changes
                setTimeout(function() {
                    window.location.reload();
                }, 1500);

            } catch (error) {
                console.error('[Config] Import failed:', error);
                window.DashboardModals.showError(__('config.importFailed') || '导入失败: ' + error.message);
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

        // Fetch history
        try {
            const history = await window.DashboardApi.endpoints.getConfigHistory();

            const modalId = 'dashboard-history-modal';
            const existing = document.getElementById(modalId);
            if (existing) existing.remove();

            // Build history list HTML
            let historyHtml = '';
            if (history && history.length > 0) {
                history.forEach(function(item) {
                    const time = new Date(item.timestamp).toLocaleString();
                    historyHtml += `
                        <div class="history-item" style="display:flex;align-items:center;padding:12px 16px;border-bottom:1px solid #e2e8f0;">
                            <div style="flex:1;">
                                <div style="font-weight:500;font-size:14px;">${item.description || __('config.manualSnapshot')}</div>
                                <div style="font-size:12px;color:#64748b;">${__('config.time') || '时间'}: ${time}</div>
                                <div style="font-size:12px;color:#94a3b8;">${__('config.version') || '版本'}: ${item.versionId}</div>
                            </div>
                            <button class="btn btn-sm btn-outline-warning" onclick="DashboardConfig.rollbackTo('${item.versionId}')" style="padding:6px 12px;">
                                <i class="bi bi-arrow-counterclockwise me-1"></i>${__('config.rollback') || '回滚'}
                            </button>
                        </div>
                    `;
                });
            } else {
                historyHtml = `<div class="text-center py-4 text-muted">${__('config.noHistory') || '暂无历史版本'}</div>`;
            }

            const modalHtml = `
                <div class="modal fade" id="${modalId}" tabindex="-1">
                    <div class="modal-dialog modal-dialog-centered modal-lg">
                        <div class="modal-content" style="border-radius:12px;border:none;box-shadow:0 20px 40px rgba(0,0,0,0.2);">
                            <div class="modal-header" style="border-bottom:1px solid #e2e8f0;padding:16px 20px;">
                                <h5 class="modal-title" style="font-weight:600;font-size:16px;display:flex;align-items:center;gap:8px;">
                                    <i class="bi bi-clock-history text-primary"></i>
                                    ${__('config.history') || '配置历史'}
                                </h5>
                                <button type="button" class="btn-close" data-bs-dismiss="modal"></button>
                            </div>
                            <div class="modal-body" style="padding:0;max-height:500px;overflow-y:auto;">
                                ${historyHtml}
                            </div>
                            <div class="modal-footer" style="border-top:1px solid #e2e8f0;padding:16px 20px;">
                                <button type="button" class="btn btn-secondary" data-bs-dismiss="modal" style="padding:8px 16px;">
                                    ${__('config.close') || 'Close'}
                                </button>
                            </div>
                        </div>
                    </div>
                </div>
            `;

            document.body.insertAdjacentHTML('beforeend', modalHtml);

            const modalEl = document.getElementById(modalId);
            const bsModal = new bootstrap.Modal(modalEl);

            // Remove modal on hide
            modalEl.addEventListener('hidden.bs.modal', function() {
                modalEl.remove();
            });

            bsModal.show();

        } catch (error) {
            console.error('[Config] Failed to get history:', error);
            window.DashboardModals.showError(__('config.getHistoryFailed') + ': ' + error.message);
        }
    };

    // ===== Rollback to Version =====
    window.DashboardConfig.rollbackTo = async function(versionId) {
        const self = this;

        window.DashboardModals.showConfirm(
            __('config.rollbackConfirm') || `确认回滚到版本 ${versionId}？`,
            async function() {
                try {
                    window.DashboardModals.showInfo(__('config.rollbacking') || '正在回滚...');

                    // Close history modal
                    const historyModal = document.getElementById('dashboard-history-modal');
                    if (historyModal) {
                        bootstrap.Modal.getInstance(historyModal)?.hide();
                    }

                    await window.DashboardApi.endpoints.rollbackConfig(versionId);

                    window.DashboardModals.showSuccess(__('config.rollbacked') || '已回滚');

                    // Reload page
                    setTimeout(function() {
                        window.location.reload();
                    }, 1500);

                } catch (error) {
                    console.error('[Config] Rollback failed:', error);
                    window.DashboardModals.showError(__('config.rollbackFailed') || '回滚失败: ' + error.message);
                }
            },
            null,
            { title: __('config.rollback') || '回滚', danger: true }
        );
    };

})();