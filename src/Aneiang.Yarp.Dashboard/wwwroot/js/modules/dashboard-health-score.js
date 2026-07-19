/**
 * Configuration Health Score Card
 * Fetches and displays gateway configuration health score on the Overview page.
 */
(function() {
    'use strict';

    window.DashboardHealthScore = {
        containerId: 'config-health-card',
        data: null,

        /**
         * Initialize the health score card.
         * Uses IntersectionObserver for lazy loading (only loads when visible).
         * @param {string} containerId - DOM element ID to render into
         */
        init: function(containerId) {
            this.containerId = containerId || 'config-health-card';

            // Lazy load: only fetch when card becomes visible
            var self = this;
            var container = document.getElementById(this.containerId);

            if (container && 'IntersectionObserver' in window) {
                var observer = new IntersectionObserver(function(entries) {
                    entries.forEach(function(entry) {
                        if (entry.isIntersecting) {
                            observer.disconnect();
                            self.load();
                        }
                    });
                }, { rootMargin: '100px' });
                observer.observe(container);
            } else {
                // Fallback: load immediately
                this.load();
            }
        },

        /**
         * Load health data from API.
         */
        load: function() {
            var self = this;
            var prefix = window.__routePrefix || 'apigateway';

            this._renderLoading();

            fetch('/' + prefix + '/api/config/health')
                .then(function(res) { return res.json(); })
                .then(function(json) {
                    if (json.success !== false && json.data) {
                        self.data = json.data;
                        self._render();
                    } else {
                        self._renderError(json.message || 'Failed to load health data');
                    }
                })
                .catch(function(err) {
                    self._renderError(err.message);
                });
        },

        /**
         * Render the health score card.
         */
        _render: function() {
            var container = document.getElementById(this.containerId);
            if (!container || !this.data) return;

            var score = this.data.score;
            var grade = this.data.grade;
            var scoreColor = this._getScoreColor(score);
            var isEn = this._isEn();

            var issuesHtml = '';
            if (this.data.issues && this.data.issues.length > 0) {
                issuesHtml = '<div class="health-issues mt-3">';
                this.data.issues.forEach(function(issue) {
                    var icon = issue.level === 'Critical' ? 'bi-exclamation-triangle-fill text-danger'
                             : issue.level === 'Warning' ? 'bi-exclamation-circle-fill text-warning'
                             : 'bi-info-circle-fill text-info';
                    var levelBadge = issue.level === 'Critical' ? 'bg-danger'
                                   : issue.level === 'Warning' ? 'bg-warning'
                                   : 'bg-info';

                    issuesHtml += '<div class="health-issue-item d-flex align-items-start gap-2 py-1">' +
                        '<i class="bi ' + icon + ' mt-1"></i>' +
                        '<div class="flex-grow-1">' +
                        '<div class="d-flex align-items-center gap-2">' +
                        '<span class="fw-semibold">' + self._esc(issue.title) + '</span>' +
                        '<span class="badge ' + levelBadge + '">' + issue.category + '</span>' +
                        '</div>' +
                        '<small class="text-muted">' + self._esc(issue.description) + '</small>' +
                        (issue.recommendation ? '<div class="small text-primary mt-1"><i class="bi bi-lightbulb"></i> ' + self._esc(issue.recommendation) + '</div>' : '') +
                        '</div>' +
                        (issue.configPageUrl ? '<a href="' + issue.configPageUrl + '" class="btn btn-sm btn-outline-primary flex-shrink-0"><i class="bi bi-arrow-right"></i></a>' : '') +
                        '</div>';
                });
                issuesHtml += '</div>';
            } else {
                issuesHtml = '<div class="text-success mt-3"><i class="bi bi-check-circle-fill"></i> ' +
                    (isEn ? 'No issues found. Configuration looks healthy!' : '未发现问题，配置健康！') +
                    '</div>';
            }

            container.innerHTML =
                '<div class="card">' +
                '<div class="card-header d-flex align-items-center justify-content-between">' +
                '<h6 class="mb-0"><i class="bi bi-shield-check"></i> ' + (isEn ? 'Configuration Health' : '配置健康评分') + '</h6>' +
                '<button class="btn btn-sm btn-outline-secondary" onclick="DashboardHealthScore.load()"><i class="bi bi-arrow-clockwise"></i></button>' +
                '</div>' +
                '<div class="card-body">' +
                '<div class="d-flex align-items-center gap-3 mb-3">' +
                '<div class="health-score-circle" style="width:64px;height:64px;border-radius:50%;display:flex;align-items:center;justify-content:center;font-size:24px;font-weight:bold;color:#fff;background:' + scoreColor + ';">' + score + '</div>' +
                '<div>' +
                '<div class="health-grade" style="font-size:28px;font-weight:bold;color:' + scoreColor + ';">' + grade + '</div>' +
                '<small class="text-muted">' + (isEn ? 'Grade' : '等级') + ' &middot; ' + this.data.triggeredRules + '/' + this.data.totalRules + ' ' + (isEn ? 'rules triggered' : '条规则触发') + '</small>' +
                '</div>' +
                '</div>' +
                '<div class="health-score-bar" style="height:6px;border-radius:3px;background:#e9ecef;overflow:hidden;">' +
                '<div style="height:100%;width:' + score + '%;background:' + scoreColor + ';transition:width 0.5s;"></div>' +
                '</div>' +
                issuesHtml +
                '</div>' +
                '</div>';
        },

        _renderLoading: function() {
            var container = document.getElementById(this.containerId);
            if (!container) return;
            container.innerHTML = '<div class="card"><div class="card-body text-center py-4"><div class="spinner-border spinner-border-sm text-primary"></div> <span class="text-muted">Loading...</span></div></div>';
        },

        _renderError: function(msg) {
            var container = document.getElementById(this.containerId);
            if (!container) return;
            container.innerHTML = '<div class="card"><div class="card-body text-center py-4 text-danger"><i class="bi bi-exclamation-triangle"></i> ' + this._esc(msg || 'Error') + '</div></div>';
        },

        _getScoreColor: function(score) {
            if (score >= 80) return '#28a745'; // green
            if (score >= 60) return '#ffc107'; // yellow
            return '#dc3545'; // red
        },

        _isEn: function() {
            return (typeof window.__ === 'function' && window.__('common.save') === 'Save');
        },

        _esc: function(s) {
            if (!s) return '';
            var d = document.createElement('div');
            d.textContent = s;
            return d.innerHTML;
        }
    };
})();
