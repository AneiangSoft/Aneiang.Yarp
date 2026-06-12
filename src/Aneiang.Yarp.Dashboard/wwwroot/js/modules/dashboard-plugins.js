/**
 * Plugin Management Module - Plugin list and toggle
 */
(function() {
    'use strict';

    var PluginModule = {
        name: 'plugin',
        initialized: false,
        autoRefreshInterval: null,

        init: function() {
            if (this.initialized) return;
            this.setupEvents();
            this.initialized = true;
        },

        setupEvents: function() {
            var self = this;
            document.addEventListener('dashboard:ready', function() {
                if (self.autoRefreshInterval) clearInterval(self.autoRefreshInterval);
                self.autoRefreshInterval = setInterval(function() {
                    self.load();
                }, 30000);
            });
            document.addEventListener('dashboard:localeChange', function() { self.load(); });
        },

        destroy: function() {
            if (this.autoRefreshInterval) {
                clearInterval(this.autoRefreshInterval);
                this.autoRefreshInterval = null;
            }
            this.initialized = false;
        },

        load: async function() {
            try {
                var container = document.getElementById('plugin-content');
                if (!container) return;

                window.DashboardDOM.showLoading(container, __('plugin.loading'));

                var data = await window.DashboardApi.getPlugins();
                this.render(data, container);
                this.updateRefreshTime();
            } catch (error) {
                console.error('[Plugin] Load failed:', error);
                var container = document.getElementById('plugin-content');
                if (container) {
                    container.innerHTML = '<div class="alert alert-danger">' + __('plugin.loadFailed') + '</div>';
                }
            }
        },

        getPluginIcon: function(pluginId) {
            if (pluginId && pluginId.toLowerCase().includes('circuit')) return 'bi-lightning-charge';
            if (pluginId && pluginId.toLowerCase().includes('retry')) return 'bi-arrow-repeat';
            if (pluginId && pluginId.toLowerCase().includes('waf')) return 'bi-shield-lock';
            if (pluginId && pluginId.toLowerCase().includes('rate')) return 'bi-speedometer2';
            return 'bi-puzzle';
        },

        getPluginColor: function(pluginId) {
            if (pluginId && pluginId.toLowerCase().includes('circuit')) return '#6366f1';
            if (pluginId && pluginId.toLowerCase().includes('retry')) return '#0ea5e9';
            if (pluginId && pluginId.toLowerCase().includes('waf')) return '#f59e0b';
            if (pluginId && pluginId.toLowerCase().includes('rate')) return '#0ea5e9';
            return '#64748b';
        },

        render: function(data, container) {
            window.DashboardDOM.clear(container);

            var plugins = Array.isArray(data) ? data : (data && data.plugins) || [];
            var enabledCount = plugins.filter(function(p) { return p.enabled; }).length;

            var summaryHtml =
                '<div class="row mb-3">' +
                    '<div class="col-md-4">' +
                        '<div class="stat-mini-card">' +
                            '<div class="stat-mini-value">' + plugins.length + '</div>' +
                            '<div class="stat-mini-label">' + (window.__dashboard?.I18N?.['plugin.totalShort'] || 'Total') + '</div>' +
                        '</div>' +
                    '</div>' +
                    '<div class="col-md-4">' +
                        '<div class="stat-mini-card">' +
                            '<div class="stat-mini-value text-success">' + enabledCount + '</div>' +
                            '<div class="stat-mini-label">' + __('plugin.enabled') + '</div>' +
                        '</div>' +
                    '</div>' +
                    '<div class="col-md-4">' +
                        '<div class="stat-mini-card">' +
                            '<div class="stat-mini-value text-secondary">' + (plugins.length - enabledCount) + '</div>' +
                            '<div class="stat-mini-label">' + __('plugin.disabled') + '</div>' +
                        '</div>' +
                    '</div>' +
                '</div>';

            if (plugins.length === 0) {
                container.innerHTML = summaryHtml +
                    '<div class="text-center py-5">' +
                        '<i class="bi bi-puzzle text-muted" style="font-size:48px;"></i>' +
                        '<p class="text-muted mt-3">' + __('plugin.empty') + '</p>' +
                        '<p class="text-muted small">' + __('plugin.emptyHelp') + '</p>' +
                    '</div>';
                return;
            }

            var cards = plugins.map(function(plugin) {
                var icon = this.getPluginIcon(plugin.pluginId);
                var color = this.getPluginColor(plugin.pluginId);
                var localizedName = __('plugin.name.' + plugin.pluginId) || plugin.displayName || plugin.pluginId;
                var localizedDesc = __('plugin.desc.' + plugin.pluginId) || plugin.description || '-';
                var enabledBadge = plugin.enabled
                    ? '<span class="badge bg-success">' + __('plugin.enabled') + '</span>'
                    : '<span class="badge bg-secondary">' + __('plugin.disabled') + '</span>';
                var toggleClass = plugin.enabled ? 'btn-outline-danger' : 'btn-outline-success';
                var toggleIcon = plugin.enabled ? 'bi-toggle-on text-success' : 'bi-toggle-off text-secondary';
                var toggleLabel = plugin.enabled ? __('plugin.toggleOff') : __('plugin.toggleOn');

                return '<div class="card-panel mb-3" style="border-left: 4px solid ' + color + ';">' +
                    '<div class="card-body">' +
                        '<div class="d-flex align-items-start gap-3">' +
                            '<div class="flex-shrink-0" style="width:48px;height:48px;background:' + color + '15;border-radius:12px;display:flex;align-items:center;justify-content:center;">' +
                                '<i class="bi ' + icon + '" style="font-size:24px;color:' + color + ';"></i>' +
                            '</div>' +
                            '<div class="flex-grow-1">' +
                                '<div class="d-flex align-items-center gap-2 mb-1">' +
                                    '<strong>' + window.DashboardUtils.escapeHtml(localizedName) + '</strong>' +
                                    enabledBadge +
                                '</div>' +
                                '<div class="text-muted small mb-1"><code>' + window.DashboardUtils.escapeHtml(plugin.pluginId) + '</code></div>' +
                                '<div class="text-muted small">' + window.DashboardUtils.escapeHtml(localizedDesc) + '</div>' +
                            '</div>' +
                            '<div class="flex-shrink-0 d-flex flex-column align-items-end gap-2">' +
                                '<span class="badge bg-light text-dark border">' +
                                    '<i class="bi bi-tag me-1"></i>v' + window.DashboardUtils.escapeHtml(plugin.version || '1.0') + '</span>' +
                                '<button class="btn btn-sm ' + toggleClass + ' d-flex align-items-center gap-1" onclick="PluginModule.togglePlugin(\'' + window.DashboardUtils.escapeHtml(plugin.pluginId) + '\', ' + !plugin.enabled + ')" title="' + toggleLabel + '">' +
                                    '<i class="bi ' + toggleIcon + '"></i>' +
                                '</button>' +
                            '</div>' +
                        '</div>' +
                    '</div>' +
                '</div>';
            }.bind(this)).join('');

            container.innerHTML = summaryHtml + cards;
        },

        togglePlugin: async function(pluginId, enable) {
            var localizedName = __('plugin.name.' + pluginId) || pluginId;
            var action = enable ? __('plugin.toggleOn') : __('plugin.toggleOff');
            var msg = __('plugin.toggleConfirm').replace('{action}', action).replace('{name}', localizedName);
            if (!confirm(msg)) return;

            try {
                await window.DashboardApi.togglePlugin(pluginId, enable);
                if (window.DashboardModals) {
                    window.DashboardModals.showToast(enable ? __('plugin.enableSuccess') : __('plugin.disableSuccess'), 'success');
                }
                await this.load();
            } catch (error) {
                console.error('[Plugin] Toggle failed:', error);
                if (window.DashboardModals) {
                    window.DashboardModals.showError(__('plugin.toggleFailed'));
                }
            }
        },

        resetAll: async function() {
            if (!confirm(__('plugin.resetConfirm'))) return;
            try {
                await window.DashboardApi.resetPlugins();
                if (window.DashboardModals) {
                    window.DashboardModals.showToast(__('plugin.resetSuccess'), 'success');
                }
                await this.load();
            } catch (error) {
                console.error('[Plugin] Reset failed:', error);
                if (window.DashboardModals) {
                    window.DashboardModals.showError(__('plugin.resetFailed'));
                }
            }
        },

        updateRefreshTime: function() {
            var el = document.getElementById('plugin-refresh-time');
            if (el) {
                el.textContent = window.DashboardI18n.formatDate(new Date());
            }
        }
    };

    if (window.DashboardApp) {
        window.DashboardApp.registerModule('plugin', PluginModule);
    }
    window.PluginModule = PluginModule;
})();
