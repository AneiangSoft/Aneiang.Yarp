/**
 * Plugin Binding Manager - Shared CRUD UI for plugin pages
 * Provides: summary cards, bindings table, add/edit modal, toggle, delete
 */
(function() {
    'use strict';

    function esc(value) {
        return window.DashboardUtils ? window.DashboardUtils.escapeHtml(value == null ? '' : String(value)) : String(value == null ? '' : value).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    }

    /* Escape a value for use inside a double-quoted HTML attribute. */
    function attr(value) {
        return esc(value).replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    function t(key, fallback) {
        var text = window.__ ? window.__(key) : null;
        return (!text || text === key) ? (fallback || key) : text;
    }

    function getPluginSchema(plugin) {
        if (window.DashboardCapabilities && window.DashboardCapabilities.getPluginSchema) {
            return window.DashboardCapabilities.getPluginSchema(plugin);
        }
        return null;
    }

    function create(config) {
        var pluginId = config.pluginId;
        var scope = config.scope; // 'Route' or 'Cluster'
        var scopeLabel = config.scopeLabel || (scope === 'Cluster' ? t('pluginPage.cluster', 'Cluster') : t('pluginPage.route', 'Route'));
        var containerId = config.containerId;
        var refreshTimeId = config.refreshTimeId;
        var moduleName = config.moduleName;

        var state = {
            plugin: null,
            bindings: [],
            scopes: [],
            pluginSchema: null
        };

        var module = {
            initialized: false,
            autoRefreshInterval: null,

            init: function() {
                if (this.initialized) return;
                this.initialized = true;
            },

            destroy: function() {
                if (this.autoRefreshInterval) {
                    clearInterval(this.autoRefreshInterval);
                    this.autoRefreshInterval = null;
                }
                this.initialized = false;
            },

            load: async function() {
                var container = document.getElementById(containerId);
                if (!container) return;
                try {
                    container.innerHTML = '<div class="loading-state"><div class="loading-spinner"></div><div class="loading-text">' + t('common.loading', 'Loading...') + '</div></div>';

                    var results = await Promise.all([
                        window.DashboardApi.getPlugin(pluginId),
                        window.DashboardApi.getPluginBindings(pluginId, scope),
                        scope === 'Cluster'
                            ? window.DashboardApi.getClusters()
                            : window.DashboardApi.getRoutes()
                    ]);

                    state.plugin = results[0];
                    state.bindings = Array.isArray(results[1]) ? results[1] : [];
                    state.scopes = Array.isArray(results[2]) ? results[2] : [];
                    state.pluginSchema = getPluginSchema(state.plugin);

                    this.render(container);
                    this.updateRefreshTime();
                } catch (error) {
                    container.innerHTML = '<div class="alert alert-danger">' + t('common.loadFailed', 'Load failed') + ': ' + esc(error.message || error) + '</div>';
                }
            },

            render: function(container) {
                var plugin = state.plugin;
                var bindings = state.bindings;
                var enabled = plugin && plugin.enabled;
                var bindingCount = bindings.length;
                var icon = config.icon || 'bi-puzzle';
                var color = config.color || 'text-primary';

                var html = '';

                // Compact meta line: status · version · bindings · type, refresh actions at right end
                html += '<div class="pc-meta-line">';
                html += '<span class="pc-meta-item' + (enabled ? ' pc-meta-item--ok' : '') + '"><i class="bi bi-' + (enabled ? 'check-circle' : 'x-circle') + '"></i><span class="pc-meta-num">' + (enabled ? t('common.enabled', 'Enabled') : t('common.disabled', 'Disabled')) + '</span></span>';
                html += '<span class="pc-meta-item"><i class="bi bi-tag"></i><span class="pc-meta-num">v' + esc(plugin ? (plugin.version || '1.0') : '-') + '</span></span>';
                html += '<span class="pc-meta-item"><i class="bi bi-link-45deg"></i><span class="pc-meta-num">' + bindingCount + '</span><span class="pc-meta-label">' + t('pluginPage.bindingCount', 'Bindings') + '</span></span>';
                html += '<span class="pc-meta-item"><i class="bi bi-box"></i><span class="pc-meta-label">' + (plugin && plugin.isBuiltIn ? t('common.builtIn', 'Built-in') : t('common.external', 'External')) + '</span></span>';
                html += '<div class="pc-meta-actions">';
                html += '<span class="plg-plugin-id" style="font-size:11.5px;">' + scopeLabel + '</span>';
                html += '<span id="' + refreshTimeId + '" class="refresh-badge"></span>';
                html += '<button class="pr-refresh-btn" data-action="refresh" title="' + t('index.btn.refresh', '刷新') + '"><i class="bi bi-arrow-clockwise"></i></button>';
                html += '<button class="btn btn-sm btn-success" data-action="add"><i class="bi bi-plus-lg me-1"></i>' + t('pluginPage.addBinding', 'Add Binding') + '</button>';
                html += '</div>';
                html += '</div>';

                // Help text (single subtle line; description collapses into it)
                var descText = config.descKey ? t(config.descKey, '') : (config.descText || '');
                var helpText = config.helpKey ? t(config.helpKey, '') : (config.helpText || '');
                if (descText || helpText) {
                    html += '<div class="pc-help-line"><i class="bi bi-info-circle"></i><span>' + esc(helpText || descText) + '</span></div>';
                }

                // Bindings table (shared plg-table style)
                html += '<div class="plg-table-wrap"><table class="plg-table"><thead><tr>';
                html += '<th style="width:220px;">' + scopeLabel + ' ID</th>';
                html += '<th>' + t('pluginPage.configSummary', 'Config Summary') + '</th>';
                html += '<th style="width:110px;">' + t('common.status', 'Status') + '</th>';
                html += '<th class="plg-th-actions">' + t('common.actions', 'Actions') + '</th>';
                html += '</tr></thead><tbody>';

                if (bindingCount > 0) {
                    bindings.forEach(function(b) {
                        var bindingConfig = parseConfigSafe(b);
                        var summary = summarizeConfigSafe(bindingConfig, pluginId);
                        var isOn = b.enabled;
                        var healthColor = isOn ? '#10b981' : '#94a3b8';
                        html += '<tr' + (isOn ? '' : ' class="plg-row--off"') + '>';
                        html += '<td><span class="plg-plugin-id" style="font-size:12px;">' + esc(b.scopeId || '-') + '</span></td>';
                        html += '<td><span class="plg-plugin-id" style="font-size:12px;">' + esc(summary) + '</span></td>';
                        html += '<td><span class="plg-health" style="color:' + healthColor + ';"><span class="plg-dot" style="background:' + healthColor + ';"></span>' + (isOn ? t('common.enabled', 'Enabled') : t('common.disabled', 'Disabled')) + '</span></td>';
                        html += '<td class="plg-actions">';
                        html += '<button class="btn btn-sm btn-outline-primary" title="' + t('pluginPage.editBinding', 'Edit') + '" data-action="edit" data-id="' + attr(b.id) + '"><i class="bi bi-pencil"></i></button>';
                        html += '<button class="btn btn-sm btn-outline-' + (isOn ? 'warning' : 'success') + '" title="' + (isOn ? t('pluginPage.disable', 'Disable') : t('pluginPage.enable', 'Enable')) + '" data-action="toggle" data-id="' + attr(b.id) + '"><i class="bi bi-' + (isOn ? 'toggle-on' : 'toggle-off') + '"></i></button>';
                        html += '<button class="btn btn-sm btn-outline-danger" title="' + t('common.delete', 'Delete') + '" data-action="delete" data-id="' + attr(b.id) + '"><i class="bi bi-trash"></i></button>';
                        html += '</td>';
                        html += '</tr>';
                    });
                } else {
                    html += '<tr><td colspan="4" style="white-space:normal;">';
                    html += '<div class="pr-empty">';
                    html += '<i class="bi bi-inbox"></i>';
                    html += '<p class="mb-1">' + t('pluginPage.noBindings', 'No bindings yet') + '</p>';
                    html += '<p class="small mb-2">' + t('pluginPage.noBindingsHint', 'Click "Add Binding" above to bind this plugin to') + ' ' + scopeLabel + '</p>';
                    html += '<button class="btn btn-outline-primary btn-sm" data-action="add"><i class="bi bi-plus-lg me-1"></i>' + t('pluginPage.addBinding', 'Add Binding') + '</button>';
                    html += '</div>';
                    html += '</td></tr>';
                }

                html += '</tbody></table></div>';
                container.innerHTML = html;

                /* Row actions are delegated instead of inlined: binding ids are
                   strings, and embedding them in onclick attributes broke out of
                   the HTML attribute and produced truncated JS (SyntaxError). */
                if (!container.__pcActionBound) {
                    container.__pcActionBound = true;
                    container.addEventListener('click', function(event) {
                        var trigger = event.target && event.target.closest ? event.target.closest('[data-action]') : null;
                        if (!trigger || !container.contains(trigger)) return;
                        var action = trigger.getAttribute('data-action');
                        var id = trigger.getAttribute('data-id');
                        if (action === 'refresh') module.load();
                        else if (action === 'add') module.openAddModal();
                        else if (action === 'edit') module.openEditModal(id);
                        else if (action === 'toggle') module.toggleBinding(id);
                        else if (action === 'delete') module.deleteBinding(id);
                    });
                }
            },

            openAddModal: function() {
                openEditor(null);
            },

            openEditModal: function(bindingId) {
                var binding = findBinding(bindingId);
                if (!binding) {
                    window.DashboardModals.showError(t('pluginPage.bindingNotFound', 'Binding not found'));
                    return;
                }
                openEditor(binding);
            },

            toggleBinding: async function(bindingId) {
                var binding = findBinding(bindingId);
                if (!binding) return;
                try {
                    var payload = Object.assign({}, binding, {
                        enabled: !binding.enabled,
                        scope: binding.scope === 1 ? 'Route' : (binding.scope === 2 ? 'Cluster' : binding.scope)
                    });
                    await window.DashboardApi.updateBinding(binding.id, payload);
                    window.DashboardModals.showSuccess(binding.enabled ? t('pluginPage.disabledSuccess', 'Disabled') : t('pluginPage.enabledSuccess', 'Enabled'));
                    await this.load();
                } catch (error) {
                    window.DashboardModals.showError(t('common.operationFailed', 'Operation failed') + ': ' + (error.message || error));
                }
            },

            deleteBinding: function(bindingId) {
                var binding = findBinding(bindingId);
                if (!binding) return;
                var self = this;
                window.DashboardModals.showConfirm(
                    t('pluginPage.deleteConfirm', 'Delete this binding?') + ' (' + (binding.scopeId || '') + ')',
                    async function() {
                        try {
                            await window.DashboardApi.deleteBinding(binding.id);
                            window.DashboardModals.showSuccess(t('pluginPage.deletedSuccess', 'Deleted'));
                            await self.load();
                        } catch (error) {
                            window.DashboardModals.showError(t('common.deleteFailed', 'Delete failed') + ': ' + (error.message || error));
                        }
                    },
                    null,
                    { title: t('common.delete', 'Delete'), danger: true }
                );
            },

            updateRefreshTime: function() {
                var el = document.getElementById(refreshTimeId);
                if (el) el.textContent = new Date().toLocaleTimeString();
            }
        };

        function parseConfigSafe(binding) {
            try {
                return JSON.parse(binding.configJson || '{}');
            } catch (_) {
                return {};
            }
        }

        /* Ids come from data-* attributes as strings, so compare loosely. */
        function findBinding(bindingId) {
            return state.bindings.find(function(b) { return String(b.id) === String(bindingId); });
        }

        function summarizeConfigSafe(config, pluginId) {
            var keys = Object.keys(config);
            if (!keys.length) return '{}';
            var Cap = window.DashboardCapabilities;
            return keys.slice(0, 4).map(function(key) {
                var value = config[key];
                if (value && typeof value === 'object') value = Array.isArray(value) ? '[' + value.length + ']' : '{...}';
                var label = (Cap && Cap.propertyLabel) ? Cap.propertyLabel(key, null, pluginId) : key;
                return label + ': ' + String(value);
            }).join(', ') + (keys.length > 4 ? ' ...' : '');
        }

        function openEditor(binding) {
            var isEdit = !!binding;
            var Cap = window.DashboardCapabilities;
            var schema = state.pluginSchema;
            var bindingConfig = isEdit ? parseConfigSafe(binding) : (Cap ? Cap.applyDefaults(schema, {}) : {});

            var modal = document.createElement('div');
            var modalId = 'plugin-binding-editor-' + Date.now();
            modal.className = 'modal fade';
            modal.id = modalId;
            modal.tabIndex = -1;
            modal.dataset.bsBackdrop = 'static';
            modal.innerHTML = '<div class="modal-dialog modal-dialog-centered modal-lg"><div class="modal-content">' +
                '<div class="modal-header"><h5 class="modal-title"><i class="bi ' + (isEdit ? 'bi-pencil-square' : 'bi-plus-square') + ' me-2"></i>' + (isEdit ? t('pluginPage.editBinding', 'Edit Binding') : t('pluginPage.addBinding', 'Add Binding')) + '</h5><button type="button" class="btn-close" data-bs-dismiss="modal"></button></div>' +
                '<div class="modal-body">' +
                (isEdit ? '' : '<div class="mb-3"><label class="form-label fw-semibold">' + scopeLabel + '</label><select class="form-select" data-role="scope-select"><option value="">' + t('pluginPage.selectScope', 'Select ') + scopeLabel + '</option></select><div class="form-text">' + t('pluginPage.selectScopeHint', 'Select a ') + ' ' + scopeLabel + '</div></div>') +
                '<div class="form-check form-switch mb-3"><input class="form-check-input" type="checkbox" data-role="enabled" id="' + modalId + '-enabled"' + (isEdit ? (binding.enabled ? ' checked' : '') : ' checked') + '><label class="form-check-label" for="' + modalId + '-enabled">' + t('pluginPage.enableBinding', 'Enable this binding') + '</label></div>' +
                '<div data-role="schema-form"></div>' +
                '<div class="d-none" data-role="json-panel"><label class="form-label fw-semibold">' + t('pluginPage.jsonConfig', 'Advanced JSON Config') + '</label><textarea class="form-control font-monospace" rows="14" data-role="json"></textarea><div class="form-text">' + t('pluginPage.jsonHint', 'Edit JSON directly. Ensure valid format before switching back to form mode.') + '</div></div>' +
                '</div>' +
                '<div class="modal-footer justify-content-between">' +
                '<button type="button" class="btn btn-outline-secondary btn-sm" data-role="mode"><i class="bi bi-braces me-1"></i>JSON</button>' +
                '<div><button type="button" class="btn btn-secondary btn-sm me-2" data-bs-dismiss="modal">' + t('common.cancel', 'Cancel') + '</button><button type="button" class="btn btn-primary btn-sm" data-role="save"><i class="bi bi-check-lg me-1"></i>' + t('common.save', 'Save') + '</button></div>' +
                '</div></div></div>';
            document.body.appendChild(modal);

            var scopeSelect = modal.querySelector('[data-role="scope-select"]');
            var enabledInput = modal.querySelector('[data-role="enabled"]');
            var schemaForm = modal.querySelector('[data-role="schema-form"]');
            var jsonPanel = modal.querySelector('[data-role="json-panel"]');
            var jsonInput = modal.querySelector('[data-role="json"]');
            var modeButton = modal.querySelector('[data-role="mode"]');
            var saveButton = modal.querySelector('[data-role="save"]');
            var advanced = false;

            if (!isEdit && scopeSelect) {
                var boundScopeIds = state.bindings.map(function(b) { return b.scopeId; });
                state.scopes.forEach(function(item) {
                    var itemId = scope === 'Cluster' ? (item.clusterId || item.ClusterId) : (item.routeId || item.RouteId);
                    if (!itemId) return;
                    var isBound = boundScopeIds.indexOf(itemId) >= 0;
                    var opt = new Option(itemId + (isBound ? ' (' + t('pluginPage.alreadyBound', 'Bound') + ')' : ''), itemId, false, false);
                    if (isBound) opt.disabled = true;
                    scopeSelect.appendChild(opt);
                });
                if (scopeSelect.children.length <= 1) {
                    var notice = modal.querySelector('.modal-body');
                    var alertDiv = document.createElement('div');
                    alertDiv.className = 'alert alert-warning';
                    alertDiv.innerHTML = '<i class="bi bi-exclamation-triangle me-1"></i>' + t('pluginPage.noScopes', 'No available') + ' ' + scopeLabel + '. ' + t('pluginPage.createScopeFirst', 'Please create a') + ' ' + scopeLabel + ' first.';
                    scopeSelect.parentElement.insertBefore(alertDiv, scopeSelect);
                }
            }

            function renderForm() {
                schemaForm.replaceChildren();
                if (!schema || !Object.keys(schema.properties || {}).length) {
                    var notice = document.createElement('div');
                    notice.className = 'alert alert-secondary py-2';
                    notice.textContent = t('pluginPage.noSchema', 'This plugin does not provide a visual configuration form. Use JSON mode instead.');
                    schemaForm.appendChild(notice);
                    return;
                }
                if (Cap && Cap.renderGroupedFields) {
                    Cap.renderGroupedFields(schemaForm, schema, bindingConfig, pluginId);
                } else {
                    var required = schema.required || [];
                    schemaForm.__schemaFields = Object.keys(schema.properties).filter(function(name) { return name !== 'enabled'; }).map(function(name) {
                        var field = Cap.schemaField(name, schema.properties[name], bindingConfig[name], required.indexOf(name) >= 0, pluginId);
                        schemaForm.appendChild(field);
                        return { name: name, field: field };
                    });
                }
            }

            function syncToJson() {
                jsonInput.value = JSON.stringify(bindingConfig, null, 2);
            }

            modeButton.addEventListener('click', function() {
                if (!advanced && schema) {
                    var formConfig = Cap.readSchemaForm(schemaForm, schema, bindingConfig);
                    if (formConfig === null) return;
                    bindingConfig = formConfig;
                    syncToJson();
                } else if (advanced) {
                    try {
                        bindingConfig = JSON.parse(jsonInput.value || '{}');
                    } catch (_) {
                        window.DashboardModals.showWarning(t('pluginPage.invalidJson', 'Invalid JSON format'));
                        return;
                    }
                    renderForm();
                }
                advanced = !advanced;
                schemaForm.classList.toggle('d-none', advanced);
                jsonPanel.classList.toggle('d-none', !advanced);
                modeButton.innerHTML = advanced ? '<i class="bi bi-ui-checks-grid me-1"></i>' + t('pluginPage.formMode', 'Form') : '<i class="bi bi-braces me-1"></i>JSON';
            });

            saveButton.addEventListener('click', async function() {
                var scopeId = isEdit ? binding.scopeId : (scopeSelect ? scopeSelect.value : '');
                if (!isEdit && !scopeId) {
                    window.DashboardModals.showWarning(t('pluginPage.scopeRequired', 'Please select') + ' ' + scopeLabel);
                    return;
                }

                if (advanced) {
                    try {
                        bindingConfig = JSON.parse(jsonInput.value || '{}');
                    } catch (_) {
                        window.DashboardModals.showWarning(t('pluginPage.invalidJson', 'Invalid JSON format'));
                        return;
                    }
                } else if (schema && Cap) {
                    var formConfig = Cap.readSchemaForm(schemaForm, schema, bindingConfig);
                    if (formConfig === null) {
                        window.DashboardModals.showWarning(t('pluginPage.validationError', 'Please check form fields'));
                        return;
                    }
                    bindingConfig = formConfig;
                }

                var payload = {
                    pluginId: pluginId,
                    scope: scope,
                    scopeId: scopeId,
                    enabled: enabledInput.checked,
                    configJson: JSON.stringify(bindingConfig),
                    schemaVersion: (Cap && Cap.getPluginSchemaVersion)
                        ? Cap.getPluginSchemaVersion(state.plugin)
                        : 1,
                    order: Number((state.plugin && state.plugin.order) || 0),
                    configVersion: isEdit ? (binding.configVersion || 0) + 1 : 1
                };

                try {
                    saveButton.disabled = true;
                    saveButton.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>' + t('common.saving', 'Saving...');
                    if (isEdit) {
                        await window.DashboardApi.updateBinding(binding.id, payload);
                    } else {
                        await window.DashboardApi.createBinding(payload);
                    }
                    window.DashboardModals.showSuccess(isEdit ? t('pluginPage.saveSuccess', 'Saved') : t('pluginPage.addSuccess', 'Added'));
                    bootstrap.Modal.getInstance(modal).hide();
                    await module.load();
                } catch (error) {
                    window.DashboardModals.showError(t('common.saveFailed', 'Save failed') + ': ' + (error.message || error));
                    saveButton.disabled = false;
                    saveButton.innerHTML = '<i class="bi bi-check-lg me-1"></i>' + t('common.save', 'Save');
                }
            });

            modal.addEventListener('hidden.bs.modal', function() { modal.remove(); });

            renderForm();
            syncToJson();
            new bootstrap.Modal(modal).show();
        }

        window[moduleName] = module;
        return module;
    }

    window.PluginBindingManager = { create: create };
})();
