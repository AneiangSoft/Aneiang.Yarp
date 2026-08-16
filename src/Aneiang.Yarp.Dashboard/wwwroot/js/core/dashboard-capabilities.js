(function() {
    'use strict';

    var pluginCache = null;
    var activePluginId = null;

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
        var pid = binding && binding.pluginId;
        return keys.slice(0, 3).map(function(key) {
            var value = config[key];
            if (value && typeof value === 'object') value = Array.isArray(value) ? '[' + value.length + ']' : '{...}';
            var label = propertyLabel(key, null, pid);
            return label + ': ' + String(value);
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
            '<span><i class="bi bi-puzzle"></i>' + __('capability.title') + '</span>' +
            '<button type="button" class="btn btn-sm btn-outline-primary capability-add"' + (availablePlugins.length ? '' : ' disabled') + '>' +
            '<i class="bi bi-plus-lg"></i> ' + __('capability.add') + '</button></div>';

        html += '<div class="small text-muted mb-2"><i class="bi bi-info-circle me-1"></i>' + __('capability.help') + '</div>';

        if (!bindings || !bindings.length) {
            html += '<div class="text-center py-4 border rounded-3 bg-light">' +
                '<i class="bi bi-inbox display-6 d-block mb-2 opacity-25"></i>' +
                '<p class="text-muted mb-1">' + __('capability.empty') + '</p>' +
                (availablePlugins.length ? '<p class="small text-muted mb-2">' + __('capability.emptyHint') + '</p>' +
                '<button type="button" class="btn btn-sm btn-outline-primary capability-add-empty">' +
                '<i class="bi bi-plus-lg me-1"></i>' + __('capability.add') + '</button>' : '') +
                '</div>';
        } else {
            html += '<div class="d-flex flex-column gap-2">';
            bindings.forEach(function(binding) {
                var plugin = pluginsById[binding.pluginId];
                var globallyEnabled = !plugin || plugin.enabled !== false;
                var effectiveEnabled = binding.enabled && globallyEnabled;
                var statusText = !globallyEnabled ? __('capability.status.globallyDisabled') : (binding.enabled ? __('capability.status.enabled') : __('capability.status.disabled'));
                var statusClass = effectiveEnabled ? 'bg-success' : (!globallyEnabled ? 'bg-danger' : 'bg-secondary');
                html += '<div class="border rounded-3 p-2 d-flex align-items-center gap-3" data-binding-id="' + escapeHtml(binding.id) + '">' +
                    '<div class="flex-grow-1 min-w-0"><div class="d-flex align-items-center gap-2">' +
                    '<strong>' + escapeHtml((plugin && plugin.displayName) || binding.pluginId) + '</strong>' +
                    '<span class="badge ' + statusClass + '">' + statusText + '</span></div>' +
                    '<div class="small text-muted text-truncate" title="' + escapeHtml(summarizeConfig(binding)) + '">' + escapeHtml(summarizeConfig(binding)) + '</div></div>' +
                    '<div class="btn-group btn-group-sm">' +
                    '<button type="button" class="btn btn-outline-primary capability-edit" title="' + __('capability.action.editJson') + '"><i class="bi bi-braces"></i></button>' +
                    '<button type="button" class="btn btn-outline-secondary capability-toggle" title="' + (binding.enabled ? __('capability.action.disable') : __('capability.action.enable')) + '"' + (!globallyEnabled ? ' disabled' : '') + '><i class="bi ' + (binding.enabled ? 'bi-toggle-on' : 'bi-toggle-off') + '"></i></button>' +
                    '<button type="button" class="btn btn-outline-danger capability-delete" title="' + __('capability.action.delete') + '"><i class="bi bi-trash"></i></button>' +
                    '</div></div>';
            });
            html += '</div>';
        }
        html += '</div>';
        container.innerHTML = html;

        container.querySelector('.capability-add')?.addEventListener('click', function() {
            showAddModal(container, scope, scopeId, bindings, plugins, availablePlugins);
        });
        container.querySelector('.capability-add-empty')?.addEventListener('click', function() {
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
        container.innerHTML = '<div class="detail-section"><div class="text-muted small"><span class="spinner-border spinner-border-sm me-2"></span>' + __('capability.loading') + '</div></div>';
        return Promise.all([bindingApi(scope, scopeId), loadPlugins()])
            .then(function(results) { render(container, scope, scopeId, results[0] || [], results[1] || []); })
            .catch(function(error) {
                container.innerHTML = '<div class="detail-section"><div class="text-danger small">' + __('capability.loadFailed') + escapeHtml(error.message) + '</div></div>';
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
            console.warn('[DashboardCapabilities] ' + __('capability.schemaParseFailed'), plugin && plugin.pluginId, error);
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

    function i18nDict() {
        var dashboard = window.__dashboard;
        return (dashboard && dashboard.I18N) || {};
    }

    function localize(key) {
        var value = i18nDict()[key];
        return (value === undefined || value === null || value === '') ? null : value;
    }

    function resolvePluginId(pluginId) {
        return pluginId || activePluginId;
    }

    function localizeFieldLabel(pluginId, name) {
        var pid = resolvePluginId(pluginId);
        if (pid) {
            var specific = localize('schema.' + pid + '.' + name);
            if (specific) return specific;
        }
        return localize('schema.common.' + name);
    }

    function localizeFieldDesc(pluginId, name) {
        var pid = resolvePluginId(pluginId);
        if (pid) {
            var specific = localize('schema.' + pid + '.' + name + '.desc');
            if (specific) return specific;
        }
        return localize('schema.common.' + name + '.desc');
    }

    function localizeEnum(pluginId, name, enumValue) {
        var pid = resolvePluginId(pluginId);
        if (pid) {
            var specific = localize('schema.' + pid + '.' + name + '.' + enumValue);
            if (specific) return specific;
        }
        return localize('schema.common.' + name + '.' + enumValue);
    }

    // Plugin metadata: icon, accent color and common (frequently used) fields for form grouping.
    var PLUGIN_META = {
        'rate-limit': { icon: 'bi-speedometer2', color: '#0dcaf0', common: ['algorithm', 'permitLimit', 'window'] },
        'rate-limit-redis': { icon: 'bi-hdd-network', color: '#f4501e', common: ['redisConnectionString', 'algorithm', 'limit', 'windowSeconds'] },
        'waf': { icon: 'bi-shield-lock', color: '#dc3545', common: ['enableSqlInjectionDetection', 'enableXssDetection', 'enablePathTraversalDetection', 'enableRequestSizeValidation', 'maxRequestBodySize'] },
        'circuit-breaker': { icon: 'bi-electrical-socket', color: '#fd7e14', common: ['failureThreshold', 'recoveryTimeoutSeconds'] },
        'request-retry': { icon: 'bi-arrow-repeat', color: '#20c997', common: ['maxRetries', 'backoffBaseMs'] },
        'response-cache': { icon: 'bi-hdd-stack', color: '#0d6efd', common: ['ttlSeconds'] },
        'compression': { icon: 'bi-file-zip', color: '#198754', common: ['compressionLevel', 'minResponseSize'] },
        'service-discovery': { icon: 'bi-diagram-3', color: '#6f42c1', common: ['mode', 'endpoint', 'staticEndpoints'] },
        'proxy-log': { icon: 'bi-journal-text', color: '#6c757d', common: ['errorsOnly', 'samplingEnabled', 'samplingRate'] },
        'traffic-metrics': { icon: 'bi-graph-up-arrow', color: '#d63384', common: [] },
        'cluster-metrics': { icon: 'bi-clipboard-data', color: '#3d8bfd', common: [] }
    };

    function pluginMeta(pluginId) {
        return PLUGIN_META[resolvePluginId(pluginId)] || null;
    }

    // Localized card title/description derived from "Name - description" i18n text.
    function pluginCardText(plugin) {
        var id = plugin.pluginId;
        var raw = localize('pluginPage.desc.' + id) || plugin.description || '';
        var split = raw.indexOf(' - ');
        return {
            title: split > 0 ? raw.slice(0, split) : (plugin.displayName || id),
            desc: split > 0 ? raw.slice(split + 3) : raw
        };
    }

    function propertyLabel(name, property, pluginId) {
        var localized = localizeFieldLabel(pluginId, name);
        if (localized) return localized;
        if (property && property.title) return property.title;
        return name.replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/[_-]+/g, ' ').replace(/^./, function(value) { return value.toUpperCase(); });
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

    function schemaField(name, rawProperty, value, required, pluginId) {
        var property = expandSchema(rawProperty);
        var choices = property.oneOf || property.anyOf;
        if (Array.isArray(choices) && choices.length) {
            var choiceWrapper = document.createElement('fieldset');
            choiceWrapper.className = 'border rounded-3 p-3 mb-3';
            var choiceLabel = document.createElement('label');
            choiceLabel.className = 'form-label fw-semibold';
            choiceLabel.textContent = propertyLabel(name, property, pluginId) + (required ? ' *' : '');
            var choiceSelect = document.createElement('select');
            choiceSelect.className = 'form-select form-select-sm mb-3';
            choices.forEach(function(choice, index) { choiceSelect.appendChild(new Option(choice.title || __('capability.choice.option', { n: index + 1 }), String(index))); });
            var selected = choices.findIndex(function(choice) { return choiceMatches(value, choice); });
            choiceSelect.value = String(selected < 0 ? 0 : selected);
            var choiceHost = document.createElement('div');
            var child;
            function renderChoice() {
                var selectedSchema = mergeSchema(property, expandSchema(choices[Number(choiceSelect.value)]));
                delete selectedSchema.oneOf;
                delete selectedSchema.anyOf;
                child = schemaField(name, selectedSchema, value, required, pluginId);
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
            legend.textContent = propertyLabel(name, property, pluginId) + (required ? ' *' : '');
            objectWrapper.appendChild(legend);
            var objectValue = value && typeof value === 'object' && !Array.isArray(value) ? value : {};
            var children = [];
            Object.keys(property.properties || {}).forEach(function(childName) {
                var child = schemaField(childName, property.properties[childName], objectValue[childName], (property.required || []).indexOf(childName) >= 0, pluginId);
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
            arrayLegend.textContent = propertyLabel(name, property, pluginId) + (required ? ' *' : '');
            var list = document.createElement('div');
            var add = document.createElement('button');
            add.type = 'button';
            add.className = 'btn btn-sm btn-outline-primary';
            add.textContent = __('capability.array.addItem');
            var rows = [];
            function addRow(item) {
                var row = document.createElement('div');
                row.className = 'border rounded p-2 mb-2 position-relative';
                var field = schemaField(__('capability.array.item', { n: rows.length + 1 }), property.items, applyDefaults(property.items, item), true, pluginId);
                var remove = document.createElement('button');
                remove.type = 'button';
                remove.className = 'btn btn-sm btn-outline-danger mb-2';
                remove.textContent = __('capability.array.remove');
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
            booleanLabel.className = 'form-check-label'; booleanLabel.htmlFor = id; booleanLabel.textContent = propertyLabel(name, property, pluginId);
            wrapper.append(input, booleanLabel);
        } else {
            var label = document.createElement('label');
            label.className = 'form-label'; label.htmlFor = id; label.textContent = propertyLabel(name, property, pluginId) + (required ? ' *' : '');
            if (Array.isArray(property.enum)) {
                input = document.createElement('select'); input.className = 'form-select';
                if (!required) input.appendChild(new Option('', ''));
                property.enum.forEach(function(optionValue) {
                    var optionLabel = localizeEnum(pluginId, name, optionValue) || String(optionValue);
                    input.appendChild(new Option(optionLabel, String(optionValue), false, JSON.stringify(optionValue) === JSON.stringify(value)));
                });
            } else if (property.type === 'array') {
                input = document.createElement('textarea'); input.className = 'form-control'; input.rows = 4;
                input.value = Array.isArray(value) ? value.join('\n') : ''; input.placeholder = __('capability.placeholder.onePerLine');
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
        var fieldDescription = localizeFieldDesc(pluginId, name) || property.description;
        if (fieldDescription) { var help = document.createElement('div'); help.className = 'form-text'; help.textContent = fieldDescription; wrapper.appendChild(help); }
        wrapper.__read = function() {
            var raw = input.type === 'checkbox' ? input.checked : input.value.trim();
            input.setCustomValidity('');
            if (input.dataset.duration === 'true' && raw && !/^(?:\d+\.)?\d{1,2}:\d{2}:\d{2}(?:\.\d{1,7})?$/.test(raw)) input.setCustomValidity(__('capability.validation.durationFormat'));
            if (property.type === 'integer' && raw !== '' && !Number.isInteger(Number(raw))) input.setCustomValidity(__('capability.validation.integer'));
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

    var advancedSectionSeq = 0;

    // Renders a schema form with common fields first and the rest folded into an "Advanced" collapse.
    // Shared by the capability modal and the plugin page binding editor.
    function renderGroupedFields(formEl, schema, config, pluginId) {
        var properties = (schema && schema.properties) || {};
        // 'enabled' is owned by the binding-level toggle; hide it from the form (still editable in JSON mode).
        var names = Object.keys(properties).filter(function(name) { return name !== 'enabled'; });
        formEl.replaceChildren();
        if (!names.length) {
            var emptyNotice = document.createElement('div');
            emptyNotice.className = 'alert alert-secondary py-2';
            emptyNotice.textContent = __('capability.form.noEditableFields');
            formEl.appendChild(emptyNotice);
            formEl.__schemaFields = [];
            return true;
        }
        var meta = pluginMeta(pluginId);
        var commonList = (meta && meta.common) || [];
        var commonNames = [];
        commonList.forEach(function(fieldName) {
            if (names.indexOf(fieldName) >= 0 && commonNames.indexOf(fieldName) < 0) commonNames.push(fieldName);
        });
        var restNames = names.filter(function(fieldName) { return commonNames.indexOf(fieldName) < 0; });
        var required = (schema && schema.required) || [];
        var entries = [];
        function appendFields(host, list) {
            list.forEach(function(fieldName) {
                var field = schemaField(fieldName, properties[fieldName], config ? config[fieldName] : undefined, required.indexOf(fieldName) >= 0, pluginId);
                host.appendChild(field);
                entries.push({ name: fieldName, field: field });
            });
        }
        if (commonNames.length && restNames.length >= 3) {
            appendFields(formEl, commonNames);
            var collapseId = 'capability-advanced-' + (++advancedSectionSeq);
            var toggle = document.createElement('button');
            toggle.type = 'button';
            toggle.className = 'btn btn-sm btn-outline-secondary w-100 capability-advanced-toggle';
            toggle.setAttribute('data-bs-toggle', 'collapse');
            toggle.setAttribute('data-bs-target', '#' + collapseId);
            toggle.setAttribute('aria-expanded', 'false');
            toggle.innerHTML = '<i class="bi bi-sliders2 me-1"></i>' + __('capability.form.advancedCount', { count: restNames.length });
            var collapsible = document.createElement('div');
            collapsible.id = collapseId;
            collapsible.className = 'collapse';
            appendFields(collapsible, restNames);
            var section = document.createElement('div');
            section.className = 'capability-advanced';
            section.append(toggle, collapsible);
            formEl.appendChild(section);
        } else {
            appendFields(formEl, commonNames.concat(restNames));
        }
        formEl.__schemaFields = entries;
        return true;
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
        if (!valid) {
            // Reveal folded sections so the invalid field becomes visible before reporting.
            form.querySelectorAll('.collapse:not(.show)').forEach(function(section) {
                if (window.bootstrap && bootstrap.Collapse) bootstrap.Collapse.getOrCreateInstance(section, { toggle: false }).show();
            });
            form.querySelector(':invalid')?.reportValidity();
        }
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
                '<div class="modal-header"><h5 class="modal-title"><i class="bi bi-puzzle me-2"></i>' + (binding ? __('capability.modal.editTitle') : __('capability.modal.addTitle')) + '</h5><button type="button" class="btn-close" data-bs-dismiss="modal"></button></div>' +
                '<div class="modal-body"><div class="mb-3"><label class="form-label">' + __('capability.modal.plugin') + '</label>' +
                (!binding ? '<div class="form-text mt-0 mb-1"><i class="bi bi-hand-index-thumb me-1"></i>' + __('capability.modal.selectPluginHint') + '</div>' : '') +
                '<select class="form-select d-none" data-role="plugin"></select>' +
                '<div class="row g-2" data-role="plugin-cards"></div>' +
                '<div class="small mt-2" data-role="plugin-info"></div></div>' +
                '<div class="mb-3 d-flex align-items-end gap-2"><div class="flex-grow-1"><label class="form-label">' + __('capability.modal.preset') + '</label><select class="form-select form-select-sm" data-role="preset"><option value="">' + __('capability.modal.noPreset') + '</option></select></div>' +
                '<button type="button" class="btn btn-outline-secondary btn-sm" data-role="save-preset" title="' + __('capability.modal.savePresetTitle') + '"><i class="bi bi-bookmark-plus me-1"></i>' + __('capability.modal.savePreset') + '</button></div>' +
                '<div class="form-check mb-3"><input class="form-check-input" type="checkbox" data-role="enabled" id="' + modalId + '-enabled"><label class="form-check-label" for="' + modalId + '-enabled">' + __('capability.modal.enableBinding') + '</label></div>' +
                '<div data-role="schema-form"></div><div class="d-none" data-role="json-panel"><label class="form-label">' + __('capability.modal.jsonConfig') + '</label><textarea class="form-control font-monospace" rows="14" data-role="json"></textarea></div></div>' +
                '<div class="modal-footer justify-content-between"><button type="button" class="btn btn-outline-secondary btn-sm" data-role="mode"><i class="bi bi-braces me-1"></i>' + __('capability.modal.jsonMode') + '</button>' +
                '<div><button type="button" class="btn btn-secondary btn-sm me-2" data-bs-dismiss="modal">' + __('common.cancel') + '</button><button type="button" class="btn btn-primary btn-sm" data-role="save"><i class="bi bi-check-lg me-1"></i>' + __('common.save') + '</button></div></div></div></div>';
            document.body.appendChild(modal);

            var pluginSelect = modal.querySelector('[data-role="plugin"]');
            var pluginCards = modal.querySelector('[data-role="plugin-cards"]');
            var pluginInfo = modal.querySelector('[data-role="plugin-info"]');
            var enabledInput = modal.querySelector('[data-role="enabled"]');
            var schemaForm = modal.querySelector('[data-role="schema-form"]');
            var jsonPanel = modal.querySelector('[data-role="json-panel"]');
            var jsonInput = modal.querySelector('[data-role="json"]');
            var modeButton = modal.querySelector('[data-role="mode"]');
            var saveButton = modal.querySelector('[data-role="save"]');
            var presetSelect = modal.querySelector('[data-role="preset"]');
            var savePresetButton = modal.querySelector('[data-role="save-preset"]');
            var advanced = false;
            var config = binding ? parseConfig(binding) : {};
            var schema = null;
            var currentPresets = [];

            function renderPluginInfo() {
                var plugin = plugins.find(function(item) { return item.pluginId === pluginSelect.value; });
                if (!plugin || plugin.enabled !== false) { pluginInfo.innerHTML = ''; return; }
                pluginInfo.innerHTML = '<div class="text-danger mt-1"><i class="bi bi-exclamation-triangle-fill me-1"></i>' + __('capability.modal.pluginDisabledWarning') + '</div>';
            }

            // Card-based plugin selector; the hidden select remains the state holder.
            function renderPluginCards() {
                pluginCards.replaceChildren();
                selectable.forEach(function(plugin) {
                    var meta = pluginMeta(plugin.pluginId);
                    var texts = pluginCardText(plugin);
                    var scopes = Array.isArray(plugin.scopes) ? plugin.scopes : [plugin.scope || 'Route'];
                    var badges = scopes.map(function(scope) {
                        var scopeLabel = localize('capability.modal.pluginScope.' + scope) || scope;
                        return '<span class="badge text-bg-light border">' + escapeHtml(scopeLabel) + '</span>';
                    }).join(' ');
                    if (plugin.enabled === false) badges += '<span class="badge text-bg-danger">' + __('capability.status.globallyDisabled') + '</span>';
                    var col = document.createElement('div');
                    col.className = 'col-12 col-md-6';
                    col.innerHTML = '<div class="capability-plugin-card' + (plugin.pluginId === pluginSelect.value ? ' selected' : '') + (pluginSelect.disabled ? ' locked' : '') + '" role="button" tabindex="' + (pluginSelect.disabled ? '-1' : '0') + '">' +
                        '<div class="d-flex align-items-start gap-2">' +
                        '<span class="capability-plugin-icon"' + (meta && meta.color ? ' style="color:' + meta.color + '"' : '') + '><i class="bi ' + ((meta && meta.icon) || 'bi-puzzle') + '"></i></span>' +
                        '<span class="flex-grow-1 min-w-0">' +
                        '<span class="d-block fw-semibold small">' + escapeHtml(texts.title) + '</span>' +
                        '<span class="capability-plugin-desc">' + escapeHtml(texts.desc) + '</span>' +
                        '</span></div>' +
                        '<div class="d-flex gap-1 mt-2 flex-wrap">' + badges + '</div></div>';
                    var card = col.firstElementChild;
                    function pick() {
                        if (pluginSelect.disabled || pluginSelect.value === plugin.pluginId) return;
                        pluginSelect.value = plugin.pluginId;
                        pluginSelect.dispatchEvent(new Event('change', { bubbles: true }));
                    }
                    card.addEventListener('click', pick);
                    card.addEventListener('keydown', function(event) {
                        if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); pick(); }
                    });
                    pluginCards.appendChild(col);
                });
            }

            selectable.forEach(function(plugin) { pluginSelect.appendChild(new Option(plugin.displayName || plugin.pluginId, plugin.pluginId, false, plugin.pluginId === (binding && binding.pluginId))); });
            pluginSelect.disabled = !!binding;
            renderPluginCards();
            enabledInput.checked = binding ? binding.enabled : true;

            function renderForm(reset) {
                var plugin = plugins.find(function(item) { return item.pluginId === pluginSelect.value; });
                schema = getPluginSchema(plugin);
                config = applyDefaults(schema, reset ? {} : config);
                schemaForm.replaceChildren();
                if (!schema || !Object.keys(schema.properties || {}).length) {
                    var notice = document.createElement('div');
                    notice.className = 'alert alert-secondary py-2';
                    notice.textContent = __('capability.modal.noSchema');
                    schemaForm.appendChild(notice);
                } else {
                    renderGroupedFields(schemaForm, schema, config, pluginSelect.value);
                }
                jsonInput.value = JSON.stringify(config, null, 2);
                renderPluginInfo();
            }

            pluginSelect.addEventListener('change', function() { renderPluginCards(); renderForm(true); loadPresetsForPlugin(); });

            async function loadPresetsForPlugin() {
                var pluginId = pluginSelect.value;
                if (!pluginId) { presetSelect.innerHTML = '<option value="">' + __('capability.modal.noPreset') + '</option>'; return; }
                try {
                    var presets = await window.DashboardApi.getPresets(pluginId);
                    currentPresets = Array.isArray(presets) ? presets : (presets && presets.data) || [];
                    presetSelect.innerHTML = '<option value="">' + __('capability.modal.noPreset') + '</option>' +
                        currentPresets.map(function(p) {
                            return '<option value="' + escapeHtml(p.id) + '">' + escapeHtml(p.name) + '</option>';
                        }).join('');
                } catch (_) { currentPresets = []; }
            }

            presetSelect.addEventListener('change', function() {
                var presetId = presetSelect.value;
                if (!presetId) return;
                var preset = currentPresets.find(function(p) { return p.id === presetId; });
                if (!preset) return;
                try {
                    config = JSON.parse(preset.configJson || '{}');
                } catch (_) { window.DashboardModals.showWarning(__('capability.modal.presetParseFailed')); return; }
                renderForm(false);
                window.DashboardModals.showSuccess(__('capability.presetLoaded', { name: preset.name }));
            });

            savePresetButton.addEventListener('click', function() {
                var pluginId = pluginSelect.value;
                if (!pluginId) { window.DashboardModals.showWarning(__('capability.modal.selectPluginFirst')); return; }
                var currentConfig = config;
                if (!advanced && schema) {
                    var formConfig = readSchemaForm(schemaForm, schema, config);
                    if (formConfig === null) return;
                    currentConfig = formConfig;
                } else if (advanced) {
                    try { currentConfig = JSON.parse(jsonInput.value || '{}'); }
                    catch (_) { window.DashboardModals.showWarning(__('capability.modal.invalidJson')); return; }
                }
                var defaultName = (binding && binding.pluginId) + ' ' + __('capability.presetSuffix');
                var name = window.prompt(__('capability.modal.presetNamePrompt'), defaultName);
                if (!name) return;
                window.DashboardApi.savePreset({
                    name: name,
                    pluginId: pluginId,
                    configJson: JSON.stringify(currentConfig),
                    schemaVersion: Number((schema && schema.version) || (binding && binding.schemaVersion) || 1)
                }).then(function() {
                    window.DashboardModals.showSuccess(__('capability.presetSaveSuccess'));
                    loadPresetsForPlugin();
                }).catch(function(error) { showError(error); });
            });

            modeButton.addEventListener('click', function() {
                if (!advanced && schema) {
                    var formConfig = readSchemaForm(schemaForm, schema, config);
                    if (formConfig === null) return;
                    config = formConfig;
                    jsonInput.value = JSON.stringify(config, null, 2);
                } else if (advanced) {
                    try { config = JSON.parse(jsonInput.value || '{}'); }
                    catch (_) { window.DashboardModals.showWarning(__('capability.modal.invalidJson')); return; }
                    renderForm(false);
                }
                advanced = !advanced;
                schemaForm.classList.toggle('d-none', advanced);
                jsonPanel.classList.toggle('d-none', !advanced);
                modeButton.innerHTML = advanced ? '<i class="bi bi-ui-checks-grid me-1"></i>' + __('capability.modal.formMode') : '<i class="bi bi-braces me-1"></i>' + __('capability.modal.jsonMode');
            });
            saveButton.addEventListener('click', async function() {
                if (advanced) {
                    try { config = JSON.parse(jsonInput.value || '{}'); }
                    catch (_) { window.DashboardModals.showWarning(__('capability.modal.invalidJson')); return; }
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
                    window.DashboardModals.showSuccess(binding ? __('capability.saveSuccess') : __('capability.addSuccess'));
                    await refresh(container, scope, scopeId);
                    bootstrap.Modal.getInstance(modal).hide();
                } catch (error) { showError(error); }
            });
            modal.addEventListener('hidden.bs.modal', function() { modal.remove(); });
            renderForm(!binding);
            loadPresetsForPlugin();
            new bootstrap.Modal(modal).show();
        };
        if (allPlugins) open(allPlugins); else loadPlugins().then(open).catch(showError);
    }

    async function toggleBinding(container, scope, scopeId, binding) {
        try {
            await updateBinding(Object.assign({}, binding, { enabled: !binding.enabled }));
            window.DashboardModals.showSuccess(binding.enabled ? __('capability.disableSuccess') : __('capability.enableSuccess'));
            await refresh(container, scope, scopeId);
        } catch (error) {
            showError(error);
        }
    }

    function deleteBinding(container, scope, scopeId, binding) {
        window.DashboardModals.showConfirm(__('capability.deleteConfirm', { pluginId: binding.pluginId }), async function() {
            try {
                await window.DashboardApi.delete('/api/plugin-bindings/' + encodeURIComponent(binding.id));
                window.DashboardModals.showSuccess(__('capability.deleteSuccess'));
                await refresh(container, scope, scopeId);
            } catch (error) {
                showError(error);
            }
        }, null, { title: __('capability.deleteTitle'), danger: true });
    }

    window.DashboardCapabilities = {
        mount: function(container, scope, scopeId) {
            if (container) refresh(container, scope, scopeId);
        },
        // Expose for plugin pages
        getPluginSchema: getPluginSchema,
        schemaField: schemaField,
        applyDefaults: applyDefaults,
        readSchemaForm: readSchemaForm,
        renderGroupedFields: renderGroupedFields,
        pluginMeta: pluginMeta,
        expandSchema: expandSchema,
        mergeSchema: mergeSchema,
        propertyLabel: propertyLabel,
        parseConfig: parseConfig,
        summarizeConfig: summarizeConfig,
        loadPlugins: loadPlugins
    };
})();
