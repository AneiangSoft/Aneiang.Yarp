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

    // ===== Webhook Settings Modal =====
    window.DashboardConfig.showWebhookModal = async function() {
        const modalId = 'dashboard-webhook-modal';
        const existing = document.getElementById(modalId);
        if (existing) existing.remove();

        const platformDefs = [
            { key: 'dingtalk', icon: 'bi-chat-dots-fill', color: '#0089FF', bgGrad: 'linear-gradient(135deg,#0089FF,#36A3FF)', placeholder: 'https://oapi.dingtalk.com/robot/send?access_token=...' },
            { key: 'generic', icon: 'bi-link-45deg', color: '#6366f1', bgGrad: 'linear-gradient(135deg,#6366f1,#818cf8)', placeholder: 'https://example.com/webhook' }
        ];

        var allEventTypes = [
            { key: 'AddRoute', group: 'route' },
            { key: 'UpdateRoute', group: 'route' },
            { key: 'RemoveRoute', group: 'route' },
            { key: 'AddCluster', group: 'cluster' },
            { key: 'UpdateCluster', group: 'cluster' },
            { key: 'RemoveCluster', group: 'cluster' },
            { key: 'RenameCluster', group: 'cluster' },
            { key: 'RollbackConfig', group: 'config' }
        ];

        function tabHtml(pd) {
            return `<button type="button" class="webhook-platform-tab btn btn-sm" data-platform="${pd.key}" data-active="false"
                style="display:flex;align-items:center;gap:6px;padding:7px 14px;border-radius:8px;border:1px solid #e2e8f0;background:#fff;color:#475569;font-size:13px;font-weight:500;cursor:pointer;transition:all 0.2s;"
                onclick="window.DashboardConfig._switchWebhookTab('${pd.key}')">
                <i class="${pd.icon}" style="font-size:14px;color:${pd.color};"></i>
                <span>${__('webhook.platform.' + pd.key)}</span>
            </button>`;
        }

        const tabsHtml = platformDefs.map(tabHtml).join('');


        function sectionHtml(pd) {
            return `<div class="webhook-platform-section" data-platform="${pd.key}" style="display:none;">
                <div style="padding:20px;background:#fafbfc;border-radius:12px;border:1px solid #e8ecf1;">
                    <div style="display:flex;align-items:center;gap:10px;margin-bottom:14px;">
                        <span style="display:inline-flex;align-items:center;justify-content:center;width:36px;height:36px;border-radius:10px;background:${pd.bgGrad};color:#fff;font-size:16px;">
                            <i class="${pd.icon}"></i>
                        </span>
                        <div>
                            <div style="font-weight:600;font-size:15px;color:#1e293b;">${__('webhook.platform.' + pd.key)}</div>
                            <small style="color:#64748b;font-size:12px;">${__('webhook.' + pd.key + '.help')}</small>
                        </div>
                    </div>

                    <!-- Endpoint List Section -->
                    <div style="margin-bottom:16px;">
                        <div style="font-weight:600;font-size:13px;color:#1e293b;margin-bottom:8px;display:flex;align-items:center;gap:6px;">
                            <i class="bi bi-robot" style="color:#6366f1;"></i>
                            <span>${__('webhook.configuredEndpoints') || 'Configured Robots'}</span>
                            <span id="webhook-endpoint-count-${pd.key}" style="background:#e2e8f0;color:#475569;padding:2px 8px;border-radius:10px;font-size:11px;font-weight:500;">0</span>
                        </div>
                        <div id="webhook-endpoint-list-${pd.key}" style="margin-bottom:8px;"></div>
                    </div>

                    <!-- Add New Endpoint Section -->
                    <div style="background:#fff;border-radius:8px;padding:12px;border:1px dashed #cbd5e1;margin-bottom:16px;">
                        <div style="font-size:12px;color:#64748b;margin-bottom:8px;">
                            <i class="bi bi-plus-circle me-1"></i>${__('webhook.addNewEndpoint') || 'Add new robot'}
                        </div>
                        <div style="margin-bottom:8px;">
                            <input type="url" id="webhook-new-url-${pd.key}" placeholder="${pd.placeholder}"
                                style="width:100%;padding:8px 12px;border:1px solid #e2e8f0;border-radius:8px;font-size:13px;outline:none;transition:border-color 0.2s;font-family:monospace;"
                                onfocus="this.style.borderColor='${pd.color}'" onblur="this.style.borderColor='#e2e8f0'">
                        </div>
                        <div style="display:flex;gap:8px;align-items:center;">
                            <input type="text" id="webhook-new-secret-${pd.key}" placeholder="${__('webhook.secret.placeholder')}"
                                style="flex:1;padding:8px 12px;border:1px solid #e2e8f0;border-radius:8px;font-size:13px;outline:none;transition:border-color 0.2s;font-family:monospace;"
                                onfocus="this.style.borderColor='${pd.color}'" onblur="this.style.borderColor='#e2e8f0'">
                            <button type="button" id="webhook-add-endpoint-btn-${pd.key}"
                                style="padding:8px 16px;background:${pd.bgGrad};color:#fff;border:none;border-radius:8px;font-size:13px;font-weight:500;cursor:pointer;white-space:nowrap;">
                                <i class="bi bi-plus-lg me-1"></i>${__('webhook.add')}
                            </button>
                        </div>
                    </div>

                    <!-- Test Section -->
                    <div style="margin-top:16px;display:flex;gap:8px;">
                        <button type="button" class="btn btn-outline-secondary btn-sm" onclick="window.DashboardConfig._testWebhook('${pd.key}')"
                            style="display:flex;align-items:center;gap:6px;padding:8px 14px;border-radius:8px;font-size:13px;">
                            <i class="bi bi-send"></i>
                            <span>${__('webhook.test') || 'Test Push'}</span>
                        </button>
                        <span id="webhook-test-result-${pd.key}" style="display:flex;align-items:center;font-size:12px;"></span>
                    </div>
                </div>
            </div>`;
        }

        const sectionsHtml = platformDefs.map(sectionHtml).join('');

        // Event types section HTML
        var eventGroups = [
            { key: 'route', label: __('webhook.events.routeGroup') || '路由变更', icon: 'bi-signpost-split', color: '#3b82f6' },
            { key: 'cluster', label: __('webhook.events.clusterGroup') || '集群变更', icon: 'bi-hdd-network', color: '#8b5cf6' },
            { key: 'config', label: __('webhook.events.configGroup') || '配置管理', icon: 'bi-gear', color: '#f59e0b' }
        ];

        function eventSectionHtml() {
            var html = '<div style="padding:20px;background:#fafbfc;border-radius:12px;border:1px solid #e8ecf1;">';
            html += '<div style="display:flex;align-items:center;gap:10px;margin-bottom:14px;">';
            html += '<span style="display:inline-flex;align-items:center;justify-content:center;width:36px;height:36px;border-radius:10px;background:linear-gradient(135deg,#10b981,#34d399);color:#fff;font-size:16px;">';
            html += '<i class="bi bi-broadcast"></i></span>';
            html += '<div><div style="font-weight:600;font-size:15px;color:#1e293b;">' + (__('webhook.events.title') || '通知事件') + '</div>';
            html += '<small style="color:#64748b;font-size:12px;">' + (__('webhook.events.help') || '选择需要推送通知的事件类型') + '</small></div>';
            html += '<div style="margin-left:auto;display:flex;gap:6px;">';
            html += '<button type="button" id="webhook-events-select-all" style="padding:4px 10px;border-radius:6px;border:1px solid #e2e8f0;background:#fff;color:#475569;font-size:11px;cursor:pointer;font-weight:500;">' + (__('webhook.events.selectAll') || '全选') + '</button>';
            html += '<button type="button" id="webhook-events-deselect-all" style="padding:4px 10px;border-radius:6px;border:1px solid #e2e8f0;background:#fff;color:#475569;font-size:11px;cursor:pointer;font-weight:500;">' + (__('webhook.events.deselectAll') || '全不选') + '</button>';
            html += '</div></div>';

            eventGroups.forEach(function(eg) {
                html += '<div style="margin-bottom:12px;">';
                html += '<div style="font-weight:600;font-size:13px;color:#1e293b;margin-bottom:6px;display:flex;align-items:center;gap:6px;">';
                html += '<i class="' + eg.icon + '" style="color:' + eg.color + ';"></i>';
                html += '<span>' + eg.label + '</span></div>';
                html += '<div style="display:flex;flex-wrap:wrap;gap:8px;">';
                allEventTypes.filter(function(e) { return e.group === eg.key; }).forEach(function(ev) {
                    html += '<label style="display:flex;align-items:center;gap:6px;padding:6px 12px;background:#fff;border:1px solid #e2e8f0;border-radius:8px;cursor:pointer;font-size:13px;color:#334155;transition:all 0.15s;user-select:none;" class="webhook-event-label" data-event-key="' + ev.key + '">';
                    html += '<input type="checkbox" class="webhook-event-checkbox" data-event-key="' + ev.key + '" checked style="accent-color:#10b981;width:16px;height:16px;cursor:pointer;">';
                    html += '<span>' + (__('webhook.events.' + ev.key) || ev.key) + '</span></label>';
                });
                html += '</div></div>';
            });

            html += '</div>';
            return html;
        }

        const modalHtml = `
            <div class="modal fade" id="${modalId}" tabindex="-1">
                <div class="modal-dialog modal-dialog-centered modal-lg">
                    <div class="modal-content" style="border-radius:16px;border:none;box-shadow:0 25px 50px rgba(0,0,0,0.25);overflow:hidden;">
                        <div class="modal-header" style="background:linear-gradient(135deg,#f8fafc 0%,#e2e8f0 100%);border-bottom:1px solid #e2e8f0;padding:18px 24px;">
                            <h5 class="modal-title" style="font-weight:600;font-size:16px;display:flex;align-items:center;gap:10px;">
                                <span style="display:inline-flex;align-items:center;justify-content:center;width:32px;height:32px;border-radius:8px;background:linear-gradient(135deg,#f59e0b,#fbbf24);color:#fff;font-size:16px;">
                                    <i class="bi bi-bell"></i>
                                </span>
                                ${__('webhook.title')}
                            </h5>
                            <button type="button" class="btn-close" data-bs-dismiss="modal"></button>
                        </div>
                        <div class="modal-body" style="padding:20px 24px;">
                            <div style="display:flex;gap:8px;flex-wrap:wrap;margin-bottom:16px;">
                                ${tabsHtml}
                            </div>
                            ${sectionsHtml}
                            <div style="margin-top:16px;">
                                ${eventSectionHtml()}
                            </div>
                        </div>
                        <div class="modal-footer" style="background:#f8fafc;border-top:1px solid #e2e8f0;padding:14px 24px;gap:8px;">
                            <button type="button" class="btn btn-secondary btn-sm" data-bs-dismiss="modal" style="min-width:70px;">
                                ${__('modal.cancelBtn')}
                            </button>
                            <button type="button" class="btn btn-primary btn-sm" id="webhook-save-btn" style="min-width:100px;">
                                <i class="bi bi-check-lg me-1"></i>${__('webhook.save')}
                            </button>
                        </div>
                    </div>
                </div>
            </div>
        `;

        document.body.insertAdjacentHTML('beforeend', modalHtml);
        const modalEl = document.getElementById(modalId);
        const bsModal = new bootstrap.Modal(modalEl);

        // Data structure: each platform has an array of { url, secret } endpoints
        var platformData = {};
        platformDefs.forEach(function(pd) {
            platformData[pd.key] = { endpoints: [] };
        });
        var enabledEvents = []; // empty = all enabled

        window.DashboardConfig._switchWebhookTab = function(platform) {
            document.querySelectorAll('.webhook-platform-tab').forEach(function(tab) {
                var isActive = tab.dataset.platform === platform;
                tab.dataset.active = isActive ? 'true' : 'false';
                if (isActive) {
                    tab.style.background = '#f1f5f9';
                    tab.style.borderColor = '#cbd5e1';
                    tab.style.color = '#0f172a';
                } else {
                    tab.style.background = '#fff';
                    tab.style.borderColor = '#e2e8f0';
                    tab.style.color = '#475569';
                }
            });
            document.querySelectorAll('.webhook-platform-section').forEach(function(sec) {
                sec.style.display = sec.dataset.platform === platform ? 'block' : 'none';
            });
        };

        window.DashboardConfig._testWebhook = async function(platform) {
            var resultEl = document.getElementById('webhook-test-result-' + platform);
            if (!resultEl) return;
            resultEl.innerHTML = '<span class="spinner-border spinner-border-sm" role="status"></span><span style="margin-left:6px;color:#64748b;">' + (__('webhook.testing') || 'Testing...') + '</span>';

            try {
                var resp = await window.DashboardApi.endpoints.testWebhook({ platform: platform });
                if (resp && resp.success > 0) {
                    resultEl.innerHTML = '<i class="bi bi-check-circle-fill" style="color:#22c55e;"></i><span style="margin-left:6px;color:#166534;">' + (__('webhook.testSuccess') || 'Success') + ' (' + resp.success + '/' + resp.total + ')</span>';
                } else {
                    resultEl.innerHTML = '<i class="bi bi-x-circle-fill" style="color:#ef4444;"></i><span style="margin-left:6px;color:#dc2626;">' + (__('webhook.testFailed') || 'Failed') + '</span>';
                }
            } catch (e) {
                resultEl.innerHTML = '<i class="bi bi-x-circle-fill" style="color:#ef4444;"></i><span style="margin-left:6px;color:#dc2626;">' + (e.message || 'Error') + '</span>';
            }
        };

        function renderEndpointList(platform) {
            var listEl = document.getElementById('webhook-endpoint-list-' + platform);
            var countEl = document.getElementById('webhook-endpoint-count-' + platform);
            if (!listEl) return;
            var endpoints = platformData[platform].endpoints;

            // Update count badge
            if (countEl) countEl.textContent = endpoints.length;

            if (endpoints.length === 0) {
                listEl.innerHTML = '<div style="text-align:center;padding:12px;color:#94a3b8;font-size:13px;background:#fff;border-radius:8px;border:1px dashed #e2e8f0;">' +
                    '<i class="bi bi-inbox" style="font-size:20px;display:block;margin-bottom:6px;opacity:0.5;"></i>' +
                    __('webhook.noEndpoints') + '</div>';
                return;
            }

            var html = '<div style="display:flex;flex-direction:column;gap:8px;">';
            endpoints.forEach(function(ep, index) {
                var displayUrl = ep.url.length > 50 ? ep.url.substring(0, 50) + '...' : ep.url;
                html += '<div style="background:#fff;border:1px solid #e2e8f0;border-radius:8px;padding:12px;transition:box-shadow 0.15s;"' +
                    ' onmouseover="this.style.boxShadow=\'0 2px 8px rgba(0,0,0,0.08)\'" onmouseout="this.style.boxShadow=\'none\'">' +
                    '<div style="display:flex;align-items:center;gap:8px;margin-bottom:8px;">' +
                    '<i class="bi bi-robot" style="color:#6366f1;font-size:16px;flex-shrink:0;"></i>' +
                    '<span style="flex:1;font-size:12px;color:#334155;word-break:break-all;font-family:monospace;" title="' + (window.DashboardUtils?.escapeHtml?.(ep.url) || ep.url) + '">' + (window.DashboardUtils?.escapeHtml?.(displayUrl) || displayUrl) + '</span>' +
                    '<button type="button" data-remove-endpoint-platform="' + platform + '" data-remove-endpoint-index="' + index + '"' +
                    ' style="background:none;border:none;color:#94a3b8;cursor:pointer;padding:4px 8px;border-radius:6px;transition:all 0.15s;"' +
                    ' onmouseover="this.style.background=\'#fef2f2\';this.style.color=\'#ef4444\'" onmouseout="this.style.background=\'none\';this.style.color=\'#94a3b8\'" title="' + __('webhook.removeEndpoint') + '">' +
                    '<i class="bi bi-trash3" style="font-size:13px;"></i></button></div>' +
                    '<div style="display:flex;align-items:center;gap:6px;">' +
                    '<i class="bi bi-key" style="color:#94a3b8;font-size:12px;"></i>' +
                    '<input type="text" data-endpoint-platform="' + platform + '" data-endpoint-index="' + index + '" value="' + (window.DashboardUtils?.escapeHtml?.(ep.secret || '') || '') + '"' +
                    ' placeholder="' + (__('webhook.secret.placeholder') || 'Secret') + '"' +
                    ' style="flex:1;padding:6px 10px;border:1px solid #e2e8f0;border-radius:6px;font-size:12px;outline:none;font-family:monospace;"' +
                    ' onfocus="this.style.borderColor=\'#6366f1\'" onblur="this.style.borderColor=\'#e2e8f0\'">' +
                    '</div></div>';
            });
            html += '</div>';
            listEl.innerHTML = html;

            // Bind remove buttons
            listEl.querySelectorAll('[data-remove-endpoint-platform]').forEach(function(btn) {
                btn.addEventListener('click', function() {
                    var p = this.dataset.removeEndpointPlatform;
                    var idx = parseInt(this.dataset.removeEndpointIndex);
                    platformData[p].endpoints.splice(idx, 1);
                    renderEndpointList(p);
                });
            });

            // Bind secret input changes
            listEl.querySelectorAll('[data-endpoint-platform]').forEach(function(input) {
                input.addEventListener('input', function() {
                    var p = this.dataset.endpointPlatform;
                    var idx = parseInt(this.dataset.endpointIndex);
                    platformData[p].endpoints[idx].secret = this.value;
                });
            });
        }

        // Bind add endpoint buttons
        platformDefs.forEach(function(pd) {
            var addBtn = document.getElementById('webhook-add-endpoint-btn-' + pd.key);
            var urlInput = document.getElementById('webhook-new-url-' + pd.key);
            var secretInput = document.getElementById('webhook-new-secret-' + pd.key);
            if (!addBtn || !urlInput) return;

            addBtn.addEventListener('click', function() {
                var url = urlInput.value.trim();
                var secret = secretInput ? secretInput.value.trim() : '';
                if (!url) {
                    window.DashboardModals.showWarning(__('webhook.urlRequired'));
                    return;
                }
                try { new URL(url); } catch (e) {
                    window.DashboardModals.showWarning(__('webhook.urlRequired'));
                    urlInput.style.borderColor = '#ef4444';
                    setTimeout(function() { urlInput.style.borderColor = '#e2e8f0'; }, 2000);
                    return;
                }
                // Check if URL already exists
                var exists = platformData[pd.key].endpoints.some(function(ep) { return ep.url === url; });
                if (exists) {
                    window.DashboardModals.showWarning(__('webhook.urlExists') || 'This URL already exists');
                    return;
                }
                platformData[pd.key].endpoints.push({ url: url, secret: secret });
                urlInput.value = '';
                if (secretInput) secretInput.value = '';
                renderEndpointList(pd.key);
            });

            urlInput.addEventListener('keydown', function(e) {
                if (e.key === 'Enter') { e.preventDefault(); addBtn.click(); }
            });
        });

        // Save button
        document.getElementById('webhook-save-btn').addEventListener('click', async function() {
            var btn = this;
            btn.disabled = true;
            btn.innerHTML = '<span class="spinner-border spinner-border-sm me-1" role="status"></span>' + __('webhook.saving');

            try {
                var platforms = {};
                platformDefs.forEach(function(pd) {
                    platforms[pd.key] = {
                        endpoints: platformData[pd.key].endpoints.map(function(ep) {
                            return { url: ep.url, secret: ep.secret };
                        })
                    };
                });

                // Collect enabled events
                var checked = [];
                document.querySelectorAll('.webhook-event-checkbox').forEach(function(cb) {
                    if (cb.checked) checked.push(cb.dataset.eventKey);
                });

                await window.DashboardApi.endpoints.saveWebhookSettings({ platforms: platforms, enabledEvents: checked });
                window.DashboardModals.showSuccess(__('webhook.saved'));
                bsModal.hide();
            } catch (error) {
                console.error('[Config] Save webhook failed:', error);
                window.DashboardModals.showError(__('webhook.saveFailed') + ': ' + error.message);
                btn.disabled = false;
                btn.innerHTML = '<i class="bi bi-check-lg me-1"></i>' + __('webhook.save');
            }
        });

        // Event select all / deselect all
        document.getElementById('webhook-events-select-all').addEventListener('click', function() {
            document.querySelectorAll('.webhook-event-checkbox').forEach(function(cb) { cb.checked = true; updateEventLabelStyle(cb); });
        });
        document.getElementById('webhook-events-deselect-all').addEventListener('click', function() {
            document.querySelectorAll('.webhook-event-checkbox').forEach(function(cb) { cb.checked = false; updateEventLabelStyle(cb); });
        });

        // Style event checkboxes based on checked state
        function updateEventLabelStyle(cb) {
            var label = cb.closest('.webhook-event-label');
            if (!label) return;
            if (cb.checked) {
                label.style.borderColor = '#10b981';
                label.style.background = '#f0fdf4';
            } else {
                label.style.borderColor = '#e2e8f0';
                label.style.background = '#fff';
            }
        }

        document.querySelectorAll('.webhook-event-checkbox').forEach(function(cb) {
            cb.addEventListener('change', function() { updateEventLabelStyle(this); });
            updateEventLabelStyle(cb);
        });

        // Load existing settings
        try {
            var settings = await window.DashboardApi.endpoints.getWebhookSettings();
            var data = settings || {};
            platformDefs.forEach(function(pd) {
                var pData = data[pd.key] || [];
                platformData[pd.key].endpoints = pData.map(function(ep) {
                    return { url: ep.url, secret: ep.secret || '' };
                });
                renderEndpointList(pd.key);
            });

            // Load enabled events
            var loadedEvents = data.enabledEvents || [];
            if (loadedEvents.length > 0) {
                document.querySelectorAll('.webhook-event-checkbox').forEach(function(cb) {
                    cb.checked = loadedEvents.some(function(e) { return e === cb.dataset.eventKey; });
                });
            }

            var firstActive = platformDefs.find(function(pd) { return platformData[pd.key].endpoints.length > 0; });
            window.DashboardConfig._switchWebhookTab(firstActive ? firstActive.key : 'dingtalk');
        } catch (error) {
            console.error('[Config] Load webhook failed:', error);
            window.DashboardModals.showError(__('webhook.loadFailed') + ': ' + error.message);
            platformDefs.forEach(function(pd) { renderEndpointList(pd.key); });
            window.DashboardConfig._switchWebhookTab('dingtalk');
        }

        modalEl.addEventListener('hidden.bs.modal', function() {
            modalEl.remove();
            window.DashboardConfig._switchWebhookTab = null;
            window.DashboardConfig._testWebhook = null;
        });

        bsModal.show();
    };

})();