/**
 * Log Processor Web Worker
 * Offloads heavy log processing from main thread
 */

// Message handlers
const handlers = {
    // Filter logs based on criteria
    filter: (data) => {
        const { logs, filters } = data;
        const { search, level, status, timeRange, gatewayOnly } = filters;

        const filtered = logs.filter(log => {
            // Search text filter
            if (search) {
                const searchLower = search.toLowerCase();
                const text = `${log.message || ''} ${log.category || ''} ${log.traceId || ''}`.toLowerCase();
                if (!text.includes(searchLower)) return false;
            }

            // Level filter
            if (level && level !== 'all' && log.level !== level) return false;

            // Gateway only filter
            if (gatewayOnly && !log.category?.includes('Yarp')) return false;

            // Status filter
            if (status && status !== 'all' && log.statusCode) {
                if (status === 'success' && (log.statusCode < 200 || log.statusCode >= 400)) return false;
                if (status === 'error' && log.statusCode < 400) return false;
            }

            // Time range filter
            if (timeRange && log.timestamp) {
                const logTime = new Date(log.timestamp).getTime();
                const now = Date.now();
                const ranges = {
                    '1m': 60 * 1000,
                    '5m': 5 * 60 * 1000,
                    '15m': 15 * 60 * 1000,
                    '1h': 60 * 60 * 1000,
                    '24h': 24 * 60 * 60 * 1000
                };
                if (ranges[timeRange] && now - logTime > ranges[timeRange]) return false;
            }

            return true;
        });

        return { filtered, count: filtered.length };
    },

    // Group logs by traceId
    groupByTrace: (data) => {
        const { logs } = data;
        const groups = new Map();

        for (const log of logs) {
            if (log.traceId) {
                if (!groups.has(log.traceId)) {
                    groups.set(log.traceId, []);
                }
                groups.get(log.traceId).push(log);
            }
        }

        // Sort each group by timestamp
        for (const [traceId, entries] of groups) {
            entries.sort((a, b) => new Date(a.timestamp) - new Date(b.timestamp));
        }

        return { groups: Array.from(groups.entries()), count: groups.size };
    },

    // Calculate statistics
    calculateStats: (data) => {
        const { logs } = data;
        const stats = {
            total: logs.length,
            byLevel: {},
            byStatus: {},
            byHour: {},
            responseTime: {
                count: 0,
                total: 0,
                min: Infinity,
                max: 0,
                avg: 0
            },
            topRoutes: {},
            topClusters: {},
            errorRate: 0
        };

        let errorCount = 0;

        for (const log of logs) {
            // Level stats
            const level = log.level || 'Unknown';
            stats.byLevel[level] = (stats.byLevel[level] || 0) + 1;

            // Status code stats
            if (log.statusCode) {
                const statusGroup = Math.floor(log.statusCode / 100) + 'xx';
                stats.byStatus[statusGroup] = (stats.byStatus[statusGroup] || 0) + 1;
                if (log.statusCode >= 400) errorCount++;
            }

            // Hourly distribution
            if (log.timestamp) {
                const hour = new Date(log.timestamp).getHours();
                stats.byHour[hour] = (stats.byHour[hour] || 0) + 1;
            }

            // Response time stats
            if (log.elapsedMs) {
                stats.responseTime.count++;
                stats.responseTime.total += log.elapsedMs;
                stats.responseTime.min = Math.min(stats.responseTime.min, log.elapsedMs);
                stats.responseTime.max = Math.max(stats.responseTime.max, log.elapsedMs);
            }

            // Route stats
            if (log.routeId) {
                stats.topRoutes[log.routeId] = (stats.topRoutes[log.routeId] || 0) + 1;
            }

            // Cluster stats
            if (log.clusterId) {
                stats.topClusters[log.clusterId] = (stats.topClusters[log.clusterId] || 0) + 1;
            }
        }

        // Calculate averages
        if (stats.responseTime.count > 0) {
            stats.responseTime.avg = stats.responseTime.total / stats.responseTime.count;
        }
        if (stats.responseTime.min === Infinity) {
            stats.responseTime.min = 0;
        }

        // Calculate error rate
        const totalWithStatus = Object.values(stats.byStatus).reduce((a, b) => a + b, 0);
        if (totalWithStatus > 0) {
            stats.errorRate = (errorCount / totalWithStatus) * 100;
        }

        // Sort top routes and clusters
        stats.topRoutes = Object.entries(stats.topRoutes)
            .sort((a, b) => b[1] - a[1])
            .slice(0, 10);

        stats.topClusters = Object.entries(stats.topClusters)
            .sort((a, b) => b[1] - a[1])
            .slice(0, 10);

        return { stats };
    },

    // Search with highlighting
    searchWithHighlight: (data) => {
        const { logs, query, maxResults = 100 } = data;
        const results = [];
        const queryLower = query.toLowerCase();

        for (const log of logs) {
            const text = `${log.message || ''} ${log.category || ''} ${log.traceId || ''} ${JSON.stringify(log.requestBody || '')}`;
            const textLower = text.toLowerCase();

            if (textLower.includes(queryLower)) {
                // Find match positions for highlighting
                const matches = [];
                let pos = textLower.indexOf(queryLower);
                while (pos !== -1) {
                    matches.push({ start: pos, end: pos + query.length });
                    pos = textLower.indexOf(queryLower, pos + 1);
                }

                results.push({
                    log,
                    matches,
                    relevance: matches.length
                });

                if (results.length >= maxResults) break;
            }
        }

        // Sort by relevance
        results.sort((a, b) => b.relevance - a.relevance);

        return { results: results.map(r => r.log), matchCount: results.length };
    },

    // Aggregate time series data for charts
    aggregateTimeSeries: (data) => {
        const { logs, interval = '1m', timeRange = '1h' } = data;

        const intervalMs = {
            '1s': 1000,
            '10s': 10 * 1000,
            '1m': 60 * 1000,
            '5m': 5 * 60 * 1000,
            '15m': 15 * 60 * 1000,
            '1h': 60 * 60 * 1000
        }[interval] || 60000;

        const timeRangeMs = {
            '5m': 5 * 60 * 1000,
            '15m': 15 * 60 * 1000,
            '1h': 60 * 60 * 1000,
            '6h': 6 * 60 * 60 * 1000,
            '24h': 24 * 60 * 60 * 1000
        }[timeRange] || 60 * 60 * 1000;

        const now = Date.now();
        const buckets = new Map();

        // Initialize buckets
        const bucketCount = Math.ceil(timeRangeMs / intervalMs);
        for (let i = 0; i < bucketCount; i++) {
            const bucketTime = now - (bucketCount - i) * intervalMs;
            const bucketKey = Math.floor(bucketTime / intervalMs) * intervalMs;
            buckets.set(bucketKey, {
                timestamp: bucketKey,
                count: 0,
                errors: 0,
                avgResponseTime: 0,
                responseTimes: []
            });
        }

        // Fill buckets
        for (const log of logs) {
            if (!log.timestamp) continue;

            const logTime = new Date(log.timestamp).getTime();
            if (now - logTime > timeRangeMs) continue;

            const bucketKey = Math.floor(logTime / intervalMs) * intervalMs;
            const bucket = buckets.get(bucketKey);

            if (bucket) {
                bucket.count++;
                if (log.statusCode >= 400) bucket.errors++;
                if (log.elapsedMs) bucket.responseTimes.push(log.elapsedMs);
            }
        }

        // Calculate averages and format results
        const series = Array.from(buckets.values()).map(b => ({
            timestamp: b.timestamp,
            count: b.count,
            errors: b.errors,
            avgResponseTime: b.responseTimes.length > 0
                ? b.responseTimes.reduce((a, b) => a + b, 0) / b.responseTimes.length
                : 0
        }));

        return { series, bucketCount: series.length };
    },

    // Deduplicate similar logs
    deduplicate: (data) => {
        const { logs, timeWindowMs = 5000 } = data;
        const signatures = new Map();
        const unique = [];
        const duplicates = [];

        for (const log of logs) {
            // Create signature based on message and level
            const sig = `${log.level || 'Unknown'}|${(log.message || '').substring(0, 100)}`;
            const time = new Date(log.timestamp).getTime();

            if (signatures.has(sig)) {
                const lastTime = signatures.get(sig);
                if (time - lastTime < timeWindowMs) {
                    duplicates.push(log);
                    continue;
                }
            }

            signatures.set(sig, time);
            unique.push(log);
        }

        return { unique, duplicates: duplicates.length, originalCount: logs.length };
    },

    // Filter and sort routes
    filterRoutes: (data) => {
        const { routes, filters } = data;
        const { search, method, source } = filters;

        let filtered = routes;

        // Search filter
        if (search) {
            const searchLower = search.toLowerCase();
            filtered = filtered.filter(route => {
                const routeId = (route.routeId || '').toLowerCase();
                const path = ((route.match && route.match.path) || '').toLowerCase();
                const clusterId = (route.clusterId || '').toLowerCase();
                return routeId.includes(searchLower) || 
                       path.includes(searchLower) || 
                       clusterId.includes(searchLower);
            });
        }

        // Method filter
        if (method && method !== 'all') {
            filtered = filtered.filter(route => {
                const methods = route.match && route.match.methods || [];
                return methods.includes(method);
            });
        }

        // Source filter
        if (source && source !== 'all') {
            filtered = filtered.filter(route => (route.source || 'config') === source);
        }

        // Sort by order
        filtered.sort((a, b) => {
            const orderA = a.order !== null && a.order !== undefined ? a.order : 999999;
            const orderB = b.order !== null && b.order !== undefined ? b.order : 999999;
            return orderA - orderB;
        });

        return { filtered, count: filtered.length, total: routes.length };
    },

    // Calculate route statistics
    calculateRouteStats: (data) => {
        const { routes } = data;
        
        const stats = {
            total: routes.length,
            bySource: {},
            byMethod: {},
            byCluster: {},
            disabled: 0,
            withTransforms: 0,
            withAuthorization: 0
        };

        for (const route of routes) {
            // Source stats
            const src = route.source || 'config';
            stats.bySource[src] = (stats.bySource[src] || 0) + 1;

            // Method stats
            const methods = route.match && route.match.methods || [];
            if (methods.length === 0) {
                stats.byMethod['ANY'] = (stats.byMethod['ANY'] || 0) + 1;
            } else {
                methods.forEach(m => {
                    stats.byMethod[m] = (stats.byMethod[m] || 0) + 1;
                });
            }

            // Cluster stats
            const cluster = route.clusterId || '(none)';
            stats.byCluster[cluster] = (stats.byCluster[cluster] || 0) + 1;

            // Feature stats
            if (route.metadata && route.metadata.Disabled === 'true') {
                stats.disabled++;
            }
            if (route.transforms && route.transforms.length > 0) {
                stats.withTransforms++;
            }
            if (route.authorizationPolicy || route.authorizationPolicy === '') {
                stats.withAuthorization++;
            }
        }

        return { stats };
    }
};

// Handle messages from main thread
self.onmessage = function(e) {
    const { id, type, data } = e.data;

    try {
        const handler = handlers[type];
        if (!handler) {
            throw new Error(`Unknown handler type: ${type}`);
        }

        const startTime = performance.now();
        const result = handler(data);
        const duration = performance.now() - startTime;

        self.postMessage({
            id,
            type,
            success: true,
            result,
            performance: { duration }
        });
    } catch (error) {
        self.postMessage({
            id,
            type,
            success: false,
            error: error.message,
            stack: error.stack
        });
    }
};

// Signal that worker is ready
self.postMessage({ type: 'ready' });
