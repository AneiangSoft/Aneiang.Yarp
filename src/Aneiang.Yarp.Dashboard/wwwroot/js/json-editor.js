/**
 * JSON Editor with CodeMirror + JSON Schema Auto-completion
 * Lightweight implementation with syntax highlighting and schema hints
 */
(function() {
    'use strict';

    // Load JSON Schema
    var jsonSchema = null;
    var schemaLoaded = false;
    
    // Load schema from file
    function loadSchema(callback) {
        if (schemaLoaded) {
            callback(jsonSchema);
            return;
        }
        
        var xhr = new XMLHttpRequest();
        xhr.open('GET', '/_content/Aneiang.Yarp.Dashboard/ConfigurationSchema.json', true);
        xhr.onreadystatechange = function() {
            if (xhr.readyState === 4 && xhr.status === 200) {
                try {
                    jsonSchema = JSON.parse(xhr.responseText);
                    schemaLoaded = true;
                    callback(jsonSchema);
                } catch (e) {
                    console.error('Failed to parse JSON Schema:', e);
                    callback(null);
                }
            }
        };
        xhr.onerror = function() {
            console.warn('Failed to load JSON Schema, auto-completion disabled');
            callback(null);
        };
        xhr.send();
    }

    // Extract schema properties for hints
    function getSchemaHints(schema, currentPath) {
        if (!schema) return [];
        
        var hints = [];
        var pathParts = currentPath.split('.').filter(p => p);
        
        // Navigate to current schema position
        var currentSchema = schema;
        for (var i = 0; i < pathParts.length; i++) {
            var part = pathParts[i];
            if (currentSchema.properties && currentSchema.properties[part]) {
                currentSchema = currentSchema.properties[part];
            } else if (currentSchema.patternProperties) {
                // For dynamic keys, use patternProperties
                for (var key in currentSchema.patternProperties) {
                    currentSchema = currentSchema.patternProperties[key];
                    break;
                }
            } else {
                break;
            }
        }
        
        // Get available properties
        if (currentSchema.properties) {
            for (var prop in currentSchema.properties) {
                var propSchema = currentSchema.properties[prop];
                hints.push({
                    text: prop,
                    displayText: prop,
                    description: propSchema.description || '',
                    type: propSchema.type || 'any'
                });
            }
        }
        
        return hints;
    }

    // Initialize CodeMirror JSON Editor
    // previewId is optional - if not provided, no live preview is shown
    window.initJsonEditor = function(textareaId, previewId, enableSchema) {
        var textarea = document.getElementById(textareaId);
        var preview = previewId ? document.getElementById(previewId) : null;
        
        if (!textarea) return;
        
        // Check if already initialized - prevent duplicate instances
        if (textarea._codeMirror) {
            textarea._codeMirror.refresh();
            textarea._codeMirror.setValue(textarea._codeMirror.getValue());
            return;
        }
        
        // Check if CodeMirror is loaded
        if (typeof CodeMirror === 'undefined') {
            console.warn('CodeMirror not loaded, using basic textarea editor');
            // Fallback to basic textarea with real-time validation
            textarea.addEventListener('input', function() {
                var value = this.value;
                try {
                    var obj = JSON.parse(value);
                    if (preview) {
                        preview.innerHTML = syntaxHighlight(obj);
                        preview.style.display = 'block';
                    }
                    this.style.borderColor = '#28a745';
                } catch (e) {
                    if (preview) {
                        preview.innerHTML = '<span style="color: #dc3545;"> ' + escapeHtml(e.message) + '</span>';
                        preview.style.display = 'block';
                    }
                    this.style.borderColor = '#dc3545';
                }
            });
            
            // Tab key support
            textarea.addEventListener('keydown', function(e) {
                if (e.key === 'Tab') {
                    e.preventDefault();
                    var start = this.selectionStart;
                    var end = this.selectionEnd;
                    this.value = this.value.substring(0, start) + '  ' + this.value.substring(end);
                    this.selectionStart = this.selectionEnd = start + 2;
                    this.dispatchEvent(new Event('input'));
                }
            });
            
            // Trigger initial validation
            textarea.dispatchEvent(new Event('input'));
            return;
        }
        
        // Create CodeMirror editor
        var editor = CodeMirror.fromTextArea(textarea, {
            mode: { name: "javascript", json: true },
            theme: "material-darker",
            lineNumbers: true,
            lineWrapping: true,
            tabSize: 2,
            indentWithTabs: false,
            autoCloseBrackets: true,
            matchBrackets: true,
            styleActiveLine: true,
            extraKeys: {
                "Ctrl-Space": function(cm) {
                    // Manual trigger for schema hints
                    if (enableSchema !== false) {
                        triggerSchemaHint(cm);
                    } else {
                        cm.showHint({ hint: CodeMirror.hint.javascript });
                    }
                },
                "Tab": function(cm) {
                    if (cm.somethingSelected()) {
                        cm.indentSelection("add");
                    } else {
                        cm.replaceSelection("  ", "end");
                    }
                }
            }
        });
        
        // Real-time validation and preview (only if preview element exists)
        editor.on('change', function(cm) {
            var value = cm.getValue();
            if (preview) {
                try {
                    var obj = JSON.parse(value);
                    preview.innerHTML = syntaxHighlight(obj);
                    preview.style.display = 'block';
                    textarea.style.borderColor = '#28a745';
                } catch (e) {
                    preview.innerHTML = '<span style="color: #dc3545;"> ' + escapeHtml(e.message) + '</span>';
                    preview.style.display = 'block';
                    textarea.style.borderColor = '#dc3545';
                }
            }
        });
        
        // Store editor instance
        textarea._codeMirror = editor;
        
        // Trigger initial validation
        editor.refresh();
        editor.setValue(editor.getValue());
    };

    // Trigger schema-based hint
    function triggerSchemaHint(cm) {
        var cursor = cm.getCursor();
        var token = cm.getTokenAt(cursor);
        
        loadSchema(function(schema) {
            if (!schema) {
                cm.showHint({ hint: CodeMirror.hint.javascript });
                return;
            }
            
            // Get current path from cursor position
            var content = cm.getValue();
            var lines = content.split('\n');
            var currentLine = lines[cursor.line] || '';
            
            // Simple path detection (can be enhanced)
            var path = detectCurrentPath(lines, cursor.line);
            
            var hints = getSchemaHints(schema, path);
            
            if (hints.length > 0) {
                cm.showHint({
                    hint: function(editor, options) {
                        return {
                            list: hints.map(function(h) {
                                return {
                                    text: '"' + h.text + '"',
                                    displayText: h.text,
                                    render: function(elt, data, cur) {
                                        elt.innerHTML = '<div style="padding: 2px 0;">' +
                                            '<strong style="color: #9cdcfe;">' + escapeHtml(h.text) + '</strong>' +
                                            (h.description ? '<br/><small style="color: #808080;">' + escapeHtml(h.description.substring(0, 80)) + '</small>' : '') +
                                            '</div>';
                                    }
                                };
                            }),
                            from: cursor,
                            to: cursor
                        };
                    }
                });
            } else {
                cm.showHint({ hint: CodeMirror.hint.javascript });
            }
        });
    }

    // Simple path detection (can be improved)
    function detectCurrentPath(lines, lineNum) {
        var path = [];
        var braceCount = 0;
        
        for (var i = 0; i <= lineNum && i < lines.length; i++) {
            var line = lines[i].trim();
            
            // Count braces to track nesting
            for (var j = 0; j < line.length; j++) {
                if (line[j] === '{') braceCount++;
                if (line[j] === '}') braceCount--;
            }
            
            // Extract property names
            var match = line.match(/^"([^"]+)":/);
            if (match && braceCount > 0) {
                path.push(match[1]);
            }
        }
        
        return path.join('.');
    }

    // Format JSON using CodeMirror
    window.formatJsonInTextarea = function(textareaId) {
        var textarea = document.getElementById(textareaId);
        var editor = textarea._codeMirror;
        
        if (!editor) {
            // Fallback to original implementation
            try {
                var obj = JSON.parse(textarea.value);
                textarea.value = JSON.stringify(obj, null, 2);
                textarea.dispatchEvent(new Event('input'));
                return true;
            } catch (e) {
                alert('Invalid JSON: ' + e.message);
                return false;
            }
        }
        
        try {
            var obj = JSON.parse(editor.getValue());
            var formatted = JSON.stringify(obj, null, 2);
            editor.setValue(formatted);
            return true;
        } catch (e) {
            alert('Invalid JSON: ' + e.message);
            return false;
        }
    };

    // Minify JSON using CodeMirror
    window.minifyJsonInTextarea = function(textareaId) {
        var textarea = document.getElementById(textareaId);
        var editor = textarea._codeMirror;
        
        if (!editor) {
            // Fallback
            try {
                var obj = JSON.parse(textarea.value);
                textarea.value = JSON.stringify(obj);
                textarea.dispatchEvent(new Event('input'));
                return true;
            } catch (e) {
                alert('Invalid JSON: ' + e.message);
                return false;
            }
        }
        
        try {
            var obj = JSON.parse(editor.getValue());
            editor.setValue(JSON.stringify(obj));
            return true;
        } catch (e) {
            alert('Invalid JSON: ' + e.message);
            return false;
        }
    };

    // Copy JSON to clipboard
    window.copyJsonToClipboard = function(textareaId) {
        var textarea = document.getElementById(textareaId);
        var editor = textarea._codeMirror;
        var value = editor ? editor.getValue() : textarea.value;
        
        navigator.clipboard.writeText(value).then(function() {
            var btn = event.target.closest('button');
            var originalHtml = btn.innerHTML;
            btn.innerHTML = '<i class="bi bi-check"></i> Copied!';
            btn.classList.remove('btn-outline-secondary');
            btn.classList.add('btn-success');
            setTimeout(function() {
                btn.innerHTML = originalHtml;
                btn.classList.remove('btn-success');
                btn.classList.add('btn-outline-secondary');
            }, 2000);
        });
    };

    // Syntax highlighting for JSON (VS Code Dark+ theme)
    function syntaxHighlight(json) {
        if (typeof json !== 'string') {
            json = JSON.stringify(json, null, 2);
        }
        
        json = json.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
        
        return json.replace(
            /(\"(\\u[a-zA-Z0-9]{4}|\\[^u]|[^\\"])*\"(\s*:)?|\b(true|false|null)\b|-?\d+(?:\.\d*)?(?:[eE][+\-]?\d+)?)/g,
            function(match) {
                var cls = 'json-number';
                if (/^"/.test(match)) {
                    if (/:$/.test(match)) {
                        cls = 'json-key';
                    } else {
                        cls = 'json-string';
                    }
                } else if (/true|false/.test(match)) {
                    cls = 'json-boolean';
                } else if (/null/.test(match)) {
                    cls = 'json-null';
                }
                return '<span class="' + cls + '">' + match + '</span>';
            }
        );
    }

    // Escape HTML
    function escapeHtml(text) {
        var div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    // Inject CSS styles
    var style = document.createElement('style');
    style.textContent = `
        /* JSON Syntax Highlighting */
        .json-key { color: #9cdcfe; }
        .json-string { color: #ce9178; }
        .json-number { color: #b5cea8; }
        .json-boolean { color: #569cd6; }
        .json-null { color: #569cd6; }
        
        /* JSON Preview Panel */
        .json-preview {
            font-family: 'Consolas', 'Monaco', 'Courier New', monospace;
            font-size: 13px;
            line-height: 1.5;
            padding: 12px;
            background: #1e1e1e;
            color: #d4d4d4;
            border-radius: 6px;
            max-height: 400px;
            overflow: auto;
            white-space: pre;
            margin-top: 8px;
        }
        
        /* CodeMirror Customization */
        .CodeMirror {
            height: 350px !important;
            border: 1px solid #404040;
            border-radius: 4px;
        }
        
        /* Ensure CodeMirror scrollbars are at the bottom */
        .CodeMirror-scroll {
            min-height: 350px;
        }
        
        .CodeMirror-hints {
            font-family: 'Consolas', monospace;
            font-size: 13px;
            max-height: 200px;
            max-width: 400px;
            overflow-y: auto;
            background: #2d2d30;
            border: 1px solid #3e3e42;
            border-radius: 4px;
            box-shadow: 0 4px 12px rgba(0,0,0,0.4);
        }
        
        .CodeMirror-hint {
            padding: 4px 8px;
            color: #d4d4d4;
            border-bottom: 1px solid #3e3e42;
        }
        
        .CodeMirror-hint-active {
            background: #094771 !important;
            color: #ffffff;
        }
        
        .CodeMirror-hint:hover {
            background: #264f78;
        }
    `;
    document.head.appendChild(style);
})();
