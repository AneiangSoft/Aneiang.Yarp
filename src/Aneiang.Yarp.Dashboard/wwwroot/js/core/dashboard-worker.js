/**
 * Dashboard Worker Module
 * Wraps Web Workers with Promise-based API
 */
(function() {
    'use strict';

    window.DashboardWorker = {
        worker: null,
        messageId: 0,
        pending: new Map(),
        isReady: false,

        init() {
            if (this.worker) return Promise.resolve();

            return new Promise((resolve, reject) => {
                try {
                    const workerPath = '/js/workers/log-processor.worker.js';
                    this.worker = new Worker(workerPath);

                    this.worker.onmessage = (e) => this.handleMessage(e.data);
                    this.worker.onerror = (err) => {
                        console.error('[Worker] Error:', err);
                        reject(err);
                    };

                    // Wait for ready signal
                    const checkReady = setInterval(() => {
                        if (this.isReady) {
                            clearInterval(checkReady);
                            resolve();
                        }
                    }, 50);

                    // Timeout after 5 seconds
                    setTimeout(() => {
                        if (!this.isReady) {
                            clearInterval(checkReady);
                            reject(new Error('Worker initialization timeout'));
                        }
                    }, 5000);

                } catch (err) {
                    console.warn('[Worker] Failed to initialize:', err);
                    reject(err);
                }
            });
        },

        handleMessage(data) {
            if (data.type === 'ready') {
                this.isReady = true;
                return;
            }

            const { id, success, result, error, performance } = data;
            const pending = this.pending.get(id);

            if (!pending) {
                console.warn('[Worker] Received response for unknown message:', id);
                return;
            }

            this.pending.delete(id);

            if (success) {
                pending.resolve({ result, performance });
            } else {
                pending.reject(new Error(error));
            }
        },

        send(type, data, timeout = 30000) {
            return new Promise((resolve, reject) => {
                if (!this.worker || !this.isReady) {
                    reject(new Error('Worker not initialized'));
                    return;
                }

                const id = ++this.messageId;
                this.pending.set(id, { resolve, reject });

                // Set timeout
                const timeoutId = setTimeout(() => {
                    this.pending.delete(id);
                    reject(new Error(`Worker operation timed out after ${timeout}ms`));
                }, timeout);

                // Override resolve to clear timeout
                const originalResolve = resolve;
                resolve = (value) => {
                    clearTimeout(timeoutId);
                    originalResolve(value);
                };

                // Update pending with new resolve
                this.pending.set(id, { resolve, reject });

                this.worker.postMessage({ id, type, data });
            });
        },

        async filterLogs(logs, filters) {
            try {
                await this.init();
                const { result } = await this.send('filter', { logs, filters });
                return result.filtered;
            } catch (err) {
                console.warn('[Worker] Filter failed, falling back to main thread:', err);
                return this.fallbackFilter(logs, filters);
            }
        },

        async groupByTrace(logs) {
            try {
                await this.init();
                const { result } = await this.send('groupByTrace', { logs });
                return result.groups;
            } catch (err) {
                console.warn('[Worker] GroupByTrace failed, falling back to main thread:', err);
                return this.fallbackGroupByTrace(logs);
            }
        },

        async calculateStats(logs) {
            try {
                await this.init();
                const { result } = await this.send('calculateStats', { logs });
                return result.stats;
            } catch (err) {
                console.warn('[Worker] Stats calculation failed, falling back to main thread:', err);
                return this.fallbackStats(logs);
            }
        },

        async searchWithHighlight(logs, query, maxResults) {
            try {
                await this.init();
                const { result } = await this.send('searchWithHighlight', { logs, query, maxResults });
                return result.results;
            } catch (err) {
                console.warn('[Worker] Search failed, falling back to main thread:', err);
                return this.fallbackSearch(logs, query, maxResults);
            }
        },

        async aggregateTimeSeries(logs, interval, timeRange) {
            try {
                await this.init();
                const { result } = await this.send('aggregateTimeSeries', { logs, interval, timeRange });
                return result.series;
            } catch (err) {
                console.warn('[Worker] TimeSeries aggregation failed, falling back to main thread:', err);
                return this.fallbackTimeSeries(logs, interval, timeRange);
            }
        },

        async deduplicate(logs, timeWindowMs) {
            try {
                await this.init();
                const { result } = await this.send('deduplicate', { logs, timeWindowMs });
                return result.unique;
            } catch (err) {
                console.warn('[Worker] Deduplication failed, falling back to main thread:', err);
                return logs;
            }
        },

        async filterRoutes(routes, filters) {
            try {
                await this.init();
                const { result } = await this.send('filterRoutes', { routes, filters });
                return result.filtered;
            } catch (err) {
                console.warn('[Worker] Route filter failed, falling back to main thread:', err);
                return this.fallbackFilterRoutes(routes, filters);
            }
        },

        async calculateRouteStats(routes) {
            try {
                await this.init();
                const { result } = await this.send('calculateRouteStats', { routes });
                return result.stats;
            } catch (err) {
                console.warn('[Worker] Route stats failed, falling back to main thread:', err);
                return this.fallbackRouteStats(routes);
            }
        },

        fallbackFilter(logs, filters) {
            const { search, level, status, timeRange, gatewayOnly } = filters;

            return logs.filter(log => {
                if (search) {
                    const searchLower = search.toLowerCase();
                    const text = `${log.message || ''} ${log.category || ''} ${log.traceId || ''}`.toLowerCase();
                    if (!text.includes(searchLower)) return false;
                }
                if (level && level !== 'all' && log.level !== level) return false;
                if (gatewayOnly && !log.category?.includes('Yarp')) return false;
                if (status && status !== 'all' && log.statusCode) {
                    if (status === 'success' && (log.statusCode < 200 || log.statusCode >= 400)) return false;
                    if (status === 'error' && log.statusCode < 400) return false;
                }
                return true;
            });
        },

        fallbackGroupByTrace(logs) {
            const groups = new Map();
            for (const log of logs) {
                if (log.traceId) {
                    if (!groups.has(log.traceId)) {
                        groups.set(log.traceId, []);
                    }
                    groups.get(log.traceId).push(log);
                }
            }
            return Array.from(groups.entries());
        },

        fallbackStats(logs) {
            const stats = {
                total: logs.length,
                byLevel: {},
                responseTime: { count: 0, total: 0, avg: 0 }
            };

            for (const log of logs) {
                const level = log.level || 'Unknown';
                stats.byLevel[level] = (stats.byLevel[level] || 0) + 1;
                if (log.elapsedMs) {
                    stats.responseTime.count++;
                    stats.responseTime.total += log.elapsedMs;
                }
            }

            if (stats.responseTime.count > 0) {
                stats.responseTime.avg = stats.responseTime.total / stats.responseTime.count;
            }

            return stats;
        },

        fallbackSearch(logs, query, maxResults) {
            const results = [];
            const queryLower = query.toLowerCase();

            for (const log of logs) {
                const text = `${log.message || ''} ${log.category || ''}`.toLowerCase();
                if (text.includes(queryLower)) {
                    results.push(log);
                    if (results.length >= maxResults) break;
                }
            }

            return results;
        },

        fallbackTimeSeries(logs, interval, timeRange) {
            const intervalMs = 60000; // 1 minute default
            const timeRangeMs = 60 * 60 * 1000; // 1 hour default
            const now = Date.now();

            const buckets = new Map();
            const bucketCount = Math.ceil(timeRangeMs / intervalMs);

            for (let i = 0; i < bucketCount; i++) {
                const bucketTime = now - (bucketCount - i) * intervalMs;
                const bucketKey = Math.floor(bucketTime / intervalMs) * intervalMs;
                buckets.set(bucketKey, { timestamp: bucketKey, count: 0 });
            }

            for (const log of logs) {
                if (!log.timestamp) continue;
                const logTime = new Date(log.timestamp).getTime();
                if (now - logTime > timeRangeMs) continue;
                const bucketKey = Math.floor(logTime / intervalMs) * intervalMs;
                const bucket = buckets.get(bucketKey);
                if (bucket) bucket.count++;
            }

            return Array.from(buckets.values());
        },

        fallbackFilterRoutes(routes, filters) {
            const { search, method, source } = filters;
            let filtered = routes;

            if (search) {
                const searchLower = search.toLowerCase();
                filtered = filtered.filter(route => {
                    const routeId = (route.routeId || '').toLowerCase();
                    const path = ((route.match && route.match.path) || '').toLowerCase();
                    return routeId.includes(searchLower) || path.includes(searchLower);
                });
            }

            if (method && method !== 'all') {
                filtered = filtered.filter(route => {
                    const methods = route.match && route.match.methods || [];
                    return methods.includes(method);
                });
            }

            if (source && source !== 'all') {
                filtered = filtered.filter(route => (route.source || 'config') === source);
            }

            filtered.sort((a, b) => {
                const orderA = a.order !== null && a.order !== undefined ? a.order : 999999;
                const orderB = b.order !== null && b.order !== undefined ? b.order : 999999;
                return orderA - orderB;
            });

            return filtered;
        },

        fallbackRouteStats(routes) {
            return {
                total: routes.length,
                bySource: {},
                byMethod: {},
                disabled: 0
            };
        },

        terminate() {
            if (this.worker) {
                this.worker.terminate();
                this.worker = null;
                this.isReady = false;
                this.pending.clear();
            }
        }
    };
})();
