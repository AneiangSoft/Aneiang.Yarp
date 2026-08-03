(function() {
    'use strict';

    var pluginCache = null;

    function escapeHtml(value) {
        return window.DashboardUtils.escapeHtml(value == null ? '' : String(value));
    }

    function bindingApi(scope, scopeId) {
        return window.DashboardApi.get('/api/plugin-bindings', { scope: scope, scopeId: scopeId });
    }

    function loadPlugins() {
        if (!pluginCache) {
            pluginCache = window.DashboardApi.get('/api/plugin-bindings/plugins').catch(function(error) {
                pluginCache = null;
                throw error;
            });
        }
        return pluginCache;
    }

    function parseConfig(binding) {
        try {
            return JSON.parse(binding.configJson || '{}');
        } catch (_) {
            return {};
        }
    }

    function summarizeConfig(binding) {
        var config = parseConfig(binding);
        var keys = Object.keys(config);
        if (!keys.length) return '{}';
        return keys.slice(0, 3).map(function(key) {
            var value = config[key];
            if (value && typeof value === 'object') value = Array.isArray(value) ? '[' + value.length + ']' : '{...}';
            return key + ': ' + String(value);
        }).join(', ') + (keys.length > 3 ? ' …' : '');
    }

    function pluginMap(plugins) {
        return (plugins || []).reduce(function(result, plugin) {
            result[plugin.pluginId] = plugin;
            return result;
        }, {});
    }

    function showError(error) {
        window.DashboardModals.showError(error && error.message ? error.message : String(error));
    }

    function createBinding(binding) {
        return window.DashboardApi.post('/api/plugin-bindings', binding);
    }

    function updateBinding(binding) {
        var request = Object.assign({}, binding, {
            scope: binding.scope === 1 ? 'Route' : (binding.scope === 2 ? 'Cluster' : binding.scope)
        });
        return window.DashboardApi.put('/api/plugin-bindings/' + encodeURIComponent(binding.id), request);
    }

    function render(container, scope, scopeId, bindings, plugins) {
        var pluginsById = pluginMap(plugins);
        var availablePlugins = (plugins || []).filter(function(plugin) {
            var supportsScope = !plugin.scopes || plugin.scopes.some(function(pluginScope) {
                return String(pluginScope).toLowerCase() === scope.toLowerCase();
            });
            return supportsScope && !(bindings || []).some(function(binding) { return binding.pluginId === plugin.pluginId; });
        });

        var html = '<div class="detail-section">' +
            '<div class="detail-section-title d-flex justify-content-between align-items-center">' +
            '<span><i class="bi bi-puzzle"></i>绑定能力</span>' +
            '<button type="button" class="btn btn-sm btn-outline-primary capability-add"' + (availablePlugins.length ? '' : ' disabled') + '>' +
            '<i class="bi bi-plus-lg"></i> 添加能力</button></div>';

        if (!bindings || !bindings.length) {
            html += '<div class="text-muted small py-2">尚未绑定插件能力。</div>';
        } else {
            html += '<div class="d-flex flex-column gap-2">';
            bindings.forEach(function(binding) {
                var plugin = pluginsById[binding.pluginId];
                var globallyEnabled = !plugin || plugin.enabled !== false;
                var effectiveEnabled = binding.enabled && globallyEnabled;
                var statusText = !globallyEnabled ? '全局禁用' : (binding.enabled ? '已启用' : '已停用');
                var statusClass = effectiveEnabled ? 'bg-success' : (!globallyEnabled ? 'bg-danger' : 'bg-secondary');
                html += '<div class="border rounded-3 p-2 d-flex align-items-center gap-3" data-binding-id="' + escapeHtml(binding.id) + '">' +
                    '<div class="flex-grow-1 min-w-0"><div class="d-flex align-items-center gap-2">' +
                    '<strong>' + escapeHtml((plugin && plugin.displayName) || binding.pluginId) + '</strong>' +
                    '<span class="badge ' + statusClass + '">' + statusText + '</span></div>' +
                    '<div class="small text-muted text-truncate" title="' + escapeHtml(summarizeConfig(binding)) + '">' + escapeHtml(summarizeConfig(binding)) + '</div></div>' +
                    '<div class="btn-group btn-group-sm">' +
                    '<button type="button" class="btn btn-outline-primary capability-edit" title="编辑 JSON 配置"><i class="bi bi-braces"></i></button>' +
                    '<button type="button" class="btn btn-outline-secondary capability-toggle" title="' + (binding.enabled ? '停用' : '启用') + '"' + (!globallyEnabled ? ' disabled' : '') + '><i class="bi ' + (binding.enabled ? 'bi-toggle-on' : 'bi-toggle-off') + '"></i></button>' +
                    '<button type="button" class="btn btn-outline-danger capability-delete" title="删除"><i class="bi bi-trash"></i></button>' +
                    '</div></div>';
            });
            html += '</div>';
        }
        html += '</div>';
        container.innerHTML = html;

        container.querySelector('.capability-add')?.addEventListener('click', function() {
            showAddModal(container, scope, scopeId, bindings, plugins, availablePlugins);
        });
        container.querySelectorAll('[data-binding-id]').forEach(function(row) {
            var binding = bindings.find(function(item) { return item.id === row.dataset.bindingId; });
            row.querySelector('.capability-edit').addEventListener('click', function() { showConfigModal(container, scope, scopeId, binding); });
            row.querySelector('.capability-toggle').addEventListener('click', function() { toggleBinding(container, scope, scopeId, binding); });
            row.querySelector('.capability-delete').addEventListener('click', function() { deleteBinding(container, scope, scopeId, binding); });
        });
    }

    function refresh(container, scope, scopeId) {
        container.innerHTML = '<div class="detail-section"><div class="text-muted small"><span class="spinner-border spinner-border-sm me-2"></span>正在加载绑定能力...</div></div>';
        return Promise.all([bindingApi(scope, scopeId), loadPlugins()])
            .then(function(results) { render(container, scope, scopeId, results[0] || [], results[1] || []); })
            .catch(function(error) {
                container.innerHTML = '<div class="detail-section"><div class="text-danger small">绑定能力加载失败：' + escapeHtml(error.message) + '</div></div>';
            });
    }

    function getPluginSchema(plugin) {
        var schemas = Array.isArray(plugin && plugin.schemas) ? plugin.schemas.slice() : [];
        schemas.sort(function(left, right) { return Number(right.version || 0) - Number(left.version || 0); });
        if (!schemas.length) return null;
        var source = schemas[0].configJsonSchema == null ? schemas[0].ConfigJsonSchema : schemas[0].configJsonSchema;
        if (source && typeof source === 'object') return source;
        try {
            var schema = JSON.parse(source || '{}');
            return schema && schema.type === 'object' ? schema : null;
        } catch (error) {
            console.warn('[DashboardCapabilities] 无法解析插件配置 Schema', plugin && plugin.pluginId, error);
            return null;
        }
    }

    function mergeSchema(base, addition) {
        var result = Object.assign({}, base || {}, addition || {});
        result.properties = Object.assign({}, (base && base.properties) || {}, (addition && addition.properties) || {});
        result.required = Array.from(new Set([].concat((base && base.required) || [], (addition && addition.required) || [])));
        delete result.allOf;
        return result;
    }

    function expandSchema(schema) {
        var result = Object.assign({}, schema || {});
        if (Array.isArray(result.allOf)) result.allOf.forEach(function(part) { result = mergeSchema(result, expandSchema(part)); });
        return result;
    }

    function applyDefaults(schema, value) {
        schema = expandSchema(schema);
        var choices = schema.oneOf || schema.anyOf;
        if (Array.isArray(choices) && choices.length) schema = mergeSchema(schema, expandSchema(choices[0]));
        if (value === undefined && schema.default !== undefined) return structuredClone(schema.default);
        if (schema.type === 'array') {
            return Array.isArray(value) ? value.map(function(item) { return applyDefaults(schema.items || {}, item); }) : (value === undefined ? [] : value);
        }
        if (schema.type === 'object' || schema.properties) {
            var result = value && typeof value === 'object' && !Array.isArray(value) ? Object.assign({}, value) : {};
            Object.keys(schema.properties || {}).forEach(function(name) {
                var next = applyDefaults(schema.properties[name], result[name]);
                if (next !== undefined) result[name] = next;
            });
            return result;
        }
        return value;
    }

    function propertyLabel(name, property) {
        return property.title || name.replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/[_-]+/g, ' ').replace(/^./, function(value) { return value.toUpperCase(); });
    }

    function choiceMatches(value, schema) {
        schema = expandSchema(schema);
        if (schema.type === 'object' && (!value || typeof value !== 'object' || Array.isArray(value))) return false;
        if (schema.type === 'array' && !Array.isArray(value)) return false;
        if (schema.type === 'string' && typeof value !== 'string') return false;
        if ((schema.type === 'number' || schema.type === 'integer') && typeof value !== 'number') return false;
        if (Array.isArray(schema.required) && schema.required.some(function(name) { return !Object.prototype.hasOwnProperty.call(value || {}, name); })) return false;
        if (schema.const !== undefined && JSON.stringify(value) !== JSON.stringify(schema.const)) return false;
        if (Array.isArray(schema.enum) && !schema.enum.some(function(item) { return JSON.stringify(item) === JSON.stringify(value); })) return false;
        return true;
    }

    function schemaField(name, rawProperty, value, required) {
        var property = expandSchema(rawProperty);
        var choices = property.oneOf || property.anyOf;
        if (Array.isArray(choices) && choices.length) {
            var choiceWrapper = document.createElement('fieldset');
            choiceWrapper.className = 'border rounded-3 p-3 mb-3';
            var choiceLabel = document.createElement('label');
            choiceLabel.className = 'form-label fw-semibold';
            choiceLabel.textContent = propertyLabel(name, property) + (required ? ' *' : '');
            var choiceSelect = document.createElement('select');
            choiceSelect.className = 'form-select form-select-sm mb-3';
            choices.forEach(function(choice, index) { choiceSelect.appendChild(new Option(choice.title || ('选项 ' + (index + 1)), String(index))); });
            var selected = choices.findIndex(function(choice) { return choiceMatches(value, choice); });
            choiceSelect.value = String(selected < 0 ? 0 : selected);
            var choiceHost = document.createElement('div');
            var child;
            function renderChoice() {
                var selectedSchema = mergeSchema(property, expandSchema(choices[Number(choiceSelect.value)]));
                delete selectedSchema.oneOf;
                delete selectedSchema.anyOf;
                child = schemaField(name, selectedSchema, value, required);
                choiceHost.replaceChildren(child);
            }
            choiceSelect.addEventListener('change', renderChoice);
            choiceWrapper.append(choiceLabel, choiceSelect, choiceHost);
            choiceWrapper.__read = function() { return child.__read(); };
            renderChoice();
            return choiceWrapper;
        }

        if (property.type === 'object' || property.properties) {
            var objectWrapper = document.createElement('fieldset');
            objectWrapper.className = 'border rounded-3 p-3 mb-3';
            var legend = document.createElement('legend');
            legend.className = 'float-none w-auto px-2 fs-6 fw-semibold';
            legend.textContent = propertyLabel(name, property) + (required ? ' *' : '');
            objectWrapper.appendChild(legend);
            var objectValue = value && typeof value === 'object' && !Array.isArray(value) ? value : {};
            var children = [];
            Object.keys(property.properties || {}).forEach(function(childName) {
                var child = schemaField(childName, property.properties[childName], objectValue[childName], (property.required || []).indexOf(childName) >= 0);
                children.push({ name: childName, field: child });
                objectWrapper.appendChild(child);
            });
            objectWrapper.__read = function() {
                var result = Object.assign({}, objectValue);
                var valid = true;
                children.forEach(function(entry) {
                    var next = entry.field.__read();
                    if (!next.valid) valid = false;
                    else if (next.present) result[entry.name] = next.value;
                    else delete result[entry.name];
                });
                return { valid: valid, present: required || Object.keys(result).length > 0, value: result };
            };
            return objectWrapper;
        }

        if (property.type === 'array' && property.items && (property.items.type === 'object' || property.items.properties || property.items.oneOf || property.items.anyOf || property.items.allOf)) {
            var arrayWrapper = document.createElement('fieldset');
            arrayWrapper.className = 'border rounded-3 p-3 mb-3';
            var arrayLegend = document.createElement('legend');
            arrayLegend.className = 'float-none w-auto px-2 fs-6 fw-semibold';
            arrayLegend.textContent = propertyLabel(name, property) + (required ? ' *' : '');
            var list = document.createElement('div');
            var add = document.createElement('button');
            add.type = 'button';
            add.className = 'btn btn-sm btn-outline-primary';
            add.textContent = '添加项目';
            var rows = [];
            function addRow(item) {
                var row = document.createElement('div');
                row.className = 'border rounded p-2 mb-2 position-relative';
                var field = schemaField('项目 ' + (rows.length + 1), property.items, applyDefaults(property.items, item), true);
                var remove = document.createElement('button');
                remove.type = 'button';
                remove.className = 'btn btn-sm btn-outline-danger mb-2';
                remove.textContent = '删除';
                var entry = { row: row, field: field };
                remove.addEventListener('click', function() { rows.splice(rows.indexOf(entry), 1); row.remove(); });
                row.append(remove, field);
                rows.push(entry);
                list.appendChild(row);
            }
            (Array.isArray(value) ? value : []).forEach(addRow);
            add.addEventListener('click', function() { addRow(undefined); });
            arrayWrapper.append(arrayLegend, list, add);
            arrayWrapper.__read = function() {
                var valid = true;
                var result = rows.map(function(entry) { var next = entry.field.__read(); if (!next.valid) valid = false; return next.value; });
                return { valid: valid, present: required || result.length > 0, value: result };
            };
            return arrayWrapper;
        }

        var wrapper = document.createElement('div');
        wrapper.className = property.type === 'boolean' ? 'mb-3 form-check' : 'mb-3';
        var id = 'capability-' + Math.random().toString(36).slice(2);
        var input;
        if (property.type === 'boolean') {
            input = document.createElement('input');
            input.type = 'checkbox'; input.className = 'form-check-input'; input.checked = value === true;
            var booleanLabel = document.createElement('label');
            booleanLabel.className = 'form-check-label'; booleanLabel.htmlFor = id; booleanLabel.textContent = propertyLabel(name, property);
            wrapper.append(input, booleanLabel);
        } else {
            var label = document.createElement('label');
            label.className = 'form-label'; label.htmlFor = id; label.textContent = propertyLabel(name, property) + (required ? ' *' : '');
            if (Array.isArray(property.enum)) {
                input = document.createElement('select'); input.className = 'form-select';
                if (!required) input.appendChild(new Option('', ''));
                property.enum.forEach(function(optionValue) { input.appendChild(new Option(String(optionValue), String(optionValue), false, JSON.stringify(optionValue) === JSON.stringify(value))); });
            } else if (property.type === 'array') {
                input = document.createElement('textarea'); input.className = 'form-control'; input.rows = 4;
                input.value = Array.isArray(value) ? value.join('\n') : ''; input.placeholder = '每行一个值';
            } else {
                input = document.createElement('input'); input.className = 'form-control';
                input.type = property.type === 'number' || property.type === 'integer' ? 'number' : 'text'; input.value = value == null ? '' : value;
                if (property.type === 'integer') input.step = '1'; else if (property.type === 'number') input.step = property.multipleOf || 'any';
                if (property.minimum !== undefined) input.min = property.minimum; if (property.maximum !== undefined) input.max = property.maximum;
                if (property.minLength !== undefined) input.minLength = property.minLength; if (property.maxLength !== undefined) input.maxLength = property.maxLength;
                if (property.pattern) input.pattern = property.pattern;
                if (property.format === 'duration') { input.placeholder = '00:01:00'; input.dataset.duration = 'true'; }
            }
            input.required = required;
            wrapper.append(label, input);
        }
        input.id = id;
        if (property.description) { var help = document.createElement('div'); help.className = 'form-text'; help.textContent = property.description; wrapper.appendChild(help); }
        wrapper.__read = function() {
            var raw = input.type === 'checkbox' ? input.checked : input.value.trim();
            input.setCustomValidity('');
            if (input.dataset.duration === 'true' && raw && !/^(?:\d+\.)?\d{1,2}:\d{2}:\d{2}(?:\.\d{1,7})?$/.test(raw)) input.setCustomValidity('请输入如 00:01:00 的时长');
            if (property.type === 'integer' && raw !== '' && !Number.isInteger(Number(raw))) input.setCustomValidity('请输入整数');
            if (!input.checkValidity()) { input.classList.add('is-invalid'); return { valid: false }; }
            input.classList.remove('is-invalid');
            if (property.type === 'boolean') return { valid: true, present: true, value: raw };
            if (raw === '' && !required) return { valid: true, present: false };
            if (property.type === 'number' || property.type === 'integer') return { valid: true, present: true, value: Number(raw) };
            if (property.type === 'array') return { valid: true, present: true, value: raw.split(/\r?\n/).map(function(item) { return item.trim(); }).filter(Boolean) };
            if (Array.isArray(property.enum)) return { valid: true, present: true, value: property.enum.find(function(item) { return String(item) === raw; }) };
            return { valid: true, present: true, value: raw };
        };
        return wrapper;
    }

    function readSchemaForm(form, schema, original) {
        var result = Object.assign({}, original || {});
        var valid = true;
        (form.__schemaFields || []).forEach(function(entry) {
            var next = entry.field.__read();
            if (!next.valid) valid = false;
            else if (next.present) result[entry.name] = next.value;
            else delete result[entry.name];
        });
        if (!valid) form.querySelector(':invalid')?.reportValidity();
        return valid ? result : null;
    }

    function showAddModal(container, scope, scopeId, bindings, plugins, availablePlugins) {
        showConfigModal(container, scope, scopeId, null, availablePlugins, plugins);
    }

    function showConfigModal(container, scope, scopeId, binding, candidates, allPlugins) {
        candidates = candidates || (pluginCache && []);
        var open = function(plugins) {
            var selectable = candidates && candidates.length ? candidates : plugins.filter(function(plugin) { return plugin.pluginId === binding.pluginId; });
            var modal = document.createElement('div');
            var modalId = 'capability-editor-' + Date.now();
            modal.className = 'modal fade';
            modal.id = modalId;
            modal.tabIndex = -1;
            modal.dataset.bsBackdrop = 'static';
            modal.innerHTML = '<div class="modal-dialog modal-dialog-centered modal-lg"><div class="modal-content">' +
                '<div class="modal-header"><h5 class="modal-title"><i class="bi bi-puzzle me-2"></i>' + (binding ? '编辑能力配置' : '添加绑定能力') + '</h5><button type="button" class="btn-close" data-bs-dismiss="modal"></button></div>' +
                '<div class="modal-body"><div class="mb-3"><label class="form-label">插件</label><select class="form-select" data-role="plugin"></select></div>' +
                '<div class="form-check mb-3"><input class="form-check-input" type="checkbox" data-role="enabled" id="' + modalId + '-enabled"><label class="form-check-label" for="' + modalId + '-enabled">启用此绑定</label></div>' +
                '<div data-role="schema-form"></div><div class="d-none" data-role="json-panel"><label class="form-label">高级 JSON 配置</label><textarea class="form-control font-monospace" rows="14" data-role="json"></textarea></div></div>' +
                '<div class="modal-footer justify-content-between"><button type="button" class="btn btn-outline-secondary btn-sm" data-role="mode"><i class="bi bi-braces me-1"></i>JSON</button>' +
                '<div><button type="button" class="btn btn-secondary btn-sm me-2" data-bs-dismiss="modal">取消</button><button type="button" class="btn btn-primary btn-sm" data-role="save"><i class="bi bi-check-lg me-1"></i>保存</button></div></div></div></div>';
            document.body.appendChild(modal);

            var pluginSelect = modal.querySelector('[data-role="plugin"]');
            var enabledInput = modal.querySelector('[data-role="enabled"]');
            var schemaForm = modal.querySelector('[data-role="schema-form"]');
            var jsonPanel = modal.querySelector('[data-role="json-panel"]');
            var jsonInput = modal.querySelector('[data-role="json"]');
            var modeButton = modal.querySelector('[data-role="mode"]');
            var saveButton = modal.querySelector('[data-role="save"]');
            var advanced = false;
            var config = binding ? parseConfig(binding) : {};
            var schema = null;

            selectable.forEach(function(plugin) { pluginSelect.appendChild(new Option(plugin.displayName || plugin.pluginId, plugin.pluginId, false, plugin.pluginId === (binding && binding.pluginId))); });
            pluginSelect.disabled = !!binding;
            enabledInput.checked = binding ? binding.enabled : true;

            function renderForm(reset) {
                var plugin = plugins.find(function(item) { return item.pluginId === pluginSelect.value; });
                schema = getPluginSchema(plugin);
                config = applyDefaults(schema, reset ? {} : config);
                schemaForm.replaceChildren();
                if (!schema || !Object.keys(schema.properties || {}).length) {
                    var notice = document.createElement('div');
                    notice.className = 'alert alert-secondary py-2';
                    notice.textContent = '此插件未提供可视化配置 Schema，可使用 JSON 模式配置。';
                    schemaForm.appendChild(notice);
                } else {
                    var required = schema.required || [];
                    schemaForm.__schemaFields = Object.keys(schema.properties).map(function(name) {
                        var field = schemaField(name, schema.properties[name], config[name], required.indexOf(name) >= 0);
                        schemaForm.appendChild(field);
                        return { name: name, field: field };
                    });
                }
                jsonInput.value = JSON.stringify(config, null, 2);
            }

            pluginSelect.addEventListener('change', function() { renderForm(true); });
            modeButton.addEventListener('click', function() {
                if (!advanced && schema) {
                    var formConfig = readSchemaForm(schemaForm, schema, config);
                    if (formConfig === null) return;
                    config = formConfig;
                    jsonInput.value = JSON.stringify(config, null, 2);
                } else if (advanced) {
                    try { config = JSON.parse(jsonInput.value || '{}'); }
                    catch (_) { window.DashboardModals.showWarning('JSON 配置格式无效'); return; }
                    renderForm(false);
                }
                advanced = !advanced;
                schemaForm.classList.toggle('d-none', advanced);
                jsonPanel.classList.toggle('d-none', !advanced);
                modeButton.innerHTML = advanced ? '<i class="bi bi-ui-checks-grid me-1"></i>表单' : '<i class="bi bi-braces me-1"></i>JSON';
            });
            saveButton.addEventListener('click', async function() {
                if (advanced) {
                    try { config = JSON.parse(jsonInput.value || '{}'); }
                    catch (_) { window.DashboardModals.showWarning('JSON 配置格式无效'); return; }
                } else if (schema) {
                    var formConfig = readSchemaForm(schemaForm, schema, config);
                    if (formConfig === null) return;
                    config = formConfig;
                }
                var plugin = plugins.find(function(item) { return item.pluginId === pluginSelect.value; });
                var payload = binding ? Object.assign({}, binding) : {
                    pluginId: pluginSelect.value, scope: scope, scopeId: scopeId,
                    schemaVersion: Number((plugin && plugin.schemas && plugin.schemas[0] && plugin.schemas[0].version) || 1), order: Number((plugin && plugin.order) || 0)
                };
                payload.enabled = plugin && plugin.enabled === false ? false : enabledInput.checked;
                payload.configJson = JSON.stringify(config);
                payload.configVersion = binding ? (binding.configVersion || 0) + 1 : 1;
                try {
                    if (binding) await updateBinding(payload); else await createBinding(payload);
                    window.DashboardModals.showSuccess(binding ? '能力配置已保存' : '能力绑定已添加');
                    await refresh(container, scope, scopeId);
                    bootstrap.Modal.getInstance(modal).hide();
                } catch (error) { showError(error); }
            });
            modal.addEventListener('hidden.bs.modal', function() { modal.remove(); });
            renderForm(!binding);
            new bootstrap.Modal(modal).show();
        };
        if (allPlugins) open(allPlugins); else loadPlugins().then(open).catch(showError);
    }

    async function toggleBinding(container, scope, scopeId, binding) {
        try {
            await updateBinding(Object.assign({}, binding, { enabled: !binding.enabled }));
            window.DashboardModals.showSuccess(binding.enabled ? '能力绑定已停用' : '能力绑定已启用');
            await refresh(container, scope, scopeId);
        } catch (error) {
            showError(error);
        }
    }

    function deleteBinding(container, scope, scopeId, binding) {
        window.DashboardModals.showConfirm('确定删除能力绑定“' + binding.pluginId + '”吗？', async function() {
            try {
                await window.DashboardApi.delete('/api/plugin-bindings/' + encodeURIComponent(binding.id));
                window.DashboardModals.showSuccess('能力绑定已删除');
                await refresh(container, scope, scopeId);
            } catch (error) {
                showError(error);
            }
        }, null, { title: '删除能力绑定', danger: true });
    }

    window.DashboardCapabilities = {
        mount: function(container, scope, scopeId) {
            if (container) refresh(container, scope, scopeId);
        }
    };
})();
