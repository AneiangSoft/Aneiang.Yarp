/**
 * Circuit Breaker Module - Status viewer and management
 */
(function() {
    'use strict';

    var CircuitModule = {
        name: 'circuit',
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
                }, 15000);
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
                var container = document.getElementById('circuit-content');
                if (!container) return;

                var data = await window.DashboardApi.getCircuitBreakerStatus();
                this.render(data, container);
                this.updateRefreshTime();
            } catch (error) {
                console.error('[Circuit] Load failed:', error);
                var container = document.getElementById('circuit-content');
                if (container) {
                    container.innerHTML = '<div class="alert alert-danger">' + __('circuit.loadFailed') + '</div>';
                }
            }
        },

        render: function(data, container) {
            window.DashboardDOM.clear(container);

            // API returns an array of CircuitStateInfo objects
            var entries = Array.isArray(data) ? data : [];

            var closedCount = 0, openCount = 0, halfOpenCount = 0;
            entries.forEach(function(circuit) {
                var state = circuit.status;
                if (state === 'Closed') closedCount++;
                else if (state === 'Open') openCount++;
                else if (state === 'HalfOpen') halfOpenCount++;
            });

            // Compact meta line: closed · open · half-open
            var summaryHtml =
                '<div class="pc-meta-line">' +
                    '<span class="pc-meta-item pc-meta-item--ok"><i class="bi bi-check-circle"></i><span class="pc-meta-num">' + closedCount + '</span><span class="pc-meta-label">' + __('circuit.healthy') + '</span></span>' +
                    '<span class="pc-meta-item' + (openCount > 0 ? ' pc-meta-item--danger' : '') + '"><i class="bi bi-x-circle"></i><span class="pc-meta-num">' + openCount + '</span><span class="pc-meta-label">' + __('circuit.tripped') + '</span></span>' +
                    '<span class="pc-meta-item' + (halfOpenCount > 0 ? ' pc-meta-item--warn' : '') + '"><i class="bi bi-arrow-repeat"></i><span class="pc-meta-num">' + halfOpenCount + '</span><span class="pc-meta-label">' + __('circuit.recovering') + '</span></span>' +
                '</div>';

            if (entries.length === 0) {
                container.innerHTML = summaryHtml +
                    '<div class="text-center py-5">' +
                        '<i class="bi bi-lightning-charge text-muted" style="font-size:48px;"></i>' +
                        '<p class="text-muted mt-3">' + __('circuit.empty') + '</p>' +
                        '<p class="text-muted small">' + __('circuit.emptyHelp') + '</p>' +
                    '</div>';
                return;
            }

            var self = this;
            var rows = entries.map(function(circuit) {
                var clusterName = circuit.clusterName || circuit.clusterKeySnapshot || circuit.key || '-';
                var destinationId = circuit.destinationKeySnapshot && circuit.destinationKeySnapshot !== 'any'
                    ? circuit.destinationKeySnapshot
                    : null;

                var statusClass = circuit.status === 'Closed' ? 'bg-success' :
                                 circuit.status === 'Open' ? 'bg-danger' : 'bg-warning';
                var statusText = circuit.status === 'Closed' ? __('circuit.status.closed') :
                                 circuit.status === 'Open' ? __('circuit.status.open') : __('circuit.status.halfOpen');
                var stateIcon = circuit.status === 'Closed' ? 'bi-check-circle-fill text-success' :
                               circuit.status === 'Open' ? 'bi-x-circle-fill text-danger' : 'bi-arrow-repeat text-warning';

                var openedAt = circuit.openedAt
                    ? window.DashboardI18n.formatDate(circuit.openedAt)
                    : '-';
                var lastAccessed = circuit.lastAccessedAt
                    ? window.DashboardI18n.formatDate(circuit.lastAccessedAt)
                    : '-';
                var recoverySec = self.formatRecoveryTimeout(circuit);

                return '<tr class="align-middle">' +
                    '<td><i class="bi ' + stateIcon + ' me-2"></i><strong>' + window.DashboardUtils.escapeHtml(clusterName) + '</strong>' +
                        (circuit.clusterKeySnapshot && circuit.clusterKeySnapshot !== clusterName
                            ? '<br><small class="text-muted">ID: ' + window.DashboardUtils.escapeHtml(circuit.clusterKeySnapshot) + '</small>'
                            : '') +
                    '</td>' +
                    '<td><code>' + (destinationId ? window.DashboardUtils.escapeHtml(destinationId) : '-') + '</code></td>' +
                    '<td><span class="badge ' + statusClass + '">' + statusText + '</span></td>' +
                    '<td>' + circuit.consecutiveFailures + ' / ' + circuit.failureThreshold + '</td>' +
                    '<td>' + recoverySec + '</td>' +
                    '<td class="text-muted small">' + openedAt + '</td>' +
                    '<td class="text-muted small">' + lastAccessed + '</td>' +
                '</tr>';
            }).join('');

            container.innerHTML = summaryHtml +
                '<div class="table-responsive">' +
                    '<table class="table table-hover align-middle">' +
                        '<thead>' +
                            '<tr>' +
                                '<th>' + __('circuit.cluster') + '</th>' +
                                '<th>' + __('circuit.destination') + '</th>' +
                                '<th>' + __('circuit.state') + '</th>' +
                                '<th>' + __('circuit.failures') + '</th>' +
                                '<th>' + __('circuit.recoveryTimeout') + '</th>' +
                                '<th>' + __('circuit.openedAt') + '</th>' +
                                '<th>' + __('circuit.lastAccessed') + '</th>' +
                            '</tr>' +
                        '</thead>' +
                        '<tbody>' + rows + '</tbody>' +
                    '</table>' +
                '</div>';
        },

        formatRecoveryTimeout: function(circuit) {
            if (typeof circuit.recoveryTimeoutSeconds === 'number') {
                return circuit.recoveryTimeoutSeconds + 's';
            }

            if (typeof circuit.recoveryTimeout === 'number') {
                return Math.round(circuit.recoveryTimeout / 1000) + 's';
            }

            if (typeof circuit.recoveryTimeout === 'string') {
                var parts = circuit.recoveryTimeout.split(':');
                if (parts.length === 3) {
                    var seconds = Math.round((parseFloat(parts[0]) || 0) * 3600 + (parseFloat(parts[1]) || 0) * 60 + (parseFloat(parts[2]) || 0));
                    return seconds + 's';
                }
                return circuit.recoveryTimeout;
            }

            return '-';
        },

        resetAll: function() {
            var self = this;
            window.DashboardModals.showConfirm(__('circuit.resetConfirm'), async function() {
                try {
                    await window.DashboardApi.resetCircuitBreakers();
                    window.DashboardModals.showSuccess(__('circuit.resetSuccess'));
                    await self.load();
                } catch (error) {
                    console.error('[Circuit] Reset failed:', error);
                    window.DashboardModals.showError(__('circuit.resetFailed'));
                }
            }, null, { danger: true });
        },

        updateRefreshTime: function() {
            var el = document.getElementById('circuit-refresh-time');
            if (el) {
                el.textContent = window.DashboardI18n.formatDate(new Date());
            }
        }
    };

    if (window.DashboardApp) {
        window.DashboardApp.registerModule('circuit', CircuitModule);
    }
    window.CircuitModule = CircuitModule;
})();
