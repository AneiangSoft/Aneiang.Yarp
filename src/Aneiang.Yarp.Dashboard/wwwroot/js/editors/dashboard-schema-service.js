/**
 * JSON Schema Service
 * Loads, caches and provides access to ConfigurationSchema.json
 */
(function() {
    'use strict';

    window.DashboardSchemaService = {
        schema: null,
        loaded: false,
        loading: false,

        /**
         * Load ConfigurationSchema.json
         * Returns Promise that resolves with schema object
         */
        load: function() {
            var self = this;
            
            if (this.loaded && this.schema) {
                return Promise.resolve(this.schema);
            }

            if (this.loading) {
                // Already loading, wait for completion
                return new Promise(function(resolve, reject) {
                    var checkInterval = setInterval(function() {
                        if (self.loaded) {
                            clearInterval(checkInterval);
                            resolve(self.schema);
                        }
                    }, 100);
                });
            }

            this.loading = true;

            return new Promise(function(resolve, reject) {
                var xhr = new XMLHttpRequest();
                xhr.open('GET', '/_content/Aneiang.Yarp.Dashboard/ConfigurationSchema.json', true);
                
                xhr.onreadystatechange = function() {
                    if (xhr.readyState === 4) {
                        if (xhr.status === 200) {
                            try {
                                self.schema = JSON.parse(xhr.responseText);
                                self.loaded = true;
                                self.loading = false;
                                console.log('[Schema] Loaded successfully');
                                resolve(self.schema);
                            } catch (e) {
                                console.error('[Schema] Parse failed:', e);
                                self.loading = false;
                                reject(e);
                            }
                        } else {
                            console.error('[Schema] Load failed:', xhr.status);
                            self.loading = false;
                            reject(new Error('Failed to load schema'));
                        }
                    }
                };

                xhr.onerror = function() {
                    console.error('[Schema] Network error');
                    self.loading = false;
                    reject(new Error('Network error'));
                };

                xhr.send();
            });
        },

        /**
         * Navigate schema to a specific path
         * @param {string} path - Dot-separated path (e.g., 'ReverseProxy.Clusters')
         * @returns {object} Schema at the specified path
         */
        getSchemaAt: function(path) {
            if (!this.schema) return null;

            var parts = path.split('.').filter(p => p);
            var current = this.schema;

            for (var i = 0; i < parts.length; i++) {
                var part = parts[i];
                
                if (current.properties && current.properties[part]) {
                    current = current.properties[part];
                } else if (current.patternProperties) {
                    // For dynamic keys (patternProperties), use the first pattern
                    for (var pattern in current.patternProperties) {
                        current = current.patternProperties[pattern];
                        break;
                    }
                } else {
                    return null;
                }
            }

            return current;
        },

        /**
         * Get property hints at a specific path
         * @param {string} path - Dot-separated path
         * @returns {array} Array of hint objects
         */
        getHints: function(path) {
            var schemaAt = this.getSchemaAt(path);
            if (!schemaAt || !schemaAt.properties) return [];

            var hints = [];
            var props = schemaAt.properties;

            for (var prop in props) {
                var propSchema = props[prop];
                hints.push({
                    text: prop,
                    displayText: prop,
                    description: propSchema.description || '',
                    type: propSchema.type || 'any',
                    required: (schemaAt.required || []).indexOf(prop) !== -1,
                    enum: propSchema.enum || null,
                    default: propSchema.default !== undefined ? propSchema.default : null
                });
            }

            return hints;
        },

        /**
         * Get enum values for a specific path
         * @param {string} path - Dot-separated path
         * @returns {array|null} Enum values or null
         */
        getEnum: function(path) {
            var schemaAt = this.getSchemaAt(path);
            if (!schemaAt) return null;

            // Direct enum
            if (schemaAt.enum) return schemaAt.enum;

            // Enum in anyOf
            if (schemaAt.anyOf) {
                for (var i = 0; i < schemaAt.anyOf.length; i++) {
                    if (schemaAt.anyOf[i].enum) {
                        return schemaAt.anyOf[i].enum;
                    }
                }
            }

            return null;
        },

        /**
         * Get default value for a specific path
         * @param {string} path - Dot-separated path
         * @returns {*} Default value or undefined
         */
        getDefault: function(path) {
            var schemaAt = this.getSchemaAt(path);
            if (!schemaAt) return undefined;
            return schemaAt.default;
        },

        /**
         * Get description for a specific path
         * @param {string} path - Dot-separated path
         * @returns {string} Description text
         */
        getDescription: function(path) {
            var schemaAt = this.getSchemaAt(path);
            if (!schemaAt) return '';
            return schemaAt.description || '';
        },

        /**
         * Check if a property is required at a specific path
         * @param {string} path - Dot-separated path
         * @param {string} property - Property name
         * @returns {boolean}
         */
        isRequired: function(path, property) {
            var schemaAt = this.getSchemaAt(path);
            if (!schemaAt || !schemaAt.required) return false;
            return schemaAt.required.indexOf(property) !== -1;
        }
    };
})();
