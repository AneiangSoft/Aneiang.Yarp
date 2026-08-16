/**
 * Overview page stats module: real-time traffic chart + HTTP fallback loader.
 *
 * Responsibilities:
 * - init():              create the Chart.js instance for #traffic-chart
 * - pushTrafficPoint():  append a QPS sample (called by the SignalR snapshot path)
 * - loadStats():         fetch GET api/overview/snapshot and apply it via the
 *                        page's window.__overviewApplySnapshot hook (HTTP fallback)
 *
 * Registered as DashboardApp module 'stats'.
 */
(function () {
    'use strict';

    var MAX_POINTS = 60; // ~5 minutes of SignalR pushes (5s interval) on one screen
    var chart = null;
    var hadFetchError = false;

    function pad(n) { return n < 10 ? '0' + n : '' + n; }

    function timeLabel() {
        var d = new Date();
        return pad(d.getHours()) + ':' + pad(d.getMinutes()) + ':' + pad(d.getSeconds());
    }

    function init() {
        var canvas = document.getElementById('traffic-chart');
        if (!canvas || !window.Chart) return;

        try {
            var ctx = canvas.getContext('2d');
            if (!ctx) return;

            var gradient = ctx.createLinearGradient(0, 0, 0, 240);
            gradient.addColorStop(0, 'rgba(59,130,246,0.20)');
            gradient.addColorStop(1, 'rgba(59,130,246,0.00)');

            chart = new window.Chart(ctx, {
                type: 'line',
                data: {
                    labels: [],
                    datasets: [{
                        data: [],
                        borderColor: '#3b82f6',
                        backgroundColor: gradient,
                        borderWidth: 2,
                        fill: true,
                        tension: 0.35,
                        pointRadius: 0,
                        pointHitRadius: 8
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    animation: false,
                    plugins: {
                        legend: { display: false },
                        tooltip: {
                            mode: 'index',
                            intersect: false,
                            callbacks: {
                                label: function (item) {
                                    return item.parsed.y + ' req/s';
                                }
                            }
                        }
                    },
                    scales: {
                        x: {
                            ticks: { maxTicksLimit: 6, color: '#94a3b8', font: { size: 11 } },
                            grid: { display: false }
                        },
                        y: {
                            beginAtZero: true,
                            ticks: { color: '#94a3b8', font: { size: 11 } },
                            grid: { color: 'rgba(148,163,184,0.12)' }
                        }
                    }
                }
            });
        } catch (err) {
            if (window.console) console.warn('[stats] traffic chart init failed:', err);
            chart = null;
        }
    }

    /** Append a QPS sample to the traffic chart (no-op before init / when Chart.js missing). */
    function pushTrafficPoint(qps) {
        if (!chart) return;
        var value = typeof qps === 'number' && isFinite(qps) ? qps : 0;

        chart.data.labels.push(timeLabel());
        chart.data.datasets[0].data.push(value);

        if (chart.data.labels.length > MAX_POINTS) {
            chart.data.labels.shift();
            chart.data.datasets[0].data.shift();
        }
        chart.update('none');
    }

    /** HTTP fallback: fetch the overview snapshot and hand it to the page renderer. */
    function loadStats() {
        var api = window.DashboardApi;
        if (!api || !api.endpoints || !api.endpoints.getOverviewSnapshot) return Promise.resolve();

        return api.endpoints.getOverviewSnapshot()
            .then(function (snapshot) {
                // Recovery: hide the page-level error bar once data flows again
                if (hadFetchError && typeof window.__overviewHideError === 'function') {
                    hadFetchError = false;
                    window.__overviewHideError(true);
                }
                if (!snapshot) return;
                if (typeof window.__overviewApplySnapshot === 'function') {
                    window.__overviewApplySnapshot(snapshot);
                }
                pushTrafficPoint(snapshot.currentQps || 0);
            })
            .catch(function (err) {
                if (window.console) console.warn('[stats] snapshot fetch failed:', err);
                // Surface the failure to the user via the page error bar (no-op if absent)
                hadFetchError = true;
                if (typeof window.__overviewShowError === 'function') {
                    window.__overviewShowError();
                }
            });
    }

    if (window.DashboardApp && typeof window.DashboardApp.registerModule === 'function') {
        window.DashboardApp.registerModule('stats', {
            init: init,
            loadStats: loadStats,
            pushTrafficPoint: pushTrafficPoint
        });
    }
})();
