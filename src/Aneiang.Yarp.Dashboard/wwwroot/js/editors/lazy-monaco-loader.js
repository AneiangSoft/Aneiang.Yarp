/**
 * Lazy Monaco Editor Loader
 * Dynamically injects Monaco loader and editor.main only when the first
 * editor instance is requested — avoiding the ~10-20MB upfront cost on every page.
 *
 * Usage: call LazyMonacoLoader.ensure().then(() => { /* use monaco *\/ });
 */
(function() {
    'use strict';

    var MONACO_BASE = '/_content/Aneiang.Yarp.Dashboard/lib/monaco';
    var LOADER_URL = MONACO_BASE + '/loader.js';
    var EDITOR_MAIN = MONACO_BASE + '/vs/editor/editor.main';

    // Singleton promise — resolved once Monaco is fully loaded.
    // All callers share the same promise so Monaco is only loaded once.
    var _loadPromise = null;

    /**
     * Returns a promise that resolves when Monaco Editor is fully initialized.
     * If Monaco is already loaded, resolves immediately.
     * If another caller is already loading, waits for that to finish.
     * If no one is loading yet, triggers lazy load.
     */
    function ensure() {
        // Fast path: already loaded
        if (typeof monaco !== 'undefined' && monaco.editor) {
            return Promise.resolve();
        }

        // If a load is already in flight, wait for it
        if (_loadPromise) {
            return _loadPromise;
        }

        // First caller — start lazy load
        _loadPromise = _doLoad();
        return _loadPromise;
    }

    function _doLoad() {
        return new Promise(function(resolve, reject) {
            // Set worker environment before loading editor.main
            // This must be set before editor.main runs
            window.MonacoEnvironment = {
                getWorkerUrl: function(workerId, label) {
                    if (label === 'json') return MONACO_BASE + '/language/json/jsonWorker.js';
                    if (label === 'css') return MONACO_BASE + '/language/css/cssWorker.js';
                    if (label === 'html') return MONACO_BASE + '/language/html/htmlWorker.js';
                    if (label === 'typescript' || label === 'javascript') {
                        return MONACO_BASE + '/language/typescript/tsWorker.js';
                    }
                    return MONACO_BASE + '/editor/editor.worker.js';
                }
            };

            // Dynamically inject the Monaco AMD loader if not present
            if (typeof require === 'undefined') {
                var loaderScript = document.createElement('script');
                loaderScript.src = LOADER_URL;
                loaderScript.onerror = function() {
                    reject(new Error('Failed to load Monaco loader from ' + LOADER_URL));
                };
                document.head.appendChild(loaderScript);
            }

            // Configure require paths for Monaco modules
            var configure = function() {
                if (typeof require !== 'undefined') {
                    require.config({ paths: { vs: MONACO_BASE } });
                }
            };

            // If require is already available, configure immediately
            if (typeof require !== 'undefined') {
                configure();
                loadEditorMain(resolve, reject);
            } else {
                // Wait for loader.js to load, then configure
                var checkRequire = setInterval(function() {
                    if (typeof require !== 'undefined') {
                        clearInterval(checkRequire);
                        configure();
                        loadEditorMain(resolve, reject);
                    }
                }, 10);
            }
        });
    }

    function loadEditorMain(resolve, reject) {
        var timeout = setTimeout(function() {
            reject(new Error('Monaco editor.main load timeout after 60s'));
        }, 60000);

        require(['vs/editor/editor.main'], function() {
            clearTimeout(timeout);
            resolve();
        });
    }

    window.LazyMonacoLoader = {
        ensure: ensure,

        /** Returns true if Monaco is currently loaded. */
        isLoaded: function() {
            return typeof monaco !== 'undefined' && monaco.editor;
        },

        /** Preload Monaco without blocking — fires and forgets. */
        preload: function() {
            ensure().catch(function(err) {
                console.warn('[LazyMonaco] Preload failed:', err.message);
            });
        }
    };
})();
