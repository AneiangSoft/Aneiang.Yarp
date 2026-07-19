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

                var editor = monaco.editor.create(container, config);

                // Register schema if provided
                if (options.schema) {
                    self._registerSchema(options.schema);
                }

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
         * Ensure Monaco is loaded using the lazy loader.
         * Falls back to the legacy window.__monacoReady promise if available
         * (for pages that still eagerly load Monaco in <head>).
         */
        _ensureMonacoLoaded: function() {
            var self = this;

            if (this.monacoReady) {
                return Promise.resolve();
            }

            // Monaco already globally available — use it directly
            if (typeof monaco !== 'undefined' && monaco.editor) {
                this.monacoReady = true;
                return Promise.resolve();
            }

            // Use the lazy loader if available (recommended path)
            if (window.LazyMonacoLoader) {
                return window.LazyMonacoLoader.ensure().then(function() {
                    self.monacoReady = true;
                });
            }

            // Fallback: legacy window.__monacoReady promise from eagerly-loaded pages
            if (window.__monacoReady) {
                return window.__monacoReady.then(function() {
                    self.monacoReady = true;
                });
            }

            return Promise.reject('Monaco Editor not available. Add lazy-monaco-loader.js or Monaco loader.js to the page.');
        },

        /**
         * Register JSON Schema for validation and hints.
         * Also registers a custom Hover Provider that displays x- extension metadata.
         */
        _registerSchema: function(schema) {
            if (typeof monaco === 'undefined' || !monaco.languages) return;

            try {
                monaco.languages.json.jsonDefaults.setDiagnosticsOptions({
                    validate: true,
                    allowComments: true,
                    trailingCommas: 'ignore',
                    schemas: [{
                        uri: 'http://aneiang.yarp/schema',
                        fileMatch: ['*'],
                        schema: schema
                    }]
                });
                this.schemaLoaded = true;
                this._currentSchema = schema;
                this._registerHoverProvider();
                this._registerCompletionProvider();
            } catch (e) {
                console.error('[Monaco] Schema registration failed:', e);
            }
        },

        /**
         * Register a custom Hover Provider that augments the built-in JSON schema hover
         * with x- extension metadata (recommendation, example, options, docUrl).
         */
        _registerHoverProvider: function() {
            if (typeof monaco === 'undefined' || !monaco.languages) return;
            if (this._hoverRegistered) return;

            var self = this;

            try {
                monaco.languages.registerHoverProvider('json', {
                    provideHover: function(model, position) {
                        if (!self._currentSchema) return null;

                        // Get the word at position (property key or value)
                        var wordInfo = model.getWordAtPosition(position);
                        if (!wordInfo) return null;

                        var word = wordInfo.word;
                        if (!word) return null;

                        // Find the schema node matching this property name
                        var schemaNode = self._findSchemaNode(model, position, word);
                        if (!schemaNode) return null;

                        // Build hover content
                        var contents = [];
                        var isZh = self._isZh();

                        // Property name and description
                        contents.push({ value: '**' + word + '**' });
                        if (schemaNode.description) {
                            contents.push({ value: schemaNode.description });
                        }

                        // x-recommendation
                        if (schemaNode['x-recommendation']) {
                            contents.push({
                                value: (isZh ? '💡 **建议**: ' : '💡 **Recommendation**: ') + schemaNode['x-recommendation']
                            });
                        }

                        // x-example
                        if (schemaNode['x-example']) {
                            contents.push({
                                value: (isZh ? '📝 **示例**: `' : '📝 **Example**: `') + schemaNode['x-example'] + '`'
                            });
                        }

                        // x-options (enum value descriptions)
                        if (schemaNode['x-options']) {
                            var optsText = isZh ? '📋 **可选值**:' : '📋 **Options**:';
                            var opts = schemaNode['x-options'];
                            for (var key in opts) {
                                optsText += '\n  • `' + key + '` - ' + opts[key];
                            }
                            contents.push({ value: optsText });
                        }

                        // x-group
                        if (schemaNode['x-group']) {
                            var groupLabel = isZh ? '分类' : 'Group';
                            contents.push({ value: '🏷️ ' + groupLabel + ': ' + schemaNode['x-group'] });
                        }

                        // x-productionRequired
                        if (schemaNode['x-productionRequired']) {
                            contents.push({
                                value: isZh ? '⚠️ **生产环境必选**' : '⚠️ **Required for production**'
                            });
                        }

                        // x-docUrl
                        if (schemaNode['x-docUrl']) {
                            contents.push({
                                value: '📖 [' + (isZh ? '官方文档' : 'Documentation') + '](' + schemaNode['x-docUrl'] + ')'
                            });
                        }

                        if (contents.length <= 1) return null;

                        return {
                            range: new monaco.Range(
                                position.lineNumber,
                                wordInfo.startColumn,
                                position.lineNumber,
                                wordInfo.endColumn
                            ),
                            contents: contents
                        };
                    }
                });

                this._hoverRegistered = true;
            } catch (e) {
                console.warn('[Monaco] Hover provider registration failed:', e);
            }
        },

        /**
         * Register a custom Completion Provider that adds "recommended" markers
         * to enum values based on x-options metadata.
         */
        _registerCompletionProvider: function() {
            if (typeof monaco === 'undefined' || !monaco.languages) return;
            if (this._completionRegistered) return;

            var self = this;

            try {
                monaco.languages.registerCompletionItemProvider('json', {
                    triggerCharacters: ['"', ':'],
                    provideCompletionItems: function(model, position) {
                        if (!self._currentSchema) return { suggestions: [] };

                        var wordInfo = model.getWordUntilPosition(position);
                        if (!wordInfo) return { suggestions: [] };

                        // Check if we're inside a Transforms array context
                        var lineText = model.getLineContent(position.lineNumber);
                        var fullText = model.getValue();
                        var lineIndex = position.lineNumber - 1;
                        var lines = fullText.split('\n');

                        // Walk up to find if we're inside a "Transforms" array
                        var inTransforms = false;
                        for (var li = lineIndex; li >= 0; li--) {
                            if (lines[li] && /"Transforms"\s*:/.test(lines[li])) {
                                inTransforms = true;
                                break;
                            }
                            // Stop if we hit a closing bracket at same or lower indentation
                            if (lines[li] && /^\s*\}/.test(lines[li]) && li < lineIndex) break;
                        }

                        if (inTransforms) {
                            // Suggest Transform type names
                            var transformTypes = [
                                'PathRemovePrefix', 'PathSet', 'PathPrefix', 'PathPattern',
                                'RequestHeader', 'RequestHeaderRouteValue', 'RequestHeadersAllowed',
                                'RequestHeaderRemove', 'RequestHeadersCopy', 'RequestHeaderOriginalHost',
                                'ResponseHeader', 'ResponseHeaderRemove', 'ResponseHeadersCopy',
                                'ResponseHeadersAllowed', 'ResponseTrailer', 'ResponseTrailerRemove',
                                'ResponseTrailersCopy', 'ResponseTrailersAllowed',
                                'X-Forwarded', 'Forwarded', 'ClientCert',
                                'QueryValueParameter', 'QueryRemoveParameter', 'QueryRouteParameter',
                                'HttpMethodChange'
                            ];

                            var suggestions = transformTypes.map(function(name) {
                                return {
                                    label: name,
                                    kind: monaco.languages.CompletionItemKind.Property,
                                    insertText: '"' + name + '": "$1"',
                                    insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
                                    detail: self._getTransformDescBrief(name),
                                    sortText: '0' + name,
                                    range: new monaco.Range(
                                        position.lineNumber,
                                        wordInfo.startColumn,
                                        position.lineNumber,
                                        wordInfo.endColumn
                                    )
                                };
                            });

                            return { suggestions: suggestions };
                        }

                        // Find schema node for the current context
                        var schemaNode = self._findSchemaNode(model, position, wordInfo.word);
                        if (!schemaNode) return { suggestions: [] };

                        // Get enum values with x-options descriptions
                        var enumValues = schemaNode.enum;
                        if (!enumValues && schemaNode.anyOf) {
                            for (var i = 0; i < schemaNode.anyOf.length; i++) {
                                if (schemaNode.anyOf[i].enum) {
                                    enumValues = schemaNode.anyOf[i].enum;
                                    break;
                                }
                            }
                        }

                        if (!enumValues || enumValues.length === 0) return { suggestions: [] };

                        var xOptions = schemaNode['x-options'] || {};
                        var suggestions = [];

                        for (var j = 0; j < enumValues.length; j++) {
                            var val = enumValues[j];
                            var desc = xOptions[val] || '';
                            var isRecommended = desc.indexOf('推荐') >= 0 || desc.indexOf('recommend') >= 0;

                            suggestions.push({
                                label: val,
                                kind: monaco.languages.CompletionItemKind.EnumMember,
                                insertText: '"' + val + '"',
                                detail: isRecommended ? '★ ' + desc : desc,
                                sortText: isRecommended ? '0' : '1' + val,
                                range: new monaco.Range(
                                    position.lineNumber,
                                    wordInfo.startColumn,
                                    position.lineNumber,
                                    wordInfo.endColumn
                                )
                            });
                        }

                        return { suggestions: suggestions };
                    }
                });

                this._completionRegistered = true;
            } catch (e) {
                console.warn('[Monaco] Completion provider registration failed:', e);
            }
        },

        /**
         * Find the schema node matching the property at the given position.
         * Walks up the AST to build a path, then navigates the schema.
         */
        _findSchemaNode: function(model, position, word) {
            if (!this._currentSchema) return null;

            // Simple approach: search for the property name in the schema properties
            // at various levels. This is a heuristic - the built-in Monaco JSON
            // language service handles precise path resolution for validation.
            var schema = this._currentSchema;

            // Try to find in top-level properties
            if (schema.properties && schema.properties[word]) {
                return schema.properties[word];
            }

            // Search recursively
            return this._searchSchema(schema, word);
        },

        /**
         * Recursively search for a property name in the schema.
         */
        _searchSchema: function(node, word, depth) {
            if (!node || depth > 5) return null;

            if (node.properties && node.properties[word]) {
                return node.properties[word];
            }

            // Search in nested properties
            if (node.properties) {
                for (var key in node.properties) {
                    var found = this._searchSchema(node.properties[key], word, (depth || 0) + 1);
                    if (found) return found;
                }
            }

            // Search in patternProperties
            if (node.patternProperties) {
                for (var pattern in node.patternProperties) {
                    var found2 = this._searchSchema(node.patternProperties[pattern], word, (depth || 0) + 1);
                    if (found2) return found2;
                }
            }

            // Search in anyOf
            if (node.anyOf) {
                for (var i = 0; i < node.anyOf.length; i++) {
                    var found3 = this._searchSchema(node.anyOf[i], word, (depth || 0) + 1);
                    if (found3) return found3;
                }
            }

            return null;
        },

        _isZh: function() {
            if (typeof window.__ === 'function') {
                try {
                    return window.__('common.save') !== 'Save';
                } catch (e) { return true; }
            }
            return true;
        },

        _getTransformDescBrief: function(name) {
            var descs = {
                'PathRemovePrefix': '移除路径前缀',
                'PathSet': '设置完整路径',
                'PathPrefix': '添加路径前缀',
                'PathPattern': '模式匹配替换（推荐）',
                'RequestHeader': '添加/修改/移除请求头',
                'RequestHeaderRouteValue': '从路由值取请求头',
                'RequestHeadersAllowed': '允许转发的请求头',
                'RequestHeaderRemove': '移除请求头',
                'RequestHeadersCopy': '控制请求头复制',
                'RequestHeaderOriginalHost': '保留原始 Host',
                'ResponseHeader': '添加/修改响应头',
                'ResponseHeaderRemove': '移除响应头',
                'ResponseHeadersCopy': '控制响应头复制',
                'ResponseHeadersAllowed': '允许转发的响应头',
                'ResponseTrailer': '添加 Trailer',
                'ResponseTrailerRemove': '移除 Trailer',
                'ResponseTrailersCopy': '控制 Trailer 复制',
                'ResponseTrailersAllowed': '允许的 Trailer 列表',
                'X-Forwarded': 'X-Forwarded-* 头',
                'Forwarded': 'RFC 7239 Forwarded 头',
                'ClientCert': '转发客户端证书',
                'QueryValueParameter': '添加查询参数',
                'QueryRemoveParameter': '移除查询参数',
                'QueryRouteParameter': '从路由值设置查询参数',
                'HttpMethodChange': '修改 HTTP 方法'
            };
            return descs[name] || 'YARP Transform';
        },

        /**
         * Update schema dynamically
         */
        updateSchema: function(schema) {
            this._registerSchema(schema);
        },

        /**
         * Get validation errors from Monaco markers and transform to friendly messages.
         * @param {string} containerId - Editor container ID
         * @returns {object} { valid, errors: [{line, message, severity}] }
         */
        getValidationErrors: function(containerId) {
            var instance = this.instances.get(containerId);
            if (!instance) return { valid: true, errors: [] };

            var model = instance.editor.getModel();
            if (!model) return { valid: true, errors: [] };

            var markers = monaco.editor.getModelMarkers({ resource: model.uri });
            var errors = [];

            for (var i = 0; i < markers.length; i++) {
                var marker = markers[i];
                // Only include Error and Warning severity
                if (marker.severity === monaco.MarkerSeverity.Error ||
                    marker.severity === monaco.MarkerSeverity.Warning) {
                    errors.push({
                        line: marker.startLineNumber,
                        column: marker.startColumn,
                        message: this._friendlyErrorMessage(marker.message),
                        severity: marker.severity === monaco.MarkerSeverity.Error ? 'error' : 'warning',
                        raw: marker.message
                    });
                }
            }

            return {
                valid: errors.filter(function(e) { return e.severity === 'error'; }).length === 0,
                errors: errors
            };
        },

        /**
         * Transform raw Monaco schema validation message to user-friendly message.
         * @param {string} raw - Original Monaco error message
         * @returns {string} Friendly message
         */
        _friendlyErrorMessage: function(raw) {
            if (!raw) return raw;

            // Detect locale (simple check: if window.__ exists and is Chinese)
            var isZh = true;
            if (typeof window.__ === 'function') {
                try {
                    var test = window.__('common.save');
                    if (test && test !== '保存') isZh = false;
                } catch (e) { /* default to Chinese */ }
            }

            // Pattern: "Property 'X' is required."
            var reqMatch = raw.match(/Property ['"](.+?)['"] is required/i);
            if (reqMatch) {
                return isZh
                    ? "缺少必需属性 '" + reqMatch[1] + "'，请添加该字段"
                    : "Required property '" + reqMatch[1] + "' is missing";
            }

            // Pattern: "Property 'X' is not allowed."
            var notAllowedMatch = raw.match(/Property ['"](.+?)['"] is not allowed/i);
            if (notAllowedMatch) {
                return isZh
                    ? "不允许的属性 '" + notAllowedMatch[1] + "'，请移除或检查拼写"
                    : "Property '" + notAllowedMatch[1] + "' is not allowed. Remove it or check spelling";
            }

            // Pattern: "Value is not a valid enum value. Valid values: A, B, C"
            var enumMatch = raw.match(/Value is not accepted\. Valid values:/i) ||
                           raw.match(/is not a valid enum value/i);
            if (enumMatch) {
                var valuesMatch = raw.match(/Valid values:?\s*(.+)/i);
                if (valuesMatch) {
                    return isZh
                        ? "值不在允许范围内。可选值: " + valuesMatch[1]
                        : "Value not in allowed range. Valid values: " + valuesMatch[1];
                }
                return isZh ? "值不在允许范围内" : "Value not in allowed range";
            }

            // Pattern: "Incorrect type. Expected 'string'."
            var typeMatch = raw.match(/Incorrect type\. Expected ['"](.+?)['"]/i) ||
                           raw.match(/Value is not of type ['"](.+?)['"]/i);
            if (typeMatch) {
                return isZh
                    ? "类型不正确，期望类型: " + typeMatch[1]
                    : "Type mismatch. Expected: " + typeMatch[1];
            }

            // Pattern: "String does not match pattern"
            if (/does not match (the )?pattern/i.test(raw)) {
                return isZh
                    ? "格式不匹配，请检查值的格式（如时间格式 hh:mm:ss）"
                    : "Format does not match expected pattern";
            }

            // Pattern: "Value must be at most/at least X"
            var maxMatch = raw.match(/must be at most (\d+)/i);
            if (maxMatch) {
                return isZh ? "值不能超过 " + maxMatch[1] : "Value must be at most " + maxMatch[1];
            }
            var minMatch = raw.match(/must be at least (\d+)/i);
            if (minMatch) {
                return isZh ? "值不能小于 " + minMatch[1] : "Value must be at least " + minMatch[1];
            }

            // Fallback: return original message
            return raw;
        }
    };
})();
