/**
 * Monaco Editor Integration Module
 * Provides JSON editing with schema validation, formatting, and lifecycle management
 */
(function() {
    'use strict';

    window.DashboardMonacoEditor = {
        instances: new Map(), // containerId -> editor instance
        schemaLoaded: false,
        monacoReady: false,

        /**
         * Initialize Monaco Editor
         * @param {string} containerId - DOM element ID for editor container
         * @param {object} options - Configuration options
         */
        init: function(containerId, options) {
            var self = this;
            var container = document.getElementById(containerId);
            
            if (!container) {
                console.warn('[Monaco] Container not found:', containerId);
                return Promise.reject('Container not found');
            }

            // If already initialized, dispose first
            if (this.instances.has(containerId)) {
                this.dispose(containerId);
            }

            // Wait for Monaco to be ready
            return this._ensureMonacoLoaded().then(function() {
                var config = Object.assign({
                    value: options.value || '',
                    language: 'json',
                    theme: 'vs-dark',
                    automaticLayout: true,
                    minimap: { enabled: false },
                    scrollBeyondLastLine: false,
                    tabSize: 2,
                    formatOnPaste: true,
                    formatOnType: true,
                    wordWrap: 'on',
                    lineNumbers: 'on',
                    renderWhitespace: 'selection',
                    bracketPairColorization: { enabled: true },
                    suggestOnTriggerCharacters: true,
                    quickSuggestions: { other: 'on', comments: 'off', strings: 'off' }
                }, options);

                // Create editor
                var editor = monaco.editor.create(container, config);

                // Register schema if provided
                if (options.schema) {
                    self._registerSchema(options.schema);
                }

                // Store instance
                self.instances.set(containerId, {
                    editor: editor,
                    options: options,
                    container: container
                });

                // Setup modal cleanup if inside a Bootstrap modal
                var modal = container.closest('.modal');
                if (modal) {
                    modal.addEventListener('hidden.bs.modal', function() {
                        self.dispose(containerId);
                    });
                }

                // Trigger layout after modal animation
                setTimeout(function() {
                    editor.layout();
                }, 200);

                return editor;
            });
        },

        /**
         * Get editor value
         */
        getValue: function(containerId) {
            var instance = this.instances.get(containerId);
            if (!instance) {
                console.warn('[Monaco] Editor not found:', containerId);
                return null;
            }
            return instance.editor.getValue();
        },

        /**
         * Set editor value
         */
        setValue: function(containerId, value) {
            var instance = this.instances.get(containerId);
            if (!instance) {
                console.warn('[Monaco] Editor not found:', containerId);
                return;
            }
            instance.editor.setValue(value);
        },

        /**
         * Format JSON in editor
         */
        format: function(containerId) {
            var instance = this.instances.get(containerId);
            if (!instance) return Promise.resolve(false);

            try {
                var value = instance.editor.getValue();
                var parsed = JSON.parse(value);
                var formatted = JSON.stringify(parsed, null, 2);
                instance.editor.setValue(formatted);
                return Promise.resolve(true);
            } catch (e) {
                console.error('[Monaco] Format failed:', e);
                return Promise.resolve(false);
            }
        },

        /**
         * Validate JSON and return errors
         */
        validate: function(containerId) {
            var instance = this.instances.get(containerId);
            if (!instance) return { valid: false, errors: ['Editor not found'] };

            var value = instance.editor.getValue();
            var errors = [];

            try {
                JSON.parse(value);
            } catch (e) {
                errors.push({
                    message: e.message,
                    type: 'syntax'
                });
            }

            return {
                valid: errors.length === 0,
                errors: errors
            };
        },

        /**
         * Dispose editor instance
         */
        dispose: function(containerId) {
            var instance = this.instances.get(containerId);
            if (instance) {
                instance.editor.dispose();
                this.instances.delete(containerId);
            }
        },

        /**
         * Dispose all editor instances
         */
        disposeAll: function() {
            var self = this;
            this.instances.forEach(function(instance, containerId) {
                self.dispose(containerId);
            });
        },

        /**
         * Ensure Monaco is loaded
         */
        _ensureMonacoLoaded: function() {
            var self = this;
            
            if (this.monacoReady) {
                return Promise.resolve();
            }

            if (typeof monaco !== 'undefined') {
                this.monacoReady = true;
                return Promise.resolve();
            }

            // Monaco not loaded, return error
            return Promise.reject('Monaco Editor not loaded. Ensure loader.js is included.');
        },

        /**
         * Register JSON Schema for validation and hints
         */
        _registerSchema: function(schema) {
            if (typeof monaco === 'undefined' || !monaco.languages) return;

            try {
                monaco.languages.json.jsonDefaults.setDiagnosticsOptions({
                    validate: true,
                    allowComments: false,
                    schemas: [{
                        uri: 'http://aneiang.yarp/schema',
                        fileMatch: ['*'],
                        schema: schema
                    }]
                });
                this.schemaLoaded = true;
            } catch (e) {
                console.error('[Monaco] Schema registration failed:', e);
            }
        },

        /**
         * Update schema dynamically
         */
        updateSchema: function(schema) {
            this._registerSchema(schema);
        }
    };
})();
