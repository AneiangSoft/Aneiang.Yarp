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
         * Recursively build form fields
         */
        _buildFields: function(container, schema, data, path, depth) {
            if (!schema || !schema.properties) return;

            var self = this;

            Object.keys(schema.properties).forEach(function(propName) {
                var propSchema = schema.properties[propName];
                var currentPath = path ? path + '.' + propName : propName;
                var value = data[propName];

                // Create field based on type
                var field = self._createField(propName, propSchema, value, currentPath, depth);
                if (field) {
                    container.appendChild(field);
                }
            });
        },

        /**
         * Create a single form field
         */
        _createField: function(name, schema, value, path, depth) {
            var wrapper = document.createElement('div');
            wrapper.className = 'mb-3';
            wrapper.style.paddingLeft = (depth * 20) + 'px';

            var label = document.createElement('label');
            label.className = 'form-label';
            label.textContent = name;
            
            if (schema.description) {
                label.title = schema.description;
            }

            var input = this._createInput(name, schema, value, path);
            if (!input) return null;

            wrapper.appendChild(label);
            wrapper.appendChild(input);

            return wrapper;
        },

        /**
         * Create input element based on schema type
         */
        _createInput: function(name, schema, value, path) {
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
            } else if (schema.type === 'object') {
                // Object -> nested fieldset
                var fieldset = document.createElement('fieldset');
                fieldset.className = 'border rounded p-3 mb-3';
                
                var legend = document.createElement('legend');
                legend.className = 'w-auto px-2';
                legend.textContent = name;
                fieldset.appendChild(legend);
                
                if (schema.properties) {
                    this._buildFields(fieldset, schema, value || {}, path, 1);
                }
                
                return fieldset;
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

                if (input.type === 'checkbox') {
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
         * Set nested value in object by path
         */
        _setNestedValue: function(obj, path, value) {
            var parts = path.split('.');
            var current = obj;
            
            for (var i = 0; i < parts.length - 1; i++) {
                var part = parts[i];
                if (!current[part]) {
                    current[part] = {};
                }
                current = current[part];
            }
            
            current[parts[parts.length - 1]] = value;
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
