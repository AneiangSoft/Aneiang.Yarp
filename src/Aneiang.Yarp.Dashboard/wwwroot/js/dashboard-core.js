/**
 * Dashboard Core Module - Aneiang.Yarp Gateway Dashboard
 * Core setup, utilities, and initialization
 */
(function() {
    'use strict';

    // ===== Core Variables =====
    // These will be set by the inline init script in Index.cshtml
    window.__dashboard = window.__dashboard || {
        basePath: '/dashboard',
        token: null,
        logPanelEnabled: false,
        tabPanels: {
            overview: [],
            services: ['cluster-panel'],
            routes: ['route-panel']
        },
        allPanels: ['cluster-panel', 'route-panel'],
        CURRENT_LOCALE: 'zh-CN',
        I18N: {}
    };

    // ===== Auth Fetch =====
    window.authFetch = async function(url, options) {
        options = options || {};
        options.headers = options.headers || {};
        var d = window.__dashboard;
        if (d.token) {
            options.headers['Authorization'] = 'Bearer ' + d.token;
            options.headers['X-Requested-With'] = 'XMLHttpRequest';
        }
        var res = await fetch(url, options);
        if (res.status === 401) {
            localStorage.removeItem('dashboard_token');
            window.location.href = d.basePath + '/login';
            throw new Error('Unauthorized');
        }
        return res;
    };

    // ===== Tab Switching =====
    window.switchTab = function(tabName) {
        var d = window.__dashboard;
        // Update sidebar active state
        document.querySelectorAll('.sidebar .nav-link[data-tab]').forEach(function(l) {
            l.classList.toggle('active', l.dataset.tab === tabName);
        });
        // Show/hide panels
        d.allPanels.forEach(function(id) {
            var el = document.getElementById(id);
            if (el) el.style.display = (d.tabPanels[tabName] || []).includes(id) ? '' : 'none';
        });
    };

    // ===== Utility Functions =====
    window.timeStr = function() {
        var locale = window.__dashboard.CURRENT_LOCALE === 'en-US' ? 'en-US' : 'zh-CN';
        return new Date().toLocaleTimeString(locale, { hour12: false });
    };

    window.healthDot = function(status) {
        var s = (status || '').toLowerCase();
        var __ = window.__;
        if (s === 'healthy')   return '<span class="health-dot healthy"></span><span style="color:#22c55e;">' + __('index.health.healthy') + '</span>';
        if (s === 'unhealthy') return '<span class="health-dot unhealthy"></span><span style="color:#ef4444;">' + __('index.health.unhealthy') + '</span>';
        return '<span class="health-dot unknown"></span><span style="color:#f59e0b;">' + __('index.health.unknown') + '</span>';
    };

    window.escapeHtml = function(text) {
        if (!text) return '';
        var div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    };

    // ===== Toast Notification =====
    window.showToast = function(message, type, duration) {
        type = type || 'info'; // success, danger, warning, info
        duration = duration || 3000;
        
        // Create toast container if not exists
        var container = document.getElementById('toast-container');
        if (!container) {
            container = document.createElement('div');
            container.id = 'toast-container';
            container.style.cssText = 'position: fixed; top: 20px; right: 20px; z-index: 9999;';
            document.body.appendChild(container);
        }
        
        // Create toast element
        var toast = document.createElement('div');
        var bgClass = type === 'success' ? 'bg-success' : 
                      type === 'danger' ? 'bg-danger' : 
                      type === 'warning' ? 'bg-warning text-dark' : 'bg-info';
        
        toast.className = 'toast show ' + bgClass;
        toast.style.cssText = 'min-width: 300px; margin-bottom: 10px; color: white; border-radius: 8px; box-shadow: 0 4px 6px rgba(0,0,0,0.1);';
        
        var icon = type === 'success' ? 'check-circle' : 
                   type === 'danger' ? 'x-circle' : 
                   type === 'warning' ? 'exclamation-triangle' : 'info-circle';
        
        toast.innerHTML = '<div class="toast-body d-flex align-items-center">' +
            '<i class="bi bi-' + icon + ' me-2" style="font-size: 1.2em;"></i>' +
            '<span>' + window.escapeHtml(message) + '</span>' +
            '<button type="button" class="btn-close btn-close-white ms-auto" style="font-size: 0.8em;"></button>' +
            '</div>';
        
        container.appendChild(toast);
        
        // Close button
        toast.querySelector('.btn-close').addEventListener('click', function() {
            toast.remove();
        });
        
        // Auto remove
        setTimeout(function() {
            if (toast.parentElement) {
                toast.remove();
            }
        }, duration);
    };

    // ===== Custom Confirm Dialog =====
    window.showConfirm = function(message, onConfirm, onCancel) {
        var __ = window.__;
        var confirmId = 'confirmModal_' + Date.now();
        
        var modalHtml = '<div class="modal fade" id="' + confirmId + '" tabindex="-1">' +
            '<div class="modal-dialog modal-dialog-centered"><div class="modal-content">' +
            '<div class="modal-header"><h5 class="modal-title"><i class="bi bi-exclamation-triangle me-2 text-warning"></i>' + (__('modal.confirm') || 'Confirm') + '</h5>' +
            '<button type="button" class="btn-close" data-bs-dismiss="modal"></button></div>' +
            '<div class="modal-body"><p class="mb-0">' + window.escapeHtml(message) + '</p></div>' +
            '<div class="modal-footer">' +
            '<button type="button" class="btn btn-secondary" data-bs-dismiss="modal">' + (__('modal.cancel') || 'Cancel') + '</button>' +
            '<button type="button" class="btn btn-danger" id="' + confirmId + '_confirm">' + (__('modal.confirm') || 'Confirm') + '</button>' +
            '</div></div></div></div>';
        
        document.body.insertAdjacentHTML('beforeend', modalHtml);
        
        var modalEl = document.getElementById(confirmId);
        var modal = new bootstrap.Modal(modalEl);
        
        // Confirm button
        document.getElementById(confirmId + '_confirm').addEventListener('click', function() {
            modal.hide();
            if (onConfirm) onConfirm();
        });
        
        // Cancel button
        modalEl.addEventListener('hidden.bs.modal', function() {
            modalEl.remove();
            if (onCancel) onCancel();
        });
        
        modal.show();
    };

    // ===== Manual Refresh =====
    window.manualRefresh = async function(btn) {
        if (btn) {
            btn.disabled = true;
            var icon = btn.querySelector('i');
            if (icon) icon.classList.add('spin');
        }
        await window.refreshAll();
        if (btn) {
            btn.disabled = false;
            var icon = btn.querySelector('i');
            if (icon) icon.classList.remove('spin');
        }
    };

    // ===== Full Refresh =====
    window.refreshAll = async function() {
        await Promise.all([window.loadInfo(), window.loadClusters(), window.loadRoutes()]);
    };

    // ===== Export Configuration =====
    window.exportConfig = async function() {
        try {
            var d = window.__dashboard;
            var __ = window.__;
            
            // Fetch dynamic config
            var res = await window.authFetch(d.basePath + '/../api/gateway/dynamic-config');
            var json = await res.json();
            
            if (json.code !== 200 || !json.data) {
                alert('Failed to load configuration');
                return;
            }
            
            // Convert internal format to YARP format
            var internalConfig = json.data;
            var yarpConfig = convertToYarpFormat(internalConfig);
            
            // Create download link
            var jsonStr = JSON.stringify(yarpConfig, null, 2);
            var blob = new Blob([jsonStr], { type: 'application/json' });
            var url = URL.createObjectURL(blob);
            var a = document.createElement('a');
            a.href = url;
            a.download = 'gateway-config-' + new Date().toISOString().slice(0, 10) + '.json';
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            URL.revokeObjectURL(url);
            
            window.showToast('Configuration exported successfully', 'success');
        } catch (e) {
            console.error('Export config failed:', e);
            window.showToast('Error: ' + e.message, 'danger');
        }
    };

    // ===== Helper: Convert Internal Format to YARP Format =====
    function convertToYarpFormat(internalConfig) {
        var yarpConfig = {
            ReverseProxy: {
                Routes: {},
                Clusters: {}
            }
        };
        
        // Convert routes
        if (internalConfig.routes && internalConfig.routes.length > 0) {
            internalConfig.routes.forEach(function(route) {
                var routeConfig = {
                    ClusterId: route.clusterId,
                    Order: route.order || 50,
                    Match: {
                        Path: route.matchPath
                    }
                };
                
                // Add transforms if present
                if (route.transforms && route.transforms.length > 0) {
                    routeConfig.Transforms = route.transforms;
                }
                
                // Add metadata if present
                if (route.metadata) {
                    routeConfig.Metadata = route.metadata;
                }
                
                yarpConfig.ReverseProxy.Routes[route.routeId] = routeConfig;
            });
        }
        
        // Convert clusters
        if (internalConfig.clusters && internalConfig.clusters.length > 0) {
            internalConfig.clusters.forEach(function(cluster) {
                var clusterConfig = {
                    Destinations: {}
                };
                
                // Convert destinations from array to object
                // Check if destinations is an array
                if (Array.isArray(cluster.destinations) && cluster.destinations.length > 0) {
                    cluster.destinations.forEach(function(dest) {
                        clusterConfig.Destinations[dest.name] = {
                            Address: dest.address
                        };
                    });
                }
                // Check if destinations is an object (key-value pairs)
                else if (cluster.destinations && typeof cluster.destinations === 'object') {
                    Object.keys(cluster.destinations).forEach(function(key) {
                        clusterConfig.Destinations[key] = {
                            Address: cluster.destinations[key]
                        };
                    });
                }
                
                // Add load balancing policy
                if (cluster.loadBalancingPolicy) {
                    clusterConfig.LoadBalancingPolicy = cluster.loadBalancingPolicy;
                }
                
                // Add health check if present
                if (cluster.healthCheck) {
                    clusterConfig.HealthCheck = cluster.healthCheck;
                }
                
                // Add metadata if present
                if (cluster.metadata) {
                    clusterConfig.Metadata = cluster.metadata;
                }
                
                yarpConfig.ReverseProxy.Clusters[cluster.clusterId] = clusterConfig;
            });
        }
        
        return yarpConfig;
    };

    // ===== Import Configuration =====
    window.importConfig = async function() {
        try {
            var __ = window.__;
            
            // Create file input
            var input = document.createElement('input');
            input.type = 'file';
            input.accept = '.json';
            
            input.onchange = async function(e) {
                var file = e.target.files[0];
                if (!file) return;
                
                try {
                    var text = await file.text();
                    var config = JSON.parse(text);
                    
                    // Detect format and convert to internal format
                    var internalConfig;
                    if (config.ReverseProxy) {
                        // YARP format
                        internalConfig = convertFromYarpFormat(config);
                    } else if (config.routes && config.clusters) {
                        // Internal format
                        internalConfig = config;
                    } else {
                        window.showToast('Invalid configuration file: missing routes or clusters', 'danger');
                        return;
                    }
                    
                    // Confirm import
                    var routeCount = internalConfig.routes ? internalConfig.routes.length : 0;
                    var clusterCount = internalConfig.clusters ? internalConfig.clusters.length : 0;
                    var __ = window.__;
                    
                    window.showConfirm(
                        (__('modal.importConfirm') || 'This will import {routes} routes and {clusters} clusters. Continue?')
                            .replace('{routes}', routeCount)
                            .replace('{clusters}', clusterCount),
                        async function() {
                            try {
                                var d = window.__dashboard;
                                var imported = { routes: 0, clusters: 0, errors: 0 };
                                
                                // Import clusters first
                                if (internalConfig.clusters) {
                                    for (var i = 0; i < internalConfig.clusters.length; i++) {
                                        var cluster = internalConfig.clusters[i];
                                        try {
                                            // Convert destinations to object format if it's an array
                                            var destinationsObj = {};
                                            if (Array.isArray(cluster.destinations)) {
                                                cluster.destinations.forEach(function(dest) {
                                                    destinationsObj[dest.name] = dest.address;
                                                });
                                            } else if (cluster.destinations && typeof cluster.destinations === 'object') {
                                                destinationsObj = cluster.destinations;
                                            }
                                            
                                            var res = await window.authFetch(d.basePath + '/../api/gateway/clusters', {
                                                method: 'POST',
                                                headers: { 'Content-Type': 'application/json' },
                                                body: JSON.stringify({
                                                    clusterId: cluster.clusterId,
                                                    destinations: destinationsObj,
                                                    loadBalancingPolicy: cluster.loadBalancingPolicy || 'RoundRobin'
                                                })
                                            });
                                            var json = await res.json();
                                            if (json.code === 200) {
                                                imported.clusters++;
                                            } else {
                                                imported.errors++;
                                                console.warn('Failed to import cluster:', cluster.clusterId, json.message);
                                            }
                                        } catch (err) {
                                            imported.errors++;
                                            console.error('Error importing cluster:', cluster.clusterId, err);
                                        }
                                    }
                                }
                                
                                // Import routes
                                if (internalConfig.routes) {
                                    for (var i = 0; i < internalConfig.routes.length; i++) {
                                        var route = internalConfig.routes[i];
                                        try {
                                            var res = await window.authFetch(d.basePath + '/../api/gateway/register-route', {
                                                method: 'POST',
                                                headers: { 'Content-Type': 'application/json' },
                                                body: JSON.stringify({
                                                    routeName: route.routeId,
                                                    clusterName: route.clusterId,
                                                    matchPath: route.matchPath,
                                                    destinationAddress: 'http://localhost:5001',
                                                    order: route.order || 50,
                                                    transforms: route.transforms || []
                                                })
                                            });
                                            var json = await res.json();
                                            if (json.code === 200) {
                                                imported.routes++;
                                            } else {
                                                imported.errors++;
                                                console.warn('Failed to import route:', route.routeId, json.message);
                                            }
                                        } catch (err) {
                                            imported.errors++;
                                            console.error('Error importing route:', route.routeId, err);
                                        }
                                    }
                                }
                                
                                // Show results
                                var message = (__('toast.importCompleted') || 'Import completed!') + '\n' +
                                    (__('toast.importedRoutes') || 'Routes: ') + imported.routes + ' ' + (__('toast.imported') || 'imported') + '\n' +
                                    (__('toast.importedClusters') || 'Clusters: ') + imported.clusters + ' ' + (__('toast.imported') || 'imported') + '\n';
                                if (imported.errors > 0) {
                                    message += (__('toast.importErrors') || 'Errors: ') + imported.errors + ' ' + (__('toast.checkConsole') || '(check console for details)');
                                }
                                window.showToast(message, imported.errors > 0 ? 'warning' : 'success', 5000);
                                
                                // Refresh dashboard
                                await window.refreshAll();
                            } catch (e) {
                                console.error('Import failed:', e);
                                window.showToast(('Error: ' + e.message), 'danger');
                            }
                        }
                    );
                } catch (e) {
                    console.error('Parse config file failed:', e);
                    window.showToast('Invalid JSON file: ' + e.message, 'danger');
                }
            };
            
            input.click();
        } catch (e) {
            console.error('Import config failed:', e);
            window.showToast('Error: ' + e.message, 'danger');
        }
    };

    // ===== Helper: Convert YARP Format to Internal Format =====
    function convertFromYarpFormat(yarpConfig) {
        var internalConfig = {
            routes: [],
            clusters: []
        };
        
        // Convert routes
        if (yarpConfig.ReverseProxy && yarpConfig.ReverseProxy.Routes) {
            var routes = yarpConfig.ReverseProxy.Routes;
            Object.keys(routes).forEach(function(routeId) {
                var route = routes[routeId];
                internalConfig.routes.push({
                    routeId: routeId,
                    clusterId: route.ClusterId,
                    matchPath: route.Match ? route.Match.Path : '',
                    order: route.Order || 50,
                    transforms: route.Transforms || [],
                    metadata: route.Metadata || null
                });
            });
        }
        
        // Convert clusters
        if (yarpConfig.ReverseProxy && yarpConfig.ReverseProxy.Clusters) {
            var clusters = yarpConfig.ReverseProxy.Clusters;
            Object.keys(clusters).forEach(function(clusterId) {
                var cluster = clusters[clusterId];
                
                // Convert destinations from object to array
                var destinations = [];
                if (cluster.Destinations) {
                    Object.keys(cluster.Destinations).forEach(function(destName) {
                        var dest = cluster.Destinations[destName];
                        destinations.push({
                            name: destName,
                            address: dest.Address
                        });
                    });
                }
                
                internalConfig.clusters.push({
                    clusterId: clusterId,
                    destinations: destinations,
                    loadBalancingPolicy: cluster.LoadBalancingPolicy || 'RoundRobin',
                    healthCheck: cluster.HealthCheck || null,
                    metadata: cluster.Metadata || null
                });
            });
        }
        
        return internalConfig;
    };

    // ===== Initialize Tab Click Handlers =====
    document.addEventListener('DOMContentLoaded', function() {
        document.querySelectorAll('.sidebar .nav-link[data-tab]').forEach(function(link) {
            link.addEventListener('click', function(e) {
                e.preventDefault();
                window.switchTab(this.dataset.tab);
            });
        });
    });
})();
