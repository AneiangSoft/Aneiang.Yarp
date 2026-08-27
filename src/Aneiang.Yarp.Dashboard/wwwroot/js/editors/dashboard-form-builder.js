/**
 * Form Builder - Dynamically generates forms from JSON Schema
 */
(function() {
    'use strict';

    window.DashboardFormBuilder = {
        /**
         * Build form from schema
         * @param {string} containerId - Container element ID
         * @param {object} schema - JSON Schema
         * @param {object} data - Initial data
         */
        build: function(containerId, schema, data) {
            var container = document.getElementById(containerId);
            if (!container) {
                console.warn('[FormBuilder] Container not found:', containerId);
                return;
            }

            container.innerHTML = '';
            this._buildFields(container, schema, data || {}, '', 0);
        },

        /**
         * Build top-level fields grouped into cards:
         * flat scalar fields -> one "Basic Info" card;
         * object/array fields -> one card each.
         */
        _buildFields: function(container, schema, data, path, depth) {
            if (!schema || !schema.properties) return;
            var self = this;
            var basicNames = [];
            var groupNames = [];
            Object.keys(schema.properties).forEach(function(propName) {
                var ps = schema.properties[propName];
                var t = ps.type;
                var isGroup = t === 'object' || t === 'array'
                    || (Array.isArray(t) && (t.indexOf('object') >= 0 || t.indexOf('array') >= 0));
                (isGroup ? groupNames : basicNames).push(propName);
            });

            if (basicNames.length > 0) {
                var basicCard = self._createCard(self._t('editor.basicInfo', 'Basic Info'));
                basicNames.forEach(function(propName) {
                    var ps = schema.properties[propName];
                    var currentPath = path ? path + '.' + propName : propName;
                    var field = self._createField(propName, ps, data[propName], currentPath, depth);
                    if (field) basicCard.body.appendChild(field);
                });
                container.appendChild(basicCard.el);
            }

            groupNames.forEach(function(propName) {
                var ps = schema.properties[propName];
                var currentPath = path ? path + '.' + propName : propName;
                var value = data[propName];
                var card = self._createCard(self._fieldTitle(propName, ps));
                var input = self._createInput(propName, ps, value, currentPath, depth);
                if (input) card.body.appendChild(input);
                container.appendChild(card.el);
            });
        },

        /**
         * Flat rendering of an object's properties (used inside a card or nested object).
         */
        _buildFieldsFlat: function(container, schema, data, path, depth) {
            if (!schema || !schema.properties) return;
            var self = this;
            Object.keys(schema.properties).forEach(function(propName) {
                var ps = schema.properties[propName];
                var currentPath = path ? path + '.' + propName : propName;
                var field = self._createField(propName, ps, data[propName], currentPath, depth);
                if (field) container.appendChild(field);
            });
        },

        /**
         * Create a card container with title bar.
         */
        _createCard: function(title) {
            var el = document.createElement('div');
            el.className = 'fb-card';
            var titleEl = document.createElement('div');
            titleEl.className = 'fb-card-title';
            titleEl.innerHTML = '<i class="bi bi-three-dots-vertical"></i><span>' + (title || '') + '</span>';
            el.appendChild(titleEl);
            var body = document.createElement('div');
            body.className = 'fb-card-body';
            el.appendChild(body);
            return { el: el, body: body };
        },

        /**
         * Derive a human-friendly title for a field.
         */
        _fieldTitle: function(name, schema) {
            if (schema && schema.title) return schema.title;
            return name.replace(/([a-z])([A-Z])/g, '$1 $2').replace(/_/g, ' ');
        },

        /**
         * i18n helper with fallback.
         */
        _t: function(key, fallback) {
            if (window.__ && typeof window.__ === 'function') {
                var v = window.__(key);
                return (v && v !== key) ? v : fallback;
            }
            return fallback;
        },

        /**
         * Create a single form field (label + input).
         */
        _createField: function(name, schema, value, path, depth) {
            var wrapper = document.createElement('div');
            wrapper.className = 'fb-field';

            var label = document.createElement('label');
            label.className = 'fb-label';
            label.textContent = name;

            if (schema.description) {
                label.title = schema.description;
            }

            var input = this._createInput(name, schema, value, path, depth);
            if (!input) return null;

            wrapper.appendChild(label);
            wrapper.appendChild(input);

            return wrapper;
        },

        /**
         * Create input element based on schema type
         */
        _createInput: function(name, schema, value, path, depth) {
            var input;

            if (schema.enum) {
                // Enum -> select
                input = document.createElement('select');
                input.className = 'form-select';
                input.setAttribute('data-path', path);
                
                schema.enum.forEach(function(option) {
                    var opt = document.createElement('option');
                    opt.value = option;
                    opt.textContent = option;
                    if (option === value) opt.selected = true;
                    input.appendChild(opt);
                });
            } else if (schema.type === 'boolean') {
                // Boolean -> checkbox
                var wrapper = document.createElement('div');
                wrapper.className = 'form-check';
                
                input = document.createElement('input');
                input.type = 'checkbox';
                input.className = 'form-check-input';
                input.setAttribute('data-path', path);
                if (value === true) input.checked = true;
                
                var label = document.createElement('label');
                label.className = 'form-check-label';
                label.textContent = name;
                
                wrapper.appendChild(input);
                wrapper.appendChild(label);
                return wrapper;
            } else if (schema.type === 'number' || schema.type === 'integer') {
                // Number -> number input
                input = document.createElement('input');
                input.type = 'number';
                input.className = 'form-control';
                input.setAttribute('data-path', path);
                if (value !== undefined) input.value = value;
            } else if (schema.type === 'object' || (Array.isArray(schema.type) && schema.type.indexOf('object') >= 0)) {
                // Object -> flat fields (when inside a card) or pattern-properties editor
                var objContainer = document.createElement('div');
                objContainer.className = 'fb-object';
                if (schema.properties) {
                    this._buildFieldsFlat(objContainer, schema, value || {}, path, (depth || 0) + 1);
                    return objContainer;
                }
                if (schema.patternProperties) {
                    return this._createPatternObjectInput(name, schema, value, path);
                }
                return objContainer;
            } else if (schema.type === 'array' || (Array.isArray(schema.type) && schema.type.indexOf('array') >= 0)) {
                // Array -> dynamic item list (primitive text inputs or per-item JSON for objects/anyOf)
                return this._createArrayInput(name, schema, value, path);
            } else {
                // String -> text input
                input = document.createElement('input');
                input.type = 'text';
                input.className = 'form-control';
                input.setAttribute('data-path', path);
                if (value !== undefined) input.value = value;
            }

            return input;
        },

        /**
         * Create pattern-properties object input (e.g. Destinations).
         * Renders each existing key as an entry with the value schema's fields,
         * plus an add button. Entry names are fixed (rename via delete + add).
         */
        _createPatternObjectInput: function(name, schema, value, path) {
            var self = this;
            var wrapper = document.createElement('div');
            wrapper.className = 'fb-pattern-object';
            var list = document.createElement('div');
            list.className = 'd-flex flex-column gap-2';
            wrapper.appendChild(list);

            var valueSchema = null;
            if (schema.patternProperties) {
                for (var pk in schema.patternProperties) { valueSchema = schema.patternProperties[pk]; break; }
            }

            function t(key, fb) { return self._t(key, fb); }
            function escapeHtml(s) {
                return (window.DashboardUtils && window.DashboardUtils.escapeHtml) ? window.DashboardUtils.escapeHtml(s) : String(s);
            }

            function renderEntry(entryKey, entryVal) {
                var entry = document.createElement('div');
                entry.className = 'fb-pattern-entry';
                var head = document.createElement('div');
                head.className = 'fb-pattern-entry-head';
                var nameSpan = document.createElement('span');
                nameSpan.className = 'fb-pattern-entry-name';
                nameSpan.innerHTML = '<i class="bi bi-server me-1"></i>' + escapeHtml(entryKey);
                head.appendChild(nameSpan);
                var delBtn = document.createElement('button');
                delBtn.type = 'button';
                delBtn.className = 'btn btn-sm btn-outline-danger';
                delBtn.innerHTML = '<i class="bi bi-trash"></i>';
                delBtn.title = t('editor.array.removeItem', 'Remove');
                delBtn.onclick = function() { entry.remove(); };
                head.appendChild(delBtn);
                entry.appendChild(head);
                if (valueSchema && valueSchema.properties) {
                    self._buildFieldsFlat(entry, valueSchema, entryVal || {}, path + '.' + entryKey, 1);
                }
                list.appendChild(entry);
            }

            var dataObj = (value && typeof value === 'object' && !Array.isArray(value)) ? value : {};
            Object.keys(dataObj).forEach(function(entryKey) {
                renderEntry(entryKey, dataObj[entryKey]);
            });

            var addBtn = document.createElement('button');
            addBtn.type = 'button';
            addBtn.className = 'btn btn-sm btn-outline-success mt-2';
            addBtn.innerHTML = '<i class="bi bi-plus-lg me-1"></i>' + t('editor.pattern.add', 'Add Item');
            addBtn.onclick = function() {
                var entryKey = window.prompt(t('editor.pattern.promptName', 'Name:'), 'destination' + (list.children.length + 1));
                if (entryKey && entryKey.trim()) {
                    renderEntry(entryKey.trim(), {});
                }
            };
            wrapper.appendChild(addBtn);

            return wrapper;
        },

        /**
         * Create array input with add/remove items.
         * Primitive items (string/number) render as text inputs;
         * object/anyOf items (e.g. Transforms) render as per-item JSON textareas.
         */
        _createArrayInput: function(name, schema, value, path) {
            var self = this;
            var items = schema.items || {};
            var itemTypes = Array.isArray(items.type) ? items.type : (items.type ? [items.type] : []);
            var isObjectItem = Array.isArray(items.anyOf)
                || itemTypes.indexOf('object') >= 0
                || (!items.type && !!items.properties);

            var wrapper = document.createElement('div');
            wrapper.className = 'array-field';

            var list = document.createElement('div');
            list.className = 'd-flex flex-column gap-2';
            wrapper.appendChild(list);

            var values = Array.isArray(value) ? value.slice() : [];

            function t(key, fallback) {
                if (window.__ && typeof window.__ === 'function') {
                    var v = window.__(key);
                    return (v && v !== key) ? v : fallback;
                }
                return fallback;
            }

            function collectCurrent() {
                var cur = [];
                var nodes = list.querySelectorAll('[data-path]');
                for (var i = 0; i < nodes.length; i++) {
                    var node = nodes[i];
                    var seg = node.getAttribute('data-path').split('.');
                    var idx = parseInt(seg[seg.length - 1], 10);
                    var v;
                    if (node.getAttribute('data-item-json') === 'true') {
                        try { v = JSON.parse(node.value || '{}'); } catch (e) { v = {}; }
                    } else {
                        v = node.value;
                    }
                    cur.push({ idx: idx, val: v });
                }
                cur.sort(function(a, b) { return a.idx - b.idx; });
                return cur.map(function(x) { return x.val; });
            }

            function renderItem(itemVal, idx) {
                var row = document.createElement('div');
                row.className = 'd-flex gap-2 align-items-start';

                var field;
                if (isObjectItem) {
                    field = document.createElement('textarea');
                    field.className = 'form-control form-control-sm font-monospace';
                    field.rows = 3;
                    field.setAttribute('data-path', path + '.' + idx);
                    field.setAttribute('data-item-json', 'true');
                    field.value = JSON.stringify(itemVal == null ? {} : itemVal, null, 2);
                } else {
                    field = document.createElement('input');
                    field.type = 'text';
                    field.className = 'form-control form-control-sm';
                    field.setAttribute('data-path', path + '.' + idx);
                    field.value = itemVal == null ? '' : String(itemVal);
                }
                row.appendChild(field);

                var removeBtn = document.createElement('button');
                removeBtn.type = 'button';
                removeBtn.className = 'btn btn-sm btn-outline-danger flex-shrink-0';
                removeBtn.innerHTML = '<i class="bi bi-dash-lg"></i>';
                removeBtn.title = t('editor.array.removeItem', 'Remove');
                removeBtn.onclick = function() {
                    values = collectCurrent();
                    values.splice(idx, 1);
                    renderAll();
                };
                row.appendChild(removeBtn);

                return row;
            }

            function renderAll() {
                list.innerHTML = '';
                values.forEach(function(itemVal, idx) {
                    list.appendChild(renderItem(itemVal, idx));
                });
            }

            renderAll();

            var addBtn = document.createElement('button');
            addBtn.type = 'button';
            addBtn.className = 'btn btn-sm btn-outline-success mt-1';
            addBtn.innerHTML = '<i class="bi bi-plus-lg me-1"></i>' + t('editor.array.addItem', 'Add Item');
            addBtn.onclick = function() {
                values = collectCurrent();
                values.push(isObjectItem ? {} : '');
                renderAll();
            };
            wrapper.appendChild(addBtn);

            return wrapper;
        },

        /**
         * Collect data from form
         */
        collectData: function(containerId) {
            var container = document.getElementById(containerId);
            if (!container) return {};

            var data = {};
            var inputs = container.querySelectorAll('[data-path]');
            
            inputs.forEach(function(input) {
                var path = input.getAttribute('data-path');
                var value;

                if (input.getAttribute('data-item-json') === 'true') {
                    // Per-item JSON (array of objects, e.g. Transforms)
                    try { value = JSON.parse(input.value || '{}'); }
                    catch (e) { value = {}; }
                } else if (input.type === 'checkbox') {
                    value = input.checked;
                } else if (input.type === 'number') {
                    value = input.value ? Number(input.value) : null;
                } else {
                    value = input.value;
                }

                this._setNestedValue(data, path, value);
            }.bind(this));

            return data;
        },

        /**
         * Set nested value in object by path (numeric segments become array indices)
         */
        _setNestedValue: function(obj, path, value) {
            var parts = path.split('.');
            var current = obj;

            for (var i = 0; i < parts.length - 1; i++) {
                var part = parts[i];
                var nextIsIndex = /^\d+$/.test(parts[i + 1]);
                if (current[part] === undefined || current[part] === null) {
                    current[part] = nextIsIndex ? [] : {};
                }
                current = current[part];
            }

            var last = parts[parts.length - 1];
            if (/^\d+$/.test(last)) {
                current[parseInt(last, 10)] = value;
            } else {
                current[last] = value;
            }
        },

        /**
         * Bind change events for real-time sync
         */
        bindSync: function(containerId, onFormDataChange) {
            var container = document.getElementById(containerId);
            if (!container) return;

            container.addEventListener('input', function() {
                var data = this.collectData(containerId);
                if (onFormDataChange) {
                    onFormDataChange(data);
                }
            }.bind(this));

            container.addEventListener('change', function() {
                var data = this.collectData(containerId);
                if (onFormDataChange) {
                    onFormDataChange(data);
                }
            }.bind(this));
        }
    };
})();
