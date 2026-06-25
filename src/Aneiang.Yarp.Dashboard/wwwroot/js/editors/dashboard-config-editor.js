/**
 * Configuration Editor - Main controller for form/JSON dual-mode editing
 */
(function() {
    'use strict';

    window.DashboardConfigEditor = {
        currentMode: 'form', // 'form' | 'json'
        draftData: null,
        originalData: null,
        configType: null, // 'cluster' | 'route' | 'full'
        configId: null,
        containerId: null,

        /**
         * Initialize configuration editor
         */
        init: function(containerId, config) {
            this.containerId = containerId;
            this.configType = config.type || 'full';
            this.configId = config.id || null;
            this.currentMode = config.mode || 'form';
            this.draftData = config.data || {};
            this.originalData = JSON.parse(JSON.stringify(this.draftData));

            var self = this;
            
            window.DashboardSchemaService.load().then(function(schema) {
                self._renderEditor();
            }).catch(function(err) {
                console.error('[ConfigEditor] Failed to load schema:', err);
            });
        },

        /**
         * Render editor based on current mode
         */
        _renderEditor: function() {
            var container = document.getElementById(this.containerId);
            if (!container) return;

            container.innerHTML = '';

            var toolbar = this._createToolbar();
            container.appendChild(toolbar);

            var editorArea = document.createElement('div');
            editorArea.className = 'editor-area mt-3';
            editorArea.id = this.containerId + '-area';
            container.appendChild(editorArea);

            var validationPanel = document.createElement('div');
            validationPanel.id = 'validationPanel';
            validationPanel.style.display = 'none';
            container.appendChild(validationPanel);

            var actions = this._createActions();
            container.appendChild(actions);

            // Render current mode
            if (this.currentMode === 'form') {
                this._renderFormEditor();
            } else {
                this._renderJsonEditor();
            }
        },

        /**
         * Create toolbar with mode switch
         */
        _createToolbar: function() {
            var toolbar = document.createElement('div');
            toolbar.className = 'btn-group';
            toolbar.setAttribute('role', 'group');

            var formBtn = document.createElement('button');
            formBtn.type = 'button';
            formBtn.className = 'btn btn-sm ' + (this.currentMode === 'form' ? 'btn-primary' : 'btn-outline-primary');
            formBtn.textContent = 'Form Mode';
            formBtn.onclick = function() { this.switchMode('form'); }.bind(this);

            var jsonBtn = document.createElement('button');
            jsonBtn.type = 'button';
            jsonBtn.className = 'btn btn-sm ' + (this.currentMode === 'json' ? 'btn-primary' : 'btn-outline-primary');
            jsonBtn.textContent = 'JSON Mode';
            jsonBtn.onclick = function() { this.switchMode('json'); }.bind(this);

            toolbar.appendChild(formBtn);
            toolbar.appendChild(jsonBtn);

            return toolbar;
        },

        /**
         * Create action buttons
         */
        _createActions: function() {
            var actions = document.createElement('div');
            actions.className = 'mt-3 d-flex gap-2';

            var validateBtn = document.createElement('button');
            validateBtn.className = 'btn btn-warning';
            validateBtn.textContent = 'Validate';
            validateBtn.onclick = function() { this.validate(); }.bind(this);

            var diffBtn = document.createElement('button');
            diffBtn.className = 'btn btn-info';
            diffBtn.textContent = 'Preview Changes';
            diffBtn.onclick = function() { this.showDiff(); }.bind(this);

            var saveBtn = document.createElement('button');
            saveBtn.className = 'btn btn-success';
            saveBtn.textContent = 'Save';
            saveBtn.onclick = function() { this.save(); }.bind(this);

            var cancelBtn = document.createElement('button');
            cancelBtn.className = 'btn btn-secondary';
            cancelBtn.textContent = 'Cancel';
            cancelBtn.onclick = function() { this.cancel(); }.bind(this);

            actions.appendChild(validateBtn);
            actions.appendChild(diffBtn);
            actions.appendChild(saveBtn);
            actions.appendChild(cancelBtn);

            return actions;
        },

        /**
         * Render form editor
         */
        _renderFormEditor: function() {
            var area = document.getElementById(this.containerId + '-area');
            if (!area) return;

            // Get schema for current config type
            var schema = window.DashboardSchemaService.getSchemaForType(this.configType);
            
            window.DashboardFormBuilder.build(this.containerId + '-form', schema, this.draftData);
            window.DashboardFormBuilder.bindSync(this.containerId + '-form', function(data) {
                this.draftData = data;
            }.bind(this));
        },

        /**
         * Render JSON editor
         */
        _renderJsonEditor: function() {
            var area = document.getElementById(this.containerId + '-area');
            if (!area) return;

            var textareaId = this.containerId + '-json';
            var textarea = document.createElement('textarea');
            textarea.id = textareaId;
            textarea.style.display = 'none';
            area.appendChild(textarea);

            var editorContainer = document.createElement('div');
            editorContainer.id = textareaId + '-editor';
            editorContainer.className = 'monaco-editor-container';
            editorContainer.style.height = '400px';
            editorContainer.style.border = '1px solid #404040';
            editorContainer.style.borderRadius = '4px';
            area.appendChild(editorContainer);

            // Initialize Monaco Editor
            var jsonValue = JSON.stringify(this.draftData, null, 2);
            window.DashboardMonacoEditor.init(textareaId, {
                value: jsonValue,
                onChange: function(newValue) {
                    try {
                        this.draftData = window.DashboardUtils.parseJsonLenient(newValue);
                    } catch (e) {
                        // Invalid JSON, ignore
                    }
                }.bind(this)
            }).then(function() {
                window.DashboardMonacoEditor.setValue(textareaId, jsonValue);
            });
        },

        /**
         * Switch editor mode
         */
        switchMode: function(mode) {
            if (mode === this.currentMode) return;

            if (mode === 'json') {
                // Form -> JSON: serialize data
                this.draftData = window.DashboardFormBuilder.collectData(this.containerId + '-form');
            } else {
                // JSON -> Form: data already synced
            }

            this.currentMode = mode;
            this._renderEditor();
        },

        /**
         * Validate current data
         */
        validate: function() {
            var schema = window.DashboardSchemaService.getSchemaForType(this.configType);
            var result = window.DashboardSchemaValidator.validate(this.draftData, schema);
            
            window.DashboardValidationPanel.show(result);
            return result.valid;
        },

        /**
         * Save configuration
         */
        save: async function() {
            if (!this.validate()) {
                return false;
            }

            try {
                var response;
                
                if (this.configType === 'cluster') {
                    response = await window.DashboardApi.endpoints.saveCluster(
                        this.configId, 
                        this.draftData
                    );
                } else if (this.configType === 'route') {
                    response = await window.DashboardApi.endpoints.saveRoute(
                        this.configId, 
                        this.draftData
                    );
                } else {
                    // Full config import
                    response = await window.DashboardApi.endpoints.importConfig(
                        this.draftData
                    );
                }

                if (response.code === 200) {
                    this.originalData = JSON.parse(JSON.stringify(this.draftData));
                    if (window.DashboardModals) window.DashboardModals.showSuccess('Saved successfully');
                    return true;
                } else {
                    if (window.DashboardModals) window.DashboardModals.showError('Save failed: ' + (response.message || 'Unknown error'));
                    return false;
                }
            } catch (error) {
                console.error('[ConfigEditor] Save failed:', error);
                if (window.DashboardModals) window.DashboardModals.showError('Save failed: ' + error.message);
                return false;
            }
        },

        /**
         * Show diff preview
         */
        showDiff: function() {
            window.DashboardDiffPanel.show(this.originalData, this.draftData);
        },

        /**
         * Import configuration from file
         */
        import: async function(file) {
            try {
                var text = await file.text();
                var config = window.DashboardUtils.parseJsonLenient(text);

                // Pre-validate
                var validation = window.DashboardSchemaValidator.validate(
                    config, 
                    window.ConfigurationSchema
                );

                if (!validation.valid) {
                    window.DashboardValidationPanel.show(validation);
                    return false;
                }

                // Call backend import
                var response = await window.DashboardApi.endpoints.importConfig(config);

                if (response.code === 200) {
                    if (window.DashboardModals) window.DashboardModals.showSuccess('Import successful');
                    window.location.reload();
                    return true;
                }

                return false;
            } catch (error) {
                console.error('[ConfigEditor] Import failed:', error);
                if (window.DashboardModals) window.DashboardModals.showError('Import failed: ' + error.message);
                return false;
            }
        },

        /**
         * Export configuration
         */
        export: async function() {
            try {
                var response = await window.DashboardApi.endpoints.exportConfig();

                if (response.code === 200) {
                    var blob = new Blob(
                        [JSON.stringify(response.data, null, 2)], 
                        { type: 'application/json' }
                    );
                    var url = URL.createObjectURL(blob);
                    var a = document.createElement('a');
                    a.href = url;
                    a.download = 'yarp-config-' + Date.now() + '.json';
                    a.click();
                    URL.revokeObjectURL(url);
                    return true;
                }

                return false;
            } catch (error) {
                console.error('[ConfigEditor] Export failed:', error);
                if (window.DashboardModals) window.DashboardModals.showError('Export failed: ' + error.message);
                return false;
            }
        },

        /**
         * Cancel editing
         */
        cancel: function() {
            // Close modal or revert changes
            var modal = document.getElementById(this.containerId)?.closest('.modal');
            if (modal) {
                var bsModal = bootstrap.Modal.getInstance(modal);
                if (bsModal) {
                    bsModal.hide();
                }
            }
        }
    };
})();
