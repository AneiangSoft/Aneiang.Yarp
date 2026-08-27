/**
 * Batch plugin binding dialog.
 *
 * Shared modal used by both the cluster list and the route list to bind a single
 * plugin (with one shared config payload) to multiple routes/clusters in one
 * atomic call to POST /api/plugin-bindings/batch-create.
 *
 * The config input mirrors the single-binding editor: a schema-driven form
 * rendered via window.DashboardCapabilities (renderGroupedFields / readSchemaForm),
 * NOT a raw JSON textarea. Plugins with no editable schema fields bind with an
 * empty {} config (plugin defaults).
 *
 * Usage:
 *   window.DashboardBatchPlugin.openDialog({
 *     scope: 'Cluster' | 'Route',
 *     scopeIds: [...],
 *     scopeLabel: '集群' | '路由',
 *     onDone: function() { /* refresh list *​/ }
 *   });
 */
(function () {
    'use strict';

    var ns = (window.DashboardBatchPlugin = window.DashboardBatchPlugin || {});

    var state = { modal: null, opts: null, plugins: [], plugin: null, schema: null, config: {} };

    function Cap() { return window.DashboardCapabilities || null; }

    function esc(value) {
        if (window.DashboardUtils && DashboardUtils.escapeHtml) return DashboardUtils.escapeHtml(String(value == null ? '' : value));
        return String(value == null ? '' : value)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    function __(key, fallback) {
        if (window.__ && typeof window.__ === 'function') {
            var v = window.__(key);
            return (v && v !== key) ? v : (fallback || key);
        }
        return fallback || key;
    }

    function scopeName(scope) {
        return scope === 'Route'
            ? __('batch.scope.route', '路由')
            : __('batch.scope.cluster', '集群');
    }

    function buildModalHtml() {
        var opts = state.opts;
        var scope = scopeName(opts.scope);
        return ''
            + '<div class="modal-header">'
            +   '<h5 class="modal-title"><i class="bi bi-plug me-2" style="color:#3b82f6;"></i>' + esc(__('batch.bindPluginTitle', '批量绑插件')) + '</h5>'
            +   '<button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Close"></button>'
            + '</div>'
            + '<div class="modal-body">'
            +   '<div class="mb-3">'
            +     '<label class="form-label fw-semibold">' + esc(scope) + ' <span class="text-muted">(' + esc(__('batch.selected', '已选')) + ' ' + opts.scopeIds.length + ')</span></label>'
            +     '<div class="form-control-plaintext text-muted small" style="max-height:60px;overflow:auto;">' + opts.scopeIds.map(esc).join('、') + '</div>'
            +   '</div>'
            +   '<div class="mb-3">'
            +     '<label class="form-label fw-semibold">' + esc(__('batch.plugin', '插件')) + '</label>'
            +     '<select id="batch-plugin-select" class="form-select">'
            +       '<option value="">' + esc(__('batch.selectPlugin', '请选择插件')) + '</option>'
            +     '</select>'
            +     '<div id="batch-plugin-meta" class="form-text text-muted small mt-1"></div>'
            +   '</div>'
            +   '<div class="mb-3">'
            +     '<label class="form-label fw-semibold">' + esc(__('batch.configForm', '插件配置')) + '</label>'
            +     '<div id="batch-plugin-schema-form" data-role="schema-form"></div>'
            +     '<div id="batch-plugin-schema-empty" class="form-text text-muted small d-none">' + esc(__('batch.noSchema', '此插件无可配置项，将使用默认配置')) + '</div>'
            +   '</div>'
            +   '<div class="d-flex gap-4 mb-2">'
            +     '<div class="form-check form-switch">'
            +       '<input class="form-check-input" type="checkbox" id="batch-plugin-enabled" checked>'
            +       '<label class="form-check-label" for="batch-plugin-enabled">' + esc(__('batch.bindingEnabled', '启用绑定')) + '</label>'
            +     '</div>'
            +     '<div class="form-check form-switch">'
            +       '<input class="form-check-input" type="checkbox" id="batch-plugin-overwrite">'
            +       '<label class="form-check-label" for="batch-plugin-overwrite">' + esc(__('batch.overwrite', '覆盖已存在绑定')) + '</label>'
            +     '</div>'
            +   '</div>'
            +   '<div id="batch-plugin-result" class="mt-2"></div>'
            + '</div>'
            + '<div class="modal-footer">'
            +   '<button type="button" class="btn btn-outline-secondary" data-bs-dismiss="modal">' + esc(__('common.cancel', '取消')) + '</button>'
            +   '<button type="button" class="btn btn-primary" id="batch-plugin-submit">'
            +     '<i class="bi bi-check-lg"></i> ' + esc(__('batch.bind', '绑定'))
            +   '</button>'
            + '</div>';
    }

    function ensureModal() {
        var existing = document.getElementById('batch-plugin-modal');
        if (existing) existing.remove();
        var el = document.createElement('div');
        el.id = 'batch-plugin-modal';
        el.className = 'modal fade';
        el.setAttribute('tabindex', '-1');
        el.innerHTML = '<div class="modal-dialog modal-lg modal-dialog-centered"><div class="modal-content">' + buildModalHtml() + '</div></div>';
        document.body.appendChild(el);
        state.modal = new bootstrap.Modal(el);
        el.addEventListener('hidden.bs.modal', function () { el.remove(); state.modal = null; });
        el.querySelector('#batch-plugin-submit').addEventListener('click', onSubmit);
        el.querySelector('#batch-plugin-select').addEventListener('change', onPluginChange);
        return el;
    }

    // Render the schema-driven config form for the selected plugin — same UI as the
    // single-binding editor (DashboardCapabilities.renderGroupedFields).
    function renderSchemaForm() {
        var formHost = state.modal._element.querySelector('#batch-plugin-schema-form');
        var emptyNotice = state.modal._element.querySelector('#batch-plugin-schema-empty');
        var cap = Cap();
        formHost.replaceChildren();
        emptyNotice.classList.add('d-none');

        var schema = state.schema;
        var hasFields = schema && schema.properties && Object.keys(schema.properties).length > 0;
        if (!hasFields) {
            // No editable config (e.g. proxy-log) — bind with empty {} config / plugin defaults.
            emptyNotice.classList.remove('d-none');
            return;
        }
        if (cap && cap.renderGroupedFields) {
            cap.renderGroupedFields(formHost, schema, state.config, state.plugin.pluginId);
        } else {
            // Cap unavailable — degrade to a notice rather than a raw JSON textarea.
            emptyNotice.textContent = __('batch.noSchema', '此插件无可配置项，将使用默认配置');
            emptyNotice.classList.remove('d-none');
        }
    }

    function onPluginChange() {
        var sel = state.modal._element.querySelector('#batch-plugin-select');
        var meta = state.modal._element.querySelector('#batch-plugin-meta');
        var pluginId = sel.value;
        var p = state.plugins.find(function (x) { return x.pluginId === pluginId; });
        if (!p) {
            state.plugin = null; state.schema = null; state.config = {};
            meta.innerHTML = '';
            renderSchemaForm();
            return;
        }
        state.plugin = p;
        var cap = Cap();
        state.schema = cap && cap.getPluginSchema ? cap.getPluginSchema(p) : null;
        state.config = cap && cap.applyDefaults ? cap.applyDefaults(state.schema, {}) : {};
        meta.innerHTML = '<i class="bi bi-info-circle"></i> ' + esc(p.displayName || p.pluginId)
            + (p.description ? ' — ' + esc(p.description) : '')
            + ' <span class="badge bg-light text-dark ms-1">v' + esc(p.version || '1') + '</span>';
        renderSchemaForm();
    }

    function loadPlugins() {
        var sel = state.modal._element.querySelector('#batch-plugin-select');
        sel.innerHTML = '<option value="">' + esc(__('batch.loading', '加载中...')) + '</option>';
        sel.disabled = true;
        window.DashboardApi.endpoints.getInstalledPlugins()
            .then(function (resp) {
                var list = (resp && resp.data) || resp || [];
                if (!Array.isArray(list)) list = list && list.items ? list.items : [];
                state.plugins = list.filter(function (p) {
                    var scopes = p.scopes || p.Scopes || [];
                    return scopes.some(function (s) {
                        var v = typeof s === 'string' ? s : (s.name || s);
                        return String(v).toLowerCase() === state.opts.scope.toLowerCase();
                    });
                });
                var opts = state.plugins.map(function (p) {
                    return '<option value="' + esc(p.pluginId) + '">' + esc(p.displayName || p.pluginId) + '</option>';
                }).join('');
                sel.innerHTML = '<option value="">' + esc(__('batch.selectPlugin', '请选择插件')) + '</option>' + opts;
                sel.disabled = state.plugins.length === 0;
                if (state.plugins.length === 0) {
                    sel.innerHTML = '<option value="">' + esc(__('batch.noPluginForScope', '当前作用域无可用插件')) + '</option>';
                }
            })
            .catch(function () {
                sel.innerHTML = '<option value="">' + esc(__('batch.loadFailed', '插件加载失败')) + '</option>';
                sel.disabled = true;
            });
    }

    function setResult(html) {
        var el = state.modal._element.querySelector('#batch-plugin-result');
        if (el) el.innerHTML = html;
    }

    function setSubmitting(flag) {
        var btn = state.modal._element.querySelector('#batch-plugin-submit');
        if (!btn) return;
        btn.disabled = !!flag;
        btn.innerHTML = flag
            ? '<span class="spinner-border spinner-border-sm"></span>'
            : '<i class="bi bi-check-lg"></i> ' + esc(__('batch.bind', '绑定'));
    }

    function onSubmit() {
        var el = state.modal._element;
        var pluginId = el.querySelector('#batch-plugin-select').value;
        var enabled = el.querySelector('#batch-plugin-enabled').checked;
        var overwrite = el.querySelector('#batch-plugin-overwrite').checked;

        if (!pluginId || !state.plugin) {
            setResult('<div class="alert alert-warning py-2 mb-0"><i class="bi bi-exclamation-triangle"></i> ' + esc(__('batch.selectPluginFirst', '请先选择插件')) + '</div>');
            return;
        }

        // Collect config from the schema form (same path as the single-binding editor).
        var config = state.config;
        var cap = Cap();
        if (state.schema && cap && cap.readSchemaForm) {
            var formEl = el.querySelector('#batch-plugin-schema-form');
            var collected = cap.readSchemaForm(formEl, state.schema, state.config);
            if (collected === null) {
                setResult('<div class="alert alert-warning py-2 mb-0"><i class="bi bi-exclamation-triangle"></i> ' + esc(__('batch.validationError', '请检查表单填写')) + '</div>');
                return;
            }
            config = collected;
        }

        var schemaVersion = 1;
        var capForVersion = Cap();
        if (capForVersion && capForVersion.getPluginSchemaVersion) {
            schemaVersion = capForVersion.getPluginSchemaVersion(state.plugin);
        }

        setSubmitting(true);
        setResult('');
        window.DashboardApi.endpoints.batchCreateBindings({
            pluginId: pluginId,
            scope: state.opts.scope,
            scopeIds: state.opts.scopeIds,
            configJson: JSON.stringify(config),
            enabled: enabled,
            schemaVersion: schemaVersion,
            order: 0,
            overwriteExisting: overwrite
        }).then(function (resp) {
            var data = (resp && resp.data) || resp || {};
            var succeeded = data.succeeded != null ? data.succeeded : (data.total || 0);
            var failed = data.failed != null ? data.failed : 0;
            var items = data.items || [];
            if (failed === 0) {
                setResult('<div class="alert alert-success py-2 mb-0"><i class="bi bi-check-circle"></i> '
                    + esc(__('batch.bindSuccess', '绑定成功') + ' (' + succeeded + ')') + '</div>');
                setTimeout(function () {
                    if (state.modal) state.modal.hide();
                    if (state.opts.onDone) state.opts.onDone();
                }, 800);
            } else {
                var failedList = items.filter(function (it) { return !it.success; })
                    .map(function (it) { return '<li>' + esc(it.id) + ' — ' + esc(it.message) + '</li>'; }).join('');
                setResult('<div class="alert alert-warning py-2 mb-0"><i class="bi bi-exclamation-triangle"></i> '
                    + esc(__('batch.bindPartial', '部分失败') + ' (' + succeeded + '/' + data.total + ')')
                    + (failedList ? '<ul class="mb-0 mt-1 small">' + failedList + '</ul>' : '') + '</div>');
            }
        }).catch(function (e) {
            setResult('<div class="alert alert-danger py-2 mb-0"><i class="bi bi-x-circle"></i> ' + esc(e.message || __('api.requestFailed', '请求失败')) + '</div>');
        }).finally(function () { setSubmitting(false); });
    }

    ns.openDialog = function (opts) {
        if (!opts || !opts.scope || !opts.scopeIds || opts.scopeIds.length === 0) return;
        state.opts = opts;
        state.plugin = null; state.schema = null; state.config = {};
        ensureModal();
        loadPlugins();
        state.modal.show();
    };
})();
