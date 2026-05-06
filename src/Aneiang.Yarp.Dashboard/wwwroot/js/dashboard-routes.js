/**
 * Dashboard Routes Module - Aneiang.Yarp Gateway Dashboard
 * Route data loading, rendering, and CRUD operations
 * Refactored to use API layer, state management, and renderers
 */
(function() {
    'use strict';

    // ===== Load Route Data =====
    window.loadRoutes = async function() {
        try {
            var state = window.dashboardState;
            state.routes.loading = true;
            
            var result = await window.dashboardApiMethods.getRoutes();
            if (result.code !== 200) return;
            
            state.routes.data = result.data;
            window.renderRoutes();
        } catch (e) { 
            console.error('Failed to load routes:', e);
            window.dashboardRenderers.ui.toast('Failed to load routes', 'error');
        } finally {
            window.dashboardState.routes.loading = false;
        }
    };

    // ===== Render Routes =====
    window.renderRoutes = function() {
        var state = window.dashboardState;
        var __ = window.__;
        var renderers = window.dashboardRenderers;
        
        // Get filtered routes
        var routes = state.getFilteredRoutes();
        
        // Render filter toolbar
        var filterContainer = document.getElementById('route-filter-container');
        if (filterContainer && !filterContainer.innerHTML.trim()) {
            filterContainer.innerHTML = window.dashboardFilters.renderRouteToolbar();
            window.dashboardFilters.initRouteToolbar();
        }

        var html = '';
        routes.forEach(function(r) {
            var rid = r.routeId;
            var isExpanded = state.isRouteExpanded(rid);
            var methods = r.methods ? r.methods.join(', ') : '<span class="text-muted">' + __('index.route.allMethods') + '</span>';

            html += '<tr class="route-row" data-route="' + window.escapeHtml(rid) + '" style="cursor:pointer;">';
            html += '<td>' + renderers.badge.routeOrder(r.order) + '</td>';
            var expandIcon = isExpanded ? '\u25BC' : '\u25B6';
            html += '<td style="font-weight:500;">';
            html += '<span class="route-expand-icon" style="display:inline-block;width:16px;">' + expandIcon + '</span> ';
            html += window.escapeHtml(rid);
            html += '</td>';
            html += '<td><code>' + window.escapeHtml(r.path || '') + '</code></td>';
            html += '<td>';
            if (r.clusterId) {
                html += '<span class="badge bg-primary" style="font-size:12px;">' + window.escapeHtml(r.clusterId) + '</span>';
            } else {
                html += '<span class="text-muted">-</span>';
            }
            html += '</td>';
            html += '<td>' + methods + '</td>';
            html += '<td onclick="event.stopPropagation;">';
            html += renderers.ui.actionButtons(
                rid,
                r.isEditable,
                'editRoute(\'' + window.escapeHtml(rid) + '\')',
                'deleteRoute(\'' + window.escapeHtml(rid) + '\')'
            );
            html += '</td>';
            html += '</tr>';

            // Expandable detail row
            html += '<tr class="route-detail" data-route="' + window.escapeHtml(rid) + '" style="display:' + (isExpanded ? '' : 'none') + ';">';
            html += '<td colspan="6" style="padding:0;">';
            html += '<div style="padding:14px 20px;background:#f8fafc;border-bottom:2px solid #e2e8f0;font-size:13px;line-height:1.8;">';

            // RouteId + ClusterId + Order + Source
            html += '<div style="display:flex;flex-wrap:wrap;gap:8px 24px;margin-bottom:6px;">';
            html += '<span><strong>RouteId:</strong> ' + window.escapeHtml(rid) + '</span>';
            html += '<span><strong>ClusterId:</strong> <code>' + window.escapeHtml(r.clusterId || '') + '</code></span>';
            html += '<span><strong>Order:</strong> ' + renderers.badge.routeOrder(r.order) + '</span>';
            html += '<span><strong>Source:</strong> ' + renderers.badge.source(r.source) + '</span>';
            html += '</div>';

            // Match
            html += '<div style="margin-top:6px;margin-bottom:4px;"><strong>' + __('index.detail.match') + '</strong></div>';
            html += '<table style="margin:0 0 6px 0;border-collapse:collapse;font-size:12px;width:auto;">';
            html += '<tr style="background:#e2e8f0;"><th style="padding:3px 10px;border:1px solid #cbd5e1;text-align:left;">' + __('index.detail.property') + '</th><th style="padding:3px 10px;border:1px solid #cbd5e1;text-align:left;">' + __('index.detail.value') + '</th></tr>';
            html += '<tr><td style="padding:2px 10px;border:1px solid #e2e8f0;">Path</td><td style="padding:2px 10px;border:1px solid #e2e8f0;"><code>' + window.escapeHtml(r.path || '') + '</code></td></tr>';
            if (r.hosts && r.hosts.length > 0) {
                html += '<tr><td style="padding:2px 10px;border:1px solid #e2e8f0;">Hosts</td><td style="padding:2px 10px;border:1px solid #e2e8f0;">' + r.hosts.join(', ') + '</td></tr>';
            }
            if (r.methods && r.methods.length > 0) {
                html += '<tr><td style="padding:2px 10px;border:1px solid #e2e8f0;">Methods</td><td style="padding:2px 10px;border:1px solid #e2e8f0;">' + r.methods.join(', ') + '</td></tr>';
            }
            html += '</table>';

            // Transforms
            if (r.transforms && r.transforms.length > 0) {
                html += '<div style="margin-top:6px;margin-bottom:4px;"><strong>' + __('index.detail.transforms') + '</strong></div>';
                html += renderers.json.collapsible(r.transforms, 'Transforms JSON', true);
            }

            // Route Metadata
            if (r.metadata) {
                html += '<div style="margin-top:6px;"><strong>' + __('index.detail.routeMetadata') + '</strong></div>';
                var rmetaKeys = Object.keys(r.metadata);
                if (rmetaKeys.length > 0) {
                    html += '<table style="margin:2px 0 0 0;border-collapse:collapse;font-size:12px;width:auto;">';
                    html += '<tr style="background:#e2e8f0;"><th style="padding:3px 10px;border:1px solid #cbd5e1;text-align:left;">' + __('index.detail.key') + '</th><th style="padding:3px 10px;border:1px solid #cbd5e1;text-align:left;">' + __('index.detail.value') + '</th></tr>';
                    for (var rmi = 0; rmi < rmetaKeys.length; rmi++) {
                        var rmk = rmetaKeys[rmi];
                        html += '<tr><td style="padding:2px 10px;border:1px solid #e2e8f0;"><code>' + window.escapeHtml(rmk) + '</code></td><td style="padding:2px 10px;border:1px solid #e2e8f0;">' + window.escapeHtml(r.metadata[rmk]) + '</td></tr>';
                    }
                    html += '</table>';
                }
            }

            html += '</div>';
            html += '</td>';
            html += '</tr>';
        });

        document.getElementById('route-tbody').innerHTML =
            html || '<tr><td colspan="6" class="text-center text-muted py-4">' + __('index.route.empty') + '</td></tr>';
        document.getElementById('route-refresh-time').textContent = __('index.route.updated') + window.timeStr();
        document.getElementById('stat-routes').textContent = routes.length;

        // Attach click handlers for route expand/collapse
        document.querySelectorAll('.route-row').forEach(function(row) {
            row.addEventListener('click', function() {
                var rid = this.dataset.route;
                window.dashboardState.toggleRoute(rid);
                window.renderRoutes();
            });
        });
    };

    // ===== Edit Route =====
    window.editRoute = async function(routeId) {
        try {
            var d = window.__dashboard;
            var __ = window.__;
            var res = await window.authFetch(d.basePath + '/../api/gateway/dynamic-config');
            var json = await res.json();
            if (json.code !== 200 || !json.data) {
                alert('Failed to load dynamic config');
                return;
            }
            var config = json.data;
            var route = config.routes.find(function(r) { return r.routeId === routeId; });
            if (!route) {
                alert('Route not found in dynamic config');
                return;
            }
            var routeJson = {
                clusterId: route.clusterId,
                matchPath: route.matchPath,
                order: route.order || 50,
                transforms: route.transforms || []
            };
            var title = __('modal.editRoute') + ': ' + routeId;
            window.showJsonEditor(title, routeJson, async function(newJson) {
                var updateRes = await window.authFetch(d.basePath + '/../api/gateway/routes/' + encodeURIComponent(routeId), {
                    method: 'PUT',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(newJson)
                });
                var updateJson = await updateRes.json();
                if (updateJson.code === 200) {
                    alert('Route updated successfully');
                    await window.loadRoutes();
                } else {
                    alert('Failed to update route: ' + updateJson.message);
                }
            });
        } catch (e) {
            console.error('Edit route failed:', e);
            alert('Error: ' + e.message);
        }
    };

    // ===== Delete Route =====
    window.deleteRoute = async function(routeId) {
        if (!confirm('Are you sure you want to delete route: ' + routeId + '?')) return;
        try {
            var d = window.__dashboard;
            var res = await window.authFetch(d.basePath + '/../api/gateway/' + encodeURIComponent(routeId), {
                method: 'DELETE'
            });
            var json = await res.json();
            if (json.code === 200) {
                alert('Route deleted successfully');
                await window.loadRoutes();
                await window.loadClusters();
            } else {
                alert('Failed to delete route: ' + json.message);
            }
        } catch (e) {
            console.error('Delete route failed:', e);
            alert('Error: ' + e.message);
        }
    };

    // ===== Show Add Route Modal =====
    window.showAddRouteModal = function() {
        var defaultRoute = {
            routeId: 'new-route-' + Date.now(),
            clusterId: '',
            matchPath: '/api/new-route/{**catch-all}',
            order: 50,
            destinationAddress: 'http://localhost:5001',
            transforms: []
        };
        var __ = window.__;
        window.showQuickAddModal(__('modal.addNewRoute'), defaultRoute, async function(newData) {
            if (!newData.clusterId || !newData.matchPath || !newData.destinationAddress) {
                alert(__('modal.routeRequired'));
                return false;
            }
            var d = window.__dashboard;
            var request = {
                routeName: newData.routeId,
                clusterName: newData.clusterId,
                matchPath: newData.matchPath,
                destinationAddress: newData.destinationAddress,
                order: newData.order || 50,
                transforms: newData.transforms || []
            };
            var res = await window.authFetch(d.basePath + '/../api/gateway/register-route', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(request)
            });
            var json = await res.json();
            if (json.code === 200) {
                alert('Route added successfully');
                await window.loadRoutes();
                await window.loadClusters();
                return true;
            } else {
                alert('Failed to add route: ' + json.message);
                return false;
            }
        });
    };
})();
