/**
 * Dashboard Clusters Module - Aneiang.Yarp Gateway Dashboard
 * Cluster data loading, rendering, and CRUD operations
 */
(function() {
    'use strict';

    // ===== Load Gateway Info =====
    window.loadInfo = async function() {
        try {
            var d = window.__dashboard;
            var res = await window.authFetch(d.basePath + '/info');
            var json = await res.json();
            if (json.code !== 200) return;
            var info = json.data;
            document.getElementById('info-version').textContent = info.version;
            document.getElementById('info-env').textContent = info.environment;
            document.getElementById('info-start').textContent = info.startTime;
            document.getElementById('info-uptime').textContent = info.uptime;
            document.getElementById('info-memory').textContent = info.memoryMb + ' MB';
            document.getElementById('info-machine').textContent = info.machineName;
        } catch (e) { console.error('Failed to load gateway info:', e); }
    };

    // ===== Load Cluster Data =====
    window.loadClusters = async function() {
        try {
            var d = window.__dashboard;
            var res = await window.authFetch(d.basePath + '/clusters');
            var json = await res.json();
            if (json.code !== 200) return;
            var clusters = json.data;
            var __ = window.__;

            var totalHealthy = 0, totalUnknown = 0, totalUnhealthy = 0;
            var html = '';

            clusters.forEach(function(c) {
                totalHealthy += c.healthyCount || 0;
                totalUnknown += c.unknownCount || 0;
                totalUnhealthy += c.unhealthyCount || 0;

                var rowspan = c.destinations.length || 1;
                c.destinations.forEach(function(dest, i) {
                    html += '<tr class="cluster-row" data-cluster="' + window.escapeHtml(c.clusterId) + '" style="cursor:pointer;">';
                    if (i === 0) {
                        html += '<td rowspan="' + rowspan + '" style="font-weight:600;vertical-align:middle;"><span class="cluster-expand-icon" style="display:inline-block;width:16px;">\u25B6</span> ' + c.clusterId + '</td>';
                    }
                    html += '<td><code>' + dest.name + '</code></td>';
                    html += '<td><a href="' + dest.address + '" target="_blank" class="text-decoration-none">' + dest.address + '</a></td>';
                    if (dest.health) html += '<td><span style="color:#64748b;">' + window.escapeHtml(dest.health) + '</span></td>';
                    else html += '<td>' + window.healthDot(dest.activeHealth || 'Unknown') + '</td>';
                    html += '<td>' + window.healthDot(dest.passiveHealth || 'Unknown') + '</td>';
                    if (i === 0) {
                        html += '<td rowspan="' + rowspan + '" style="vertical-align:middle;"><span class="badge bg-secondary">' + c.loadBalancingPolicy + '</span></td>';
                        html += '<td rowspan="' + rowspan + '" style="vertical-align:middle;" onclick="event.stopPropagation();">';
                        if (c.isEditable) {
                            html += '<button class="btn btn-sm btn-outline-primary me-1" onclick="editCluster(\'' + window.escapeHtml(c.clusterId) + '\')" title="Edit">';
                            html += '<i class="bi bi-pencil"></i></button>';
                            html += '<button class="btn btn-sm btn-outline-danger" onclick="deleteCluster(\'' + window.escapeHtml(c.clusterId) + '\')" title="Delete">';
                            html += '<i class="bi bi-trash"></i></button>';
                        } else {
                            html += '<span class="text-muted">-</span>';
                        }
                        html += '</td>';
                    }
                    html += '</tr>';
                });

                // Expandable detail row
                html += '<tr class="cluster-detail" data-cluster="' + window.escapeHtml(c.clusterId) + '" style="display:none;">';
                html += '<td colspan="7" style="padding:0;">';
                html += '<div style="padding:14px 20px;background:#f8fafc;border-bottom:2px solid #e2e8f0;font-size:13px;line-height:1.8;">';

                // ClusterId + LoadBalancingPolicy
                html += '<div style="display:flex;flex-wrap:wrap;gap:8px 24px;margin-bottom:6px;">';
                html += '<span><strong>ClusterId:</strong> ' + window.escapeHtml(c.clusterId) + '</span>';
                html += '<span><strong>LoadBalancingPolicy:</strong> <span class="badge bg-secondary">' + window.escapeHtml(c.loadBalancingPolicy) + '</span></span>';
                html += '</div>';

                // Destinations detail
                if (c.destinations && c.destinations.length > 0) {
                    html += '<div style="margin-top:6px;margin-bottom:4px;"><strong>' + __('index.detail.destinations') + '</strong></div>';
                    html += '<table style="margin:0 0 6px 0;border-collapse:collapse;font-size:12px;width:auto;">';
                    html += '<tr style="background:#e2e8f0;"><th style="padding:3px 10px;border:1px solid #cbd5e1;text-align:left;">' + __('index.detail.node') + '</th><th style="padding:3px 10px;border:1px solid #cbd5e1;text-align:left;">' + __('index.detail.address') + '</th></tr>';
                    c.destinations.forEach(function(dest) {
                        html += '<tr><td style="padding:2px 10px;border:1px solid #e2e8f0;"><code>' + window.escapeHtml(dest.name) + '</code></td>';
                        html += '<td style="padding:2px 10px;border:1px solid #e2e8f0;">' + window.escapeHtml(dest.address) + '</td></tr>';
                    });
                    html += '</table>';
                }

                // Session affinity
                if (c.sessionAffinity) {
                    var sa = c.sessionAffinity;
                    html += '<div style="margin-top:6px;margin-bottom:4px;"><strong>' + __('index.detail.sessionAffinity') + '</strong></div>';
                    html += '<table style="margin:0 0 6px 0;border-collapse:collapse;font-size:12px;width:auto;">';
                    html += '<tr style="background:#e2e8f0;"><th style="padding:3px 10px;border:1px solid #cbd5e1;text-align:left;">' + __('index.detail.property') + '</th><th style="padding:3px 10px;border:1px solid #cbd5e1;text-align:left;">' + __('index.detail.value') + '</th></tr>';
                    if (sa.enabled != null) html += '<tr><td style="padding:2px 10px;border:1px solid #e2e8f0;">Enabled</td><td style="padding:2px 10px;border:1px solid #e2e8f0;">' + (sa.enabled ? 'true' : 'false') + '</td></tr>';
                    if (sa.policy) html += '<tr><td style="padding:2px 10px;border:1px solid #e2e8f0;">Policy</td><td style="padding:2px 10px;border:1px solid #e2e8f0;"><code>' + window.escapeHtml(sa.policy) + '</code></td></tr>';
                    if (sa.affinityKeyName) html += '<tr><td style="padding:2px 10px;border:1px solid #e2e8f0;">AffinityKeyName</td><td style="padding:2px 10px;border:1px solid #e2e8f0;"><code>' + window.escapeHtml(sa.affinityKeyName) + '</code></td></tr>';
                    html += '</table>';
                }

                // Health check
                if (c.healthCheck) {
                    var hc = c.healthCheck;
                    html += '<div style="margin-top:6px;margin-bottom:4px;"><strong>' + __('index.detail.healthCheck') + '</strong></div>';
                    html += '<table style="margin:0 0 6px 0;border-collapse:collapse;font-size:12px;width:auto;">';
                    html += '<tr style="background:#e2e8f0;"><th style="padding:3px 10px;border:1px solid #cbd5e1;text-align:left;">' + __('index.detail.property') + '</th><th style="padding:3px 10px;border:1px solid #cbd5e1;text-align:left;">' + __('index.detail.value') + '</th></tr>';
                    if (hc.activeEnabled != null || (hc.active && hc.active.enabled != null)) {
                        var activeEnabled = hc.activeEnabled != null ? hc.activeEnabled : (hc.active ? hc.active.enabled : null);
                        html += '<tr><td style="padding:2px 10px;border:1px solid #e2e8f0;">Active.Enabled</td><td style="padding:2px 10px;border:1px solid #e2e8f0;">' + (activeEnabled ? 'true' : 'false') + '</td></tr>';
                    }
                    if (hc.activeInterval || (hc.active && hc.active.interval)) {
                        html += '<tr><td style="padding:2px 10px;border:1px solid #e2e8f0;">Active.Interval</td><td style="padding:2px 10px;border:1px solid #e2e8f0;">' + (hc.activeInterval || (hc.active ? hc.active.interval : '')) + '</td></tr>';
                    }
                    if (hc.passiveEnabled != null || (hc.passive && hc.passive.enabled != null)) {
                        var passiveEnabled = hc.passiveEnabled != null ? hc.passiveEnabled : (hc.passive ? hc.passive.enabled : null);
                        html += '<tr><td style="padding:2px 10px;border:1px solid #e2e8f0;">Passive.Enabled</td><td style="padding:2px 10px;border:1px solid #e2e8f0;">' + (passiveEnabled ? 'true' : 'false') + '</td></tr>';
                    }
                    html += '</table>';
                }

                // HttpClient
                if (c.httpClient) {
                    var hcl = c.httpClient;
                    html += '<div style="margin-top:6px;margin-bottom:4px;"><strong>' + __('index.detail.httpClient') + '</strong></div>';
                    html += '<table style="margin:0 0 6px 0;border-collapse:collapse;font-size:12px;width:auto;">';
                    html += '<tr style="background:#e2e8f0;"><th style="padding:3px 10px;border:1px solid #cbd5e1;text-align:left;">' + __('index.detail.property') + '</th><th style="padding:3px 10px;border:1px solid #cbd5e1;text-align:left;">' + __('index.detail.value') + '</th></tr>';
                    if (hcl.sslProtocols) html += '<tr><td style="padding:2px 10px;border:1px solid #e2e8f0;">SslProtocols</td><td style="padding:2px 10px;border:1px solid #e2e8f0;">' + (Array.isArray(hcl.sslProtocols) ? hcl.sslProtocols.join(', ') : window.escapeHtml(hcl.sslProtocols)) + '</td></tr>';
                    if (hcl.dangerousAcceptAnyServerCertificate != null) html += '<tr><td style="padding:2px 10px;border:1px solid #e2e8f0;">DangerousAcceptAnyServerCertificate</td><td style="padding:2px 10px;border:1px solid #e2e8f0;">' + (hcl.dangerousAcceptAnyServerCertificate ? 'true' : 'false') + '</td></tr>';
                    if (hcl.maxConnectionsPerServer != null) html += '<tr><td style="padding:2px 10px;border:1px solid #e2e8f0;">MaxConnectionsPerServer</td><td style="padding:2px 10px;border:1px solid #e2e8f0;">' + hcl.maxConnectionsPerServer + '</td></tr>';
                    html += '</table>';
                }

                // HttpRequest
                if (c.httpRequest) {
                    var hr = c.httpRequest;
                    html += '<div style="margin-top:6px;margin-bottom:4px;"><strong>' + __('index.detail.httpRequest') + '</strong></div>';
                    html += '<table style="margin:0 0 6px 0;border-collapse:collapse;font-size:12px;width:auto;">';
                    html += '<tr style="background:#e2e8f0;"><th style="padding:3px 10px;border:1px solid #cbd5e1;text-align:left;">' + __('index.detail.property') + '</th><th style="padding:3px 10px;border:1px solid #cbd5e1;text-align:left;">' + __('index.detail.value') + '</th></tr>';
                    if (hr.activityTimeout) html += '<tr><td style="padding:2px 10px;border:1px solid #e2e8f0;">ActivityTimeout</td><td style="padding:2px 10px;border:1px solid #e2e8f0;">' + hr.activityTimeout + '</td></tr>';
                    if (hr.version) html += '<tr><td style="padding:2px 10px;border:1px solid #e2e8f0;">Version</td><td style="padding:2px 10px;border:1px solid #e2e8f0;">' + window.escapeHtml(hr.version) + '</td></tr>';
                    if (hr.versionPolicy) html += '<tr><td style="padding:2px 10px;border:1px solid #e2e8f0;">VersionPolicy</td><td style="padding:2px 10px;border:1px solid #e2e8f0;">' + window.escapeHtml(hr.versionPolicy) + '</td></tr>';
                    if (hr.allowResponseBuffering != null) html += '<tr><td style="padding:2px 10px;border:1px solid #e2e8f0;">AllowResponseBuffering</td><td style="padding:2px 10px;border:1px solid #e2e8f0;">' + (hr.allowResponseBuffering ? 'true' : 'false') + '</td></tr>';
                    html += '</table>';
                }

                // Cluster Metadata
                if (c.metadata) {
                    html += '<div style="margin-top:6px;"><strong>' + __('index.detail.clusterMetadata') + '</strong></div>';
                    var cmetaKeys = Object.keys(c.metadata);
                    if (cmetaKeys.length > 0) {
                        html += '<table style="margin:2px 0 0 0;border-collapse:collapse;font-size:12px;width:auto;">';
                        html += '<tr style="background:#e2e8f0;"><th style="padding:3px 10px;border:1px solid #cbd5e1;text-align:left;">' + __('index.detail.key') + '</th><th style="padding:3px 10px;border:1px solid #cbd5e1;text-align:left;">' + __('index.detail.value') + '</th></tr>';
                        for (var cmi = 0; cmi < cmetaKeys.length; cmi++) {
                            var cmk = cmetaKeys[cmi];
                            html += '<tr><td style="padding:2px 10px;border:1px solid #e2e8f0;"><code>' + window.escapeHtml(cmk) + '</code></td><td style="padding:2px 10px;border:1px solid #e2e8f0;">' + window.escapeHtml(c.metadata[cmk]) + '</td></tr>';
                        }
                        html += '</table>';
                    }
                }

                html += '</div>';
                html += '</td>';
                html += '</tr>';
            });

            document.getElementById('cluster-tbody').innerHTML =
                html || '<tr><td colspan="7" class="text-center text-muted py-4">' + __('index.cluster.empty') + '</td></tr>';
            document.getElementById('cluster-refresh-time').textContent = __('index.cluster.updated') + window.timeStr();

            // Store clusters for filtering
            window._allClusters = clusters;

            // Update stat cards
            document.getElementById('stat-clusters').textContent = clusters.length;
            var healthLabel = totalUnhealthy > 0
                ? '<span style="color:#22c55e;">' + totalHealthy + '</span> / <span style="color:#f59e0b;">' + totalUnknown + '</span> / <span style="color:#ef4444;">' + totalUnhealthy + '</span>'
                : '<span style="color:#22c55e;">' + totalHealthy + '</span> / <span style="color:#f59e0b;">' + totalUnknown + '</span> / 0';
            document.getElementById('stat-healthy').innerHTML = healthLabel;

            // Attach click handlers for cluster expand/collapse
            document.querySelectorAll('.cluster-row').forEach(function(row) {
                row.addEventListener('click', function() {
                    var cid = this.dataset.cluster;
                    var detailRow = document.querySelector('.cluster-detail[data-cluster="' + cid + '"]');
                    if (detailRow) {
                        var isHidden = detailRow.style.display === 'none';
                        detailRow.style.display = isHidden ? '' : 'none';
                        var icon = this.querySelector('.cluster-expand-icon');
                        if (icon) icon.textContent = isHidden ? '\u25BC' : '\u25B6';
                    }
                });
            });
        } catch (e) { console.error('Failed to load clusters:', e); }
    };

    // ===== Edit Cluster =====
    window.editCluster = async function(clusterId) {
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
            var cluster = config.clusters.find(function(c) { return c.clusterId === clusterId; });
            if (!cluster) {
                alert('Cluster not found in dynamic config');
                return;
            }
            
            // Prepare cluster data for editing
            var clusterJson = {
                destinations: cluster.destinations || {},
                loadBalancingPolicy: cluster.loadBalancingPolicy || 'RoundRobin'
            };
            
            var title = __('modal.editCluster') + ': ' + clusterId;
            window.showJsonEditor(title, clusterJson, async function(newJson) {
                try {
                    var updateRes = await window.authFetch(d.basePath + '/../api/gateway/clusters/' + encodeURIComponent(clusterId), {
                        method: 'PUT',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify(newJson)
                    });
                    var updateJson = await updateRes.json();
                    if (updateJson.code === 200) {
                        window.showToast('Cluster updated successfully', 'success');
                        await window.loadClusters();
                    } else {
                        window.showToast('Failed to update cluster: ' + updateJson.message, 'danger');
                    }
                } catch (e) {
                    console.error('Update cluster failed:', e);
                    alert('Error: ' + e.message);
                }
            });
        } catch (e) {
            console.error('Edit cluster failed:', e);
            alert('Error: ' + e.message);
        }
    };

    // ===== Delete Cluster =====
    window.deleteCluster = async function(clusterId) {
        var __ = window.__;
        window.showConfirm(
            (__('modal.deleteClusterConfirm') || 'Are you sure you want to delete cluster: {clusterId}?\n\nWarning: All routes referencing this cluster will be affected.')
                .replace('{clusterId}', clusterId),
            async function() {
                try {
                    var d = window.__dashboard;
                    var res = await window.authFetch(d.basePath + '/../api/gateway/clusters/' + encodeURIComponent(clusterId), {
                        method: 'DELETE'
                    });
                    var json = await res.json();
                    if (json.code === 200) {
                        window.showToast(__('toast.clusterDeleted') || 'Cluster deleted successfully', 'success');
                        await window.loadClusters();
                    } else {
                        window.showToast(__('toast.deleteFailed') + ': ' + json.message, 'danger');
                    }
                } catch (e) {
                    console.error('Delete cluster failed:', e);
                    window.showToast('Error: ' + e.message, 'danger');
                }
            }
        );
    };

    // ===== Show Add Cluster Modal =====
    window.showAddClusterModal = function() {
        var defaultCluster = {
            clusterId: 'cluster-' + Date.now(),
            destinations: {
                'd1': 'http://localhost:5001'
            },
            loadBalancingPolicy: 'RoundRobin'
        };
        var __ = window.__;
        
        window.showQuickAddModal(__('modal.addNewCluster'), defaultCluster, async function(newData) {
            // Validate required fields
            if (!newData.clusterId) {
                alert(__('modal.clusterIdRequired'));
                return false;
            }
            
            // Destinations is now collected as object from KV editor
            var destinations = newData.destinations || {};
            
            if (Object.keys(destinations).length === 0) {
                alert('At least one destination is required');
                return false;
            }
            
            var d = window.__dashboard;
            var request = {
                clusterId: newData.clusterId,
                destinations: destinations,
                loadBalancingPolicy: newData.loadBalancingPolicy || 'RoundRobin'
            };
            
            try {
                var res = await window.authFetch(d.basePath + '/../api/gateway/clusters', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(request)
                });
                var json = await res.json();
                
                if (json.code === 200) {
                    window.showToast(json.message, 'success');
                    await window.loadClusters();
                    return true;
                } else {
                    window.showToast(json.message || 'Failed to create cluster', 'danger');
                    return false;
                }
            } catch (e) {
                console.error('Create cluster failed:', e);
                window.showToast('Error: ' + e.message, 'danger');
                return false;
            }
        }, {
            fieldTypes: {
                destinations: 'keyvalue'
            },
            requiredFields: ['clusterId', 'destinations']
        });
    };

    // ===== Filter Clusters =====
    window.filterClusters = function(keyword) {
        var allClusters = window._allClusters || [];
        if (!keyword || keyword.trim() === '') {
            // If no keyword, reload all clusters
            window.loadClusters();
            return;
        }
        
        var lowerKeyword = keyword.toLowerCase().trim();
        var filteredClusters = allClusters.filter(function(cluster) {
            // Search in clusterId
            if (cluster.clusterId && cluster.clusterId.toLowerCase().includes(lowerKeyword)) {
                return true;
            }
            // Search in destinations (name and address)
            if (cluster.destinations) {
                for (var i = 0; i < cluster.destinations.length; i++) {
                    var dest = cluster.destinations[i];
                    if ((dest.name && dest.name.toLowerCase().includes(lowerKeyword)) ||
                        (dest.address && dest.address.toLowerCase().includes(lowerKeyword))) {
                        return true;
                    }
                }
            }
            // Search in loadBalancingPolicy
            if (cluster.loadBalancingPolicy && cluster.loadBalancingPolicy.toLowerCase().includes(lowerKeyword)) {
                return true;
            }
            return false;
        });
        
        // Render filtered clusters
        var __ = window.__;
        var html = '';
        
        filteredClusters.forEach(function(c) {
            var rowspan = c.destinations.length || 1;
            c.destinations.forEach(function(dest, i) {
                html += '<tr class="cluster-row" data-cluster="' + window.escapeHtml(c.clusterId) + '" style="cursor:pointer;">';
                if (i === 0) {
                    html += '<td rowspan="' + rowspan + '" style="font-weight:600;vertical-align:middle;"><span class="cluster-expand-icon" style="display:inline-block;width:16px;">\u25B6</span> ' + window.escapeHtml(c.clusterId) + '</td>';
                }
                html += '<td><code>' + dest.name + '</code></td>';
                html += '<td><a href="' + dest.address + '" target="_blank" class="text-decoration-none">' + dest.address + '</a></td>';
                if (dest.health) html += '<td><span style="color:#64748b;">' + window.escapeHtml(dest.health) + '</span></td>';
                else html += '<td>' + window.healthDot(dest.activeHealth || 'Unknown') + '</td>';
                html += '<td>' + window.healthDot(dest.passiveHealth || 'Unknown') + '</td>';
                if (i === 0) {
                    html += '<td rowspan="' + rowspan + '" style="vertical-align:middle;"><span class="badge bg-secondary">' + c.loadBalancingPolicy + '</span></td>';
                    html += '<td rowspan="' + rowspan + '" style="vertical-align:middle;" onclick="event.stopPropagation();">';
                    if (c.isEditable) {
                        html += '<button class="btn btn-sm btn-outline-primary me-1" onclick="editCluster(\'' + window.escapeHtml(c.clusterId) + '\')" title="Edit">';
                        html += '<i class="bi bi-pencil"></i></button>';
                        html += '<button class="btn btn-sm btn-outline-danger" onclick="deleteCluster(\'' + window.escapeHtml(c.clusterId) + '\')" title="Delete">';
                        html += '<i class="bi bi-trash"></i></button>';
                    } else {
                        html += '<span class="text-muted">-</span>';
                    }
                    html += '</td>';
                }
                html += '</tr>';
            });

            // Expandable detail row (same as in loadClusters)
            html += '<tr class="cluster-detail" data-cluster="' + window.escapeHtml(c.clusterId) + '" style="display:none;">';
            html += '<td colspan="7" style="padding:0;">';
            html += '<div style="padding:14px 20px;background:#f8fafc;border-bottom:2px solid #e2e8f0;font-size:13px;line-height:1.8;">';

            html += '<div style="display:flex;flex-wrap:wrap;gap:8px 24px;margin-bottom:6px;">';
            html += '<span><strong>ClusterId:</strong> ' + window.escapeHtml(c.clusterId) + '</span>';
            html += '<span><strong>LoadBalancingPolicy:</strong> <span class="badge bg-secondary">' + window.escapeHtml(c.loadBalancingPolicy) + '</span></span>';
            html += '</div>';

            if (c.destinations && c.destinations.length > 0) {
                html += '<div style="margin-top:6px;margin-bottom:4px;"><strong>' + __('index.detail.destinations') + '</strong></div>';
                html += '<table style="margin:0 0 6px 0;border-collapse:collapse;font-size:12px;width:auto;">';
                html += '<tr style="background:#e2e8f0;"><th style="padding:3px 10px;border:1px solid #cbd5e1;text-align:left;">' + __('index.detail.node') + '</th><th style="padding:3px 10px;border:1px solid #cbd5e1;text-align:left;">' + __('index.detail.address') + '</th></tr>';
                c.destinations.forEach(function(dest) {
                    html += '<tr><td style="padding:2px 10px;border:1px solid #e2e8f0;"><code>' + window.escapeHtml(dest.name) + '</code></td>';
                    html += '<td style="padding:2px 10px;border:1px solid #e2e8f0;">' + window.escapeHtml(dest.address) + '</td></tr>';
                });
                html += '</table>';
            }

            html += '</div>';
            html += '</td>';
            html += '</tr>';
        });

        document.getElementById('cluster-tbody').innerHTML =
            html || '<tr><td colspan="7" class="text-center text-muted py-4">未找到匹配的集群</td></tr>';
        
        // Re-attach click handlers for cluster expand/collapse
        document.querySelectorAll('.cluster-row').forEach(function(row) {
            row.addEventListener('click', function() {
                window.toggleClusterDetail(this);
            });
        });
    };
})();
