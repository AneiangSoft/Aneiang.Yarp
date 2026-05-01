/**
 * Dashboard Routes Module - Aneiang.Yarp Gateway Dashboard
 * Route data loading, rendering, and CRUD operations
 */
(function() {
    'use strict';

    // ===== Load Route Data =====
    window.loadRoutes = async function() {
        try {
            var d = window.__dashboard;
            var res = await window.authFetch(d.basePath + '/routes');
            var json = await res.json();
            if (json.code !== 200) return;
            var routes = json.data;
            var __ = window.__;

            var html = '';
            routes.forEach(function(r) {
                var rid = r.routeId;
                var orderColor = r.order <= 5 ? 'bg-primary' : r.order <= 20 ? 'bg-info' : 'bg-secondary';
                var methods = r.methods ? r.methods.join(', ') : '<span class="text-muted">' + __('index.route.allMethods') + '</span>';

                html += '<tr class="route-row" data-route="' + window.escapeHtml(rid) + '" style="cursor:pointer;">';
                html += '<td><span class="badge ' + orderColor + ' badge-order">' + r.order + '</span></td>';
                html += '<td style="font-weight:500;"><span class="route-expand-icon" style="display:inline-block;width:16px;">\u25B6</span> ' + window.escapeHtml(rid) + '</td>';
                html += '<td><code>' + window.escapeHtml(r.path || '') + '</code></td>';
                html += '<td>';
                if (r.clusterId) {
                    html += '<span class="badge bg-primary" style="font-size:12px;">' + window.escapeHtml(r.clusterId) + '</span>';
                } else {
                    html += '<span class="text-muted">-</span>';
                }
                html += '</td>';
                html += '<td>' + methods + '</td>';
                html += '<td onclick="event.stopPropagation();">';
                if (r.isEditable) {
                    html += '<button class="btn btn-sm btn-outline-primary me-1" onclick="editRoute(\'' + window.escapeHtml(rid) + '\')" title="Edit">';
                    html += '<i class="bi bi-pencil"></i></button>';
                    html += '<button class="btn btn-sm btn-outline-danger" onclick="deleteRoute(\'' + window.escapeHtml(rid) + '\')" title="Delete">';
                    html += '<i class="bi bi-trash"></i></button>';
                } else {
                    html += '<span class="text-muted">-</span>';
                }
                html += '</td>';
                html += '</tr>';

                // Expandable detail row
                html += '<tr class="route-detail" data-route="' + window.escapeHtml(rid) + '" style="display:none;">';
                html += '<td colspan="6" style="padding:0;">';
                html += '<div style="padding:14px 20px;background:#f8fafc;border-bottom:2px solid #e2e8f0;font-size:13px;line-height:1.8;">';

                // RouteId + ClusterId + Order
                html += '<div style="display:flex;flex-wrap:wrap;gap:8px 24px;margin-bottom:6px;">';
                html += '<span><strong>RouteId:</strong> ' + window.escapeHtml(rid) + '</span>';
                html += '<span><strong>ClusterId:</strong> <code>' + window.escapeHtml(r.clusterId || '') + '</code></span>';
                html += '<span><strong>Order:</strong> <span class="badge ' + orderColor + '">' + r.order + '</span></span>';
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
                    html += '<pre style="background:#f1f5f9;border:1px solid #cbd5e1;border-radius:4px;padding:8px;margin:2px 0 0;overflow-x:auto;white-space:pre-wrap;word-break:break-all;font-size:12px;color:#334155;line-height:1.6;">';
                    html += window.escapeHtml(JSON.stringify(r.transforms, null, 2)) + '</pre>';
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
                    window.toggleRouteDetail(this);
                });
            });
        } catch (e) { console.error('Failed to load routes:', e); }
    };

    // ===== Toggle Route Detail =====
    window.toggleRouteDetail = function(rowEl) {
        var rid = rowEl.dataset.route;
        if (!rid) return;
        var detailRow = document.querySelector('.route-detail[data-route="' + rid + '"]');
        var arrow = rowEl.querySelector('.route-expand-icon');
        if (!detailRow || !detailRow.classList.contains('route-detail')) return;
        var isOpen = detailRow.style.display !== 'none';
        detailRow.style.display = isOpen ? 'none' : 'table-row';
        if (arrow) arrow.textContent = isOpen ? '\u25B6' : '\u25BC';
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
