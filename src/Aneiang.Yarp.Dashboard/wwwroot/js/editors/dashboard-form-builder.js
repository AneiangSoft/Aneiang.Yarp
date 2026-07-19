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
         * Create a single form field.
         * Displays x- extension metadata (recommendation, group, required) from schema.
         */
        _createField: function(name, schema, value, path, depth) {
            var wrapper = document.createElement('div');
            wrapper.className = 'mb-3';
            wrapper.style.paddingLeft = (depth * 20) + 'px';
            wrapper.setAttribute('data-field-path', path);

            // visibleWhen: conditional visibility
            if (schema['x-visibleWhen']) {
                wrapper.setAttribute('data-visible-when', JSON.stringify(schema['x-visibleWhen']));
                wrapper.style.display = 'none';
            }

            var labelRow = document.createElement('div');
            labelRow.className = 'd-flex align-items-center gap-2';

            var label = document.createElement('label');
            label.className = 'form-label mb-0';
            label.textContent = name;

            if (schema.description) {
                label.title = schema.description;
            }

            labelRow.appendChild(label);

            // x-productionRequired badge
            if (schema['x-productionRequired']) {
                var reqBadge = document.createElement('span');
                reqBadge.className = 'badge bg-danger';
                reqBadge.textContent = '生产必选';
                reqBadge.style.fontSize = '0.7em';
                labelRow.appendChild(reqBadge);
            }

            // x-group badge
            if (schema['x-group']) {
                var groupBadge = document.createElement('span');
                groupBadge.className = 'badge bg-secondary';
                groupBadge.textContent = schema['x-group'];
                groupBadge.style.fontSize = '0.7em';
                labelRow.appendChild(groupBadge);
            }

            wrapper.appendChild(labelRow);

            // x-recommendation hint
            if (schema['x-recommendation']) {
                var hint = document.createElement('div');
                hint.className = 'small text-primary mb-1';
                hint.innerHTML = '<i class="bi bi-lightbulb"></i> ' + this._esc(schema['x-recommendation']);
                wrapper.appendChild(hint);
            }

            // x-example hint
            if (schema['x-example']) {
                var exampleHint = document.createElement('div');
                exampleHint.className = 'small text-muted';
                exampleHint.innerHTML = '<i class="bi bi-code-slash"></i> 示例: <code>' + this._esc(schema['x-example']) + '</code>';
                wrapper.appendChild(exampleHint);
            }

            var input = this._createInput(name, schema, value, path);
            if (!input) return null;

            wrapper.appendChild(input);

            return wrapper;
        },

        /**
         * Create input element based on schema type.
         * Uses x-options for enum value descriptions.
         */
        _createInput: function(name, schema, value, path) {
            var input;

            // Get enum values (direct or from anyOf)
            var enumValues = schema.enum;
            if (!enumValues && schema.anyOf) {
                for (var i = 0; i < schema.anyOf.length; i++) {
                    if (schema.anyOf[i].enum) {
                        enumValues = schema.anyOf[i].enum;
                        break;
                    }
                }
            }

            if (enumValues) {
                // Enum -> select with x-options descriptions
                input = document.createElement('select');
                input.className = 'form-select';
                input.setAttribute('data-path', path);

                var xOptions = schema['x-options'] || {};

                enumValues.forEach(function(option) {
                    var opt = document.createElement('option');
                    opt.value = option;
                    var desc = xOptions[option];
                    opt.textContent = desc ? option + ' - ' + desc : option;
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
         * Bind change events for real-time sync.
         * Also evaluates visibleWhen conditions to show/hide fields dynamically.
         */
        bindSync: function(containerId, onFormDataChange) {
            var container = document.getElementById(containerId);
            if (!container) return;
            var self = this;

            var handleChange = function() {
                var data = self.collectData(containerId);
                if (onFormDataChange) {
                    onFormDataChange(data);
                }
                // Evaluate visibleWhen conditions
                self._evaluateVisibleWhen(container, data);
            };

            container.addEventListener('input', handleChange);
            container.addEventListener('change', handleChange);

            // Initial evaluation
            var initialData = this.collectData(containerId);
            this._evaluateVisibleWhen(container, initialData);
        },

        /**
         * Evaluate all visibleWhen conditions in the container.
         * @param {HTMLElement} container - Form container
         * @param {object} data - Current form data
         */
        _evaluateVisibleWhen: function(container, data) {
            var conditionalFields = container.querySelectorAll('[data-visible-when]');
            conditionalFields.forEach(function(field) {
                var condition = JSON.parse(field.getAttribute('data-visible-when'));
                var visible = true;

                // condition format: { "field": "path.to.field", "equals": "value" }
                if (condition.field) {
                    var fieldValue = this._getNestedValue(data, condition.field);
                    if (condition.equals !== undefined) {
                        visible = fieldValue === condition.equals;
                    } else if (condition.notEquals !== undefined) {
                        visible = fieldValue !== condition.notEquals;
                    } else if (condition.in) {
                        visible = condition.in.indexOf(fieldValue) >= 0;
                    }
                }

                field.style.display = visible ? '' : 'none';
            }.bind(this));
        },

        /**
         * Get nested value from object by dot-separated path.
         */
        _getNestedValue: function(obj, path) {
            var parts = path.split('.');
            var current = obj;
            for (var i = 0; i < parts.length; i++) {
                if (current == null) return undefined;
                current = current[parts[i]];
            }
            return current;
        },

        /**
         * Build a multi-step wizard form from schema.
         * @param {string} containerId - Container element ID
         * @param {object} schema - JSON Schema
         * @param {object} data - Initial data
         * @param {array} steps - Array of {title, fields: ['FieldName', ...]}
         */
        buildWizard: function(containerId, schema, data, steps) {
            var container = document.getElementById(containerId);
            if (!container || !schema || !steps || steps.length === 0) return;

            data = data || {};
            var self = this;
            var currentStep = 0;

            function render() {
                container.innerHTML = '';

                // Progress indicator
                var progress = document.createElement('div');
                progress.className = 'd-flex justify-content-between mb-3';
                steps.forEach(function(step, i) {
                    var active = i === currentStep;
                    var done = i < currentStep;
                    var bg = done ? 'bg-success' : (active ? 'bg-primary' : 'bg-light text-muted');
                    progress.innerHTML += '<div class="text-center flex-grow-1">' +
                        '<div class="rounded-circle mx-auto d-flex align-items-center justify-content-center ' +
                        bg + '" style="width:32px;height:32px;font-size:14px;">' +
                        (done ? '✓' : (i + 1)) + '</div>' +
                        '<small class="' + (active ? 'fw-bold' : 'text-muted') + ' d-block mt-1">' + (step.title || ('Step ' + (i + 1))) + '</small>' +
                        '</div>';
                    if (i < steps.length - 1) {
                        progress.innerHTML += '<div class="flex-grow-1 mt-2"><hr class="' + (done ? 'border-success' : '') + '"></div>';
                    }
                });
                container.appendChild(progress);

                // Step content
                var stepDiv = document.createElement('div');
                stepDiv.id = containerId + '-step-content';
                var stepConfig = steps[currentStep];

                if (stepConfig.fields && schema.properties) {
                    stepConfig.fields.forEach(function(fieldName) {
                        var propSchema = schema.properties[fieldName];
                        if (!propSchema) return;
                        var value = data[fieldName];
                        var field = self._createField(fieldName, propSchema, value, fieldName, 0);
                        if (field) stepDiv.appendChild(field);
                    });
                }
                container.appendChild(stepDiv);

                // Navigation buttons
                var nav = document.createElement('div');
                nav.className = 'd-flex justify-content-between mt-3';
                if (currentStep > 0) {
                    var prevBtn = document.createElement('button');
                    prevBtn.className = 'btn btn-secondary btn-sm';
                    prevBtn.innerHTML = '← 上一步';
                    prevBtn.onclick = function() { currentStep--; render(); self._bindInputs(containerId); };
                    nav.appendChild(prevBtn);
                } else {
                    nav.appendChild(document.createElement('span'));
                }
                if (currentStep < steps.length - 1) {
                    var nextBtn = document.createElement('button');
                    nextBtn.className = 'btn btn-primary btn-sm';
                    nextBtn.innerHTML = '下一步 →';
                    nextBtn.onclick = function() {
                        // Collect current step data
                        var stepData = self.collectData(containerId);
                        Object.keys(stepData).forEach(function(k) { data[k] = stepData[k]; });
                        currentStep++;
                        render();
                    };
                    nav.appendChild(nextBtn);
                } else {
                    var finishBtn = document.createElement('button');
                    finishBtn.className = 'btn btn-success btn-sm';
                    finishBtn.innerHTML = '✓ 完成';
                    finishBtn.onclick = function() {
                        var stepData = self.collectData(containerId);
                        Object.keys(stepData).forEach(function(k) { data[k] = stepData[k]; });
                        container.dispatchEvent(new CustomEvent('wizard:complete', { detail: data }));
                    };
                    nav.appendChild(finishBtn);
                }
                container.appendChild(nav);

                // Bind sync for visibleWhen
                self.bindSync(containerId, function(newData) {
                    Object.keys(newData).forEach(function(k) { data[k] = newData[k]; });
                });
            }

            render();

            // Return an API for external control
            return {
                getData: function() { return data; },
                goToStep: function(step) { currentStep = step; render(); },
                reset: function() { currentStep = 0; data = {}; render(); }
            };
        },

        _esc: function(s) {
            if (!s) return '';
            var d = document.createElement('div');
            d.textContent = s;
            return d.innerHTML;
        }
    };
})();
