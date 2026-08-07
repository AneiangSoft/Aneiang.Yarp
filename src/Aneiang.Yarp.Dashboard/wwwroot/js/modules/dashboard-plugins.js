/**
 * Plugin Management Module - Plugin list and toggle
 */
(function() {
    'use strict';

    var PluginModule = {

        /** Try i18n lookup, fall back to provided default if key not found */
        _t: function(key, fallback) {
            var text = window.DashboardI18n && window.DashboardI18n.translations ? window.DashboardI18n.translations[key] : null;
            return text || fallback || key;
        },

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

        healthBadge: function(health) {
            var value = health || 'Unknown';
            var badge = value === 'Healthy' ? 'bg-success'
                : value === 'Disabled' || value === 'Stopped' ? 'bg-secondary'
                : value === 'Degraded' || value === 'Starting' || value === 'Stopping' ? 'bg-warning text-dark'
                : 'bg-danger';
            return '<span class="badge ' + badge + '">' + window.DashboardUtils.escapeHtml(value) + '</span>';
        },

        renderDeclaredResources: function(resources) {
            if (!resources || resources.length === 0) return '<span class="text-muted">' + __('plugin.resourcesNone') + '</span>';
            return resources.map(function(resource) {
                var capacity = resource.capacity ? ' (' + window.DashboardUtils.escapeHtml(resource.capacity) + ')' : '';
                return '<span class="badge bg-light text-dark border me-1 mb-1">' + window.DashboardUtils.escapeHtml(resource.type) + capacity + '</span>';
            }).join('');
        },

        renderRuntimeResources: function(resources) {
            var self = this;
            if (!resources || resources.length === 0) return '<span class="text-muted">' + __('plugin.resourcesNone') + '</span>';
            return resources.map(function(resource) {
                var status = resource.health || (resource.running ? 'Healthy' : 'Stopped');
                var statistics = Object.entries(resource.statistics || {}).map(function(entry) {
                    return window.DashboardUtils.escapeHtml(entry[0]) + '=' + window.DashboardUtils.escapeHtml(entry[1]);
                }).join(', ');
                return '<div class="border rounded p-2 mb-1">' +
                    '<div class="d-flex justify-content-between align-items-center gap-2"><code>' + window.DashboardUtils.escapeHtml(resource.resourceId) + '</code>' + self.healthBadge(status) + '</div>' +
                    '<div class="small text-muted">' + window.DashboardUtils.escapeHtml(resource.resourceType) +
                    (resource.message ? ' · ' + window.DashboardUtils.escapeHtml(resource.message) : '') +
                    (statistics ? ' · ' + statistics : '') + '</div></div>';
            }).join('');
        },

        render: function(data, container) {
            window.DashboardDOM.clear(container);

            var plugins = Array.isArray(data) ? data : (data && data.plugins) || [];
            this.plugins = plugins;
            var enabledCount = plugins.filter(function(p) { return p.enabled; }).length;
            var externalCount = plugins.filter(function(p) { return p.registrationStatus; }).length;

            var installBtn = externalCount >= 0 ?
                '<button class="btn btn-primary btn-sm me-2" onclick="PluginModule.showInstallDialog()">' +
                '<i class="bi bi-box-arrow-in-down me-1"></i>' + __('plugin.install') + '</button>' : '';

            var summaryHtml =
                '<div class="d-flex justify-content-between align-items-center mb-3">' +
                    '<div class="row g-3 flex-grow-1">' +
                        '<div class="col-md-4">' +
                            '<div class="stat-mini-card">' +
                                '<div class="stat-mini-value">' + plugins.length + '</div>' +
                                '<div class="stat-mini-label">' + this._t('plugin.totalShort', 'Total') + '</div>' +
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
                    '</div>' +
                    '<div class="ms-3">' + installBtn + '</div>' +
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
                var localizedName = plugin.displayName || plugin.pluginId;
                var localizedDesc = plugin.description || '-';
                var enabledBadge = plugin.enabled
                    ? '<span class="badge bg-success">' + __('plugin.enabled') + '</span>'
                    : '<span class="badge bg-secondary">' + __('plugin.disabled') + '</span>';
                var health = plugin.healthProbe?.status || plugin.health;
                var healthMessage = plugin.healthProbe?.message;

                // Registration status badge for external plugins
                var regBadge = '';
                if (plugin.registrationStatus) {
                    var regStatus = plugin.registrationStatus;
                    var regBadgeClass = regStatus === 'Active' || regStatus === 'Discovered' ? 'bg-info text-dark'
                        : regStatus === 'InvalidManifest' || regStatus === 'LoadFailed' ? 'bg-danger'
                        : 'bg-warning text-dark';
                    regBadge = '<span class="badge ' + regBadgeClass + '" title="' + window.DashboardUtils.escapeHtml(plugin.registrationError || '') + '">' +
                        '<i class="bi bi-box-seam me-1"></i>' + window.DashboardUtils.escapeHtml(regStatus) + '</span>';
                }

                // Binding targets
                var bindingHtml = '';
                if (plugin.bindingTargets && plugin.bindingTargets.length > 0) {
                    bindingHtml = '<div class="small mt-1">' +
                        '<i class="bi bi-diagram-3 me-1 text-muted"></i>' +
                        '<span class="text-muted">' + this._t('plugin.boundTo', 'Bound to') + ': </span>' +
                        plugin.bindingTargets.map(function(target) {
                            return '<span class="badge bg-light text-dark border me-1">' + window.DashboardUtils.escapeHtml(target) + '</span>';
                        }).join('') +
                        '</div>';
                }

                // Dependencies
                var depsHtml = '';
                if (plugin.dependencies && plugin.dependencies.length > 0) {
                    depsHtml = '<div class="small mt-1">' +
                        '<i class="bi bi-link-45deg me-1 text-muted"></i>' +
                        '<span class="text-muted">' + this._t('plugin.dependencies', 'Dependencies') + ': </span>' +
                        plugin.dependencies.map(function(dep) {
                            return '<span class="badge bg-light text-dark border me-1">' + window.DashboardUtils.escapeHtml(dep) + '</span>';
                        }).join('') +
                        '</div>';
                }

                var resourceHtml = '<div class="row g-3 mt-2 pt-2 border-top">' +
                    '<div class="col-lg-6"><div class="small fw-semibold mb-1">' + __('plugin.declaredResources') + '</div>' + this.renderDeclaredResources(plugin.declaredResources) + '</div>' +
                    '<div class="col-lg-6"><div class="small fw-semibold mb-1">' + __('plugin.runtimeResources') + '</div>' + this.renderRuntimeResources(plugin.runtimeResources) + '</div>' +
                    '</div>';
                var toggleClass = plugin.enabled ? 'btn-outline-danger' : 'btn-outline-success';
                var toggleIcon = plugin.enabled ? 'bi-toggle-on text-success' : 'bi-toggle-off text-secondary';
                var toggleLabel = plugin.enabled ? __('plugin.toggleOff') : __('plugin.toggleOn');

                // External plugin lifecycle buttons
                var lifecycleBtns = '';
                if (plugin.registrationStatus) {
                    lifecycleBtns =
                        '<button class="btn btn-sm btn-outline-warning d-flex align-items-center gap-1" ' +
                        'onclick="PluginModule.showUpgradeDialog(\'' + window.DashboardUtils.escapeHtml(plugin.pluginId) + '\')" title="' + this._t('plugin.upgrade', 'Upgrade') + '" ' + (plugin.enabled ? 'disabled' : '') + '>' +
                        '<i class="bi bi-arrow-up-circle"></i></button>' +
                        '<button class="btn btn-sm btn-outline-danger d-flex align-items-center gap-1" ' +
                        'onclick="PluginModule.uninstallPlugin(\'' + window.DashboardUtils.escapeHtml(plugin.pluginId) + '\')" title="' + this._t('plugin.uninstall', 'Uninstall') + '" ' + (plugin.enabled ? 'disabled' : '') + '>' +
                        '<i class="bi bi-trash"></i></button>';
                }

                return '<div class="card-panel mb-3" style="border-left: 4px solid ' + color + ';">' +
                    '<div class="card-body">' +
                        '<div class="d-flex align-items-start gap-3">' +
                            '<div class="flex-shrink-0" style="width:48px;height:48px;background:' + color + '15;border-radius:12px;display:flex;align-items:center;justify-content:center;">' +
                                '<i class="bi ' + icon + '" style="font-size:24px;color:' + color + ';"></i>' +
                            '</div>' +
                            '<div class="flex-grow-1">' +
                                '<div class="d-flex align-items-center gap-2 mb-1 flex-wrap">' +
                                    '<strong>' + window.DashboardUtils.escapeHtml(localizedName) + '</strong>' +
                                    enabledBadge + this.healthBadge(health) + regBadge +
                                '</div>' +
                                '<div class="text-muted small mb-1"><code>' + window.DashboardUtils.escapeHtml(plugin.pluginId) + '</code>' +
                                    (plugin.bindingCount > 0 ? ' · <i class="bi bi-link me-1"></i>' + plugin.bindingCount : '') +
                                '</div>' +
                                '<div class="text-muted small">' + window.DashboardUtils.escapeHtml(localizedDesc) + '</div>' +
                                (healthMessage ? '<div class="small text-danger mt-1">' + window.DashboardUtils.escapeHtml(healthMessage) + '</div>' : '') +
                                (plugin.registrationError ? '<div class="small text-danger mt-1"><i class="bi bi-exclamation-triangle me-1"></i>' + window.DashboardUtils.escapeHtml(plugin.registrationError) + '</div>' : '') +
                                bindingHtml + depsHtml +
                            '</div>' +
                            '<div class="flex-shrink-0 d-flex flex-column align-items-end gap-2">' +
                                '<span class="badge bg-light text-dark border">' +
                                    '<i class="bi bi-tag me-1"></i>v' + window.DashboardUtils.escapeHtml(plugin.version || '1.0') + '</span>' +
                                '<div class="d-flex gap-1">' +
                                    '<button class="btn btn-sm ' + toggleClass + ' d-flex align-items-center gap-1" onclick="PluginModule.togglePlugin(\'' + window.DashboardUtils.escapeHtml(plugin.pluginId) + '\', ' + !plugin.enabled + ')" title="' + toggleLabel + '">' +
                                        '<i class="bi ' + toggleIcon + '"></i>' +
                                    '</button>' +
                                    lifecycleBtns +
                                '</div>' +
                            '</div>' +
                        '</div>' +
                        resourceHtml +
                    '</div>' +
                '</div>';
            }.bind(this)).join('');

            container.innerHTML = summaryHtml + cards;
        },

        togglePlugin: function(pluginId, enable) {
            var self = this;
            var plugin = this.plugins.find(function(item) { return item.pluginId === pluginId; });
            var localizedName = (plugin && plugin.displayName) || pluginId;
            var action = enable ? __('plugin.toggleOn') : __('plugin.toggleOff');
            var msg = __('plugin.toggleConfirm', { action: action, name: localizedName });
            window.DashboardModals.showConfirm(msg, async function() {
                try {
                    await window.DashboardApi.togglePlugin(pluginId, enable);
                    window.DashboardModals.showSuccess(enable ? __('plugin.enableSuccess') : __('plugin.disableSuccess'));
                    await self.load();
                } catch (error) {
                    console.error('[Plugin] Toggle failed:', error);
                    window.DashboardModals.showError(__('plugin.toggleFailed'));
                }
            }, null, { danger: !enable });
        },

        resetAll: function() {
            var self = this;
            window.DashboardModals.showConfirm(__('plugin.resetConfirm'), async function() {
                try {
                    await window.DashboardApi.resetPlugins();
                    window.DashboardModals.showSuccess(__('plugin.resetSuccess'));
                    await self.load();
                } catch (error) {
                    console.error('[Plugin] Reset failed:', error);
                    window.DashboardModals.showError(__('plugin.resetFailed'));
                }
            }, null, { danger: true });
        },

        showInstallDialog: function() {
            var self = this;
            var html =
                '<div class="mb-3">' +
                    '<label class="form-label">' + this._t('plugin.installPath', 'Plugin source directory path') + '</label>' +
                    '<input type="text" class="form-control" id="install-source-dir" placeholder="/path/to/plugin/directory" />' +
                    '<div class="form-text">' + this._t('plugin.installHelp', 'Directory must contain a plugin.json manifest file.') + '</div>' +
                '</div>';
            window.DashboardModals.showCustom({
                title: '<i class="bi bi-box-arrow-in-down me-2"></i>' + this._t('plugin.install', 'Install Plugin'),
                body: html,
                confirmText: this._t('plugin.install', 'Install'),
                confirmClass: 'btn-primary',
                onConfirm: async function() {
                    var sourceDir = document.getElementById('install-source-dir').value.trim();
                    if (!sourceDir) {
                        window.DashboardModals.showError(self._t('plugin.installPathRequired', 'Source directory path is required'));
                        return false;
                    }
                    try {
                        await window.DashboardApi.installPlugin(sourceDir);
                        window.DashboardModals.showSuccess(self._t('plugin.installSuccess', 'Plugin installed successfully'));
                        await self.load();
                        return true;
                    } catch (error) {
                        console.error('[Plugin] Install failed:', error);
                        var msg = (error && error.message) || self._t('plugin.installFailed', 'Install failed');
                        window.DashboardModals.showError(msg);
                        return false;
                    }
                }
            });
        },

        showUpgradeDialog: function(pluginId) {
            var self = this;
            var plugin = this.plugins.find(function(item) { return item.pluginId === pluginId; });
            var localizedName = (plugin && plugin.displayName) || pluginId;

            var html =
                '<div class="mb-3">' +
                    '<p class="text-muted">' + this._t('plugin.upgradeHelp', 'Specify the new plugin version source directory. The plugin must be disabled first.') + '</p>' +
                    '<label class="form-label">' + this._t('plugin.upgradePath', 'New version source directory path') + '</label>' +
                    '<input type="text" class="form-control" id="upgrade-source-dir" placeholder="/path/to/new/version/directory" />' +
                '</div>';
            window.DashboardModals.showCustom({
                title: '<i class="bi bi-arrow-up-circle me-2"></i>' + this._t('plugin.upgrade', 'Upgrade') + ' · ' + window.DashboardUtils.escapeHtml(localizedName),
                body: html,
                confirmText: this._t('plugin.upgrade', 'Upgrade'),
                confirmClass: 'btn-warning',
                onConfirm: async function() {
                    var sourceDir = document.getElementById('upgrade-source-dir').value.trim();
                    if (!sourceDir) {
                        window.DashboardModals.showError(self._t('plugin.installPathRequired', 'Source directory path is required'));
                        return false;
                    }
                    try {
                        await window.DashboardApi.upgradePlugin(pluginId, sourceDir);
                        window.DashboardModals.showSuccess(self._t('plugin.upgradeSuccess', 'Plugin upgraded successfully'));
                        await self.load();
                        return true;
                    } catch (error) {
                        console.error('[Plugin] Upgrade failed:', error);
                        var msg = (error && error.message) || self._t('plugin.upgradeFailed', 'Upgrade failed');
                        window.DashboardModals.showError(msg);
                        return false;
                    }
                }
            });
        },

        uninstallPlugin: function(pluginId) {
            var self = this;
            var plugin = this.plugins.find(function(item) { return item.pluginId === pluginId; });
            var localizedName = (plugin && plugin.displayName) || pluginId;
            var msg = this._t('plugin.uninstallConfirm', 'Are you sure you want to uninstall plugin "{name}"? This will permanently remove the plugin files.')
                .replace('{name}', localizedName);
            window.DashboardModals.showConfirm(msg, async function() {
                try {
                    await window.DashboardApi.uninstallPlugin(pluginId);
                    window.DashboardModals.showSuccess(self._t('plugin.uninstallSuccess', 'Plugin uninstalled successfully'));
                    await self.load();
                } catch (error) {
                    console.error('[Plugin] Uninstall failed:', error);
                    var errMsg = (error && error.message) || self._t('plugin.uninstallFailed', 'Uninstall failed');
                    window.DashboardModals.showError(errMsg);
                }
            }, null, { danger: true });
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
