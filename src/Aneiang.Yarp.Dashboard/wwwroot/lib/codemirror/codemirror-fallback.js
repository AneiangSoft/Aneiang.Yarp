// CodeMirror Local Fallback
// This file provides a fallback when CDN CodeMirror fails to load
// In production, you should download the actual CodeMirror files

if (typeof CodeMirror === 'undefined') {
    console.warn('CodeMirror CDN failed to load, using minimal implementation');
    
    // Minimal CodeMirror stub
    window.CodeMirror = {
        fromTextArea: function(textarea, options) {
            console.warn('Using fallback textarea editor (CodeMirror not available)');
            
            // Return a minimal editor interface
            return {
                getValue: function() {
                    return textarea.value;
                },
                setValue: function(val) {
                    textarea.value = val;
                    textarea.dispatchEvent(new Event('input'));
                },
                on: function(event, handler) {
                    if (event === 'change') {
                        textarea.addEventListener('input', function() {
                            handler(this);
                        });
                    }
                },
                refresh: function() {
                    // No-op for fallback
                },
                getCursor: function() {
                    return { line: 0, ch: 0 };
                },
                getTokenAt: function() {
                    return { string: '' };
                },
                showHint: function() {
                    console.warn('CodeMirror hints not available in fallback mode');
                },
                somethingSelected: function() {
                    return false;
                },
                indentSelection: function() {
                    // No-op
                },
                replaceSelection: function(text) {
                    var start = textarea.selectionStart;
                    var end = textarea.selectionEnd;
                    textarea.value = textarea.value.substring(0, start) + text + textarea.value.substring(end);
                    textarea.selectionStart = textarea.selectionEnd = start + text.length;
                }
            };
        },
        hint: {
            javascript: function() {
                return { list: [], from: { line: 0, ch: 0 }, to: { line: 0, ch: 0 } };
            }
        }
    };
}
