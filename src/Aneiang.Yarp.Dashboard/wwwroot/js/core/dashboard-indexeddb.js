/**
 * Dashboard IndexedDB Module
 * Provides client-side caching for logs, stats, and configuration
 */
(function() {
    'use strict';

    const DB_NAME = 'YarpDashboardDB';
    const DB_VERSION = 1;

    window.DashboardIndexedDB = {
        db: null,
        isOpen: false,
        openPromise: null,

        // Database schema
        stores: {
            logs: {
                keyPath: 'id',
                autoIncrement: true,
                indexes: [
                    { name: 'timestamp', keyPath: 'timestamp', unique: false },
                    { name: 'traceId', keyPath: 'traceId', unique: false },
                    { name: 'level', keyPath: 'level', unique: false }
                ]
            },
            stats: {
                keyPath: 'key',
                autoIncrement: false,
                indexes: [
                    { name: 'timestamp', keyPath: 'timestamp', unique: false }
                ]
            },
            routes: {
                keyPath: 'routeId',
                autoIncrement: false
            },
            clusters: {
                keyPath: 'clusterId',
                autoIncrement: false
            },
            cache: {
                keyPath: 'key',
                autoIncrement: false,
                indexes: [
                    { name: 'expires', keyPath: 'expires', unique: false }
                ]
            }
        },

        // ===== Database Initialization =====
        async open() {
            if (this.isOpen && this.db) return this.db;
            if (this.openPromise) return this.openPromise;

            this.openPromise = new Promise((resolve, reject) => {
                const request = indexedDB.open(DB_NAME, DB_VERSION);

                request.onerror = () => reject(request.error);
                request.onsuccess = () => {
                    this.db = request.result;
                    this.isOpen = true;
                    console.log('[IndexedDB] Database opened');
                    resolve(this.db);
                };

                request.onupgradeneeded = (event) => {
                    const db = event.target.result;

                    // Create stores
                    for (const [storeName, config] of Object.entries(this.stores)) {
                        if (!db.objectStoreNames.contains(storeName)) {
                            const store = db.createObjectStore(storeName, {
                                keyPath: config.keyPath,
                                autoIncrement: config.autoIncrement
                            });

                            // Create indexes
                            if (config.indexes) {
                                for (const idx of config.indexes) {
                                    store.createIndex(idx.name, idx.keyPath, { unique: idx.unique });
                                }
                            }

                            console.log(`[IndexedDB] Created store: ${storeName}`);
                        }
                    }
                };
            });

            return this.openPromise;
        },

        async close() {
            if (this.db) {
                this.db.close();
                this.db = null;
                this.isOpen = false;
                this.openPromise = null;
            }
        },

        // ===== Generic CRUD Operations =====
        async add(storeName, data) {
            await this.open();
            return new Promise((resolve, reject) => {
                const tx = this.db.transaction(storeName, 'readwrite');
                const store = tx.objectStore(storeName);
                const request = store.add(data);

                request.onsuccess = () => resolve(request.result);
                request.onerror = () => reject(request.error);
            });
        },

        async put(storeName, data) {
            await this.open();
            return new Promise((resolve, reject) => {
                const tx = this.db.transaction(storeName, 'readwrite');
                const store = tx.objectStore(storeName);
                const request = store.put(data);

                request.onsuccess = () => resolve(request.result);
                request.onerror = () => reject(request.error);
            });
        },

        async get(storeName, key) {
            await this.open();
            return new Promise((resolve, reject) => {
                const tx = this.db.transaction(storeName, 'readonly');
                const store = tx.objectStore(storeName);
                const request = store.get(key);

                request.onsuccess = () => resolve(request.result);
                request.onerror = () => reject(request.error);
            });
        },

        async getAll(storeName, limit = 1000) {
            await this.open();
            return new Promise((resolve, reject) => {
                const tx = this.db.transaction(storeName, 'readonly');
                const store = tx.objectStore(storeName);
                const results = [];

                const request = store.openCursor();
                request.onsuccess = (event) => {
                    const cursor = event.target.result;
                    if (cursor && results.length < limit) {
                        results.push(cursor.value);
                        cursor.continue();
                    } else {
                        resolve(results);
                    }
                };
                request.onerror = () => reject(request.error);
            });
        },

        async delete(storeName, key) {
            await this.open();
            return new Promise((resolve, reject) => {
                const tx = this.db.transaction(storeName, 'readwrite');
                const store = tx.objectStore(storeName);
                const request = store.delete(key);

                request.onsuccess = () => resolve();
                request.onerror = () => reject(request.error);
            });
        },

        async clear(storeName) {
            await this.open();
            return new Promise((resolve, reject) => {
                const tx = this.db.transaction(storeName, 'readwrite');
                const store = tx.objectStore(storeName);
                const request = store.clear();

                request.onsuccess = () => resolve();
                request.onerror = () => reject(request.error);
            });
        },

        // ===== Query Operations =====
        async getByIndex(storeName, indexName, value) {
            await this.open();
            return new Promise((resolve, reject) => {
                const tx = this.db.transaction(storeName, 'readonly');
                const store = tx.objectStore(storeName);
                const index = store.index(indexName);
                const request = index.getAll(value);

                request.onsuccess = () => resolve(request.result);
                request.onerror = () => reject(request.error);
            });
        },

        async getRange(storeName, indexName, lower, upper, limit = 1000) {
            await this.open();
            return new Promise((resolve, reject) => {
                const tx = this.db.transaction(storeName, 'readonly');
                const store = tx.objectStore(storeName);
                const index = store.index(indexName);
                const range = IDBKeyRange.bound(lower, upper);
                const results = [];

                const request = index.openCursor(range, 'prev');
                request.onsuccess = (event) => {
                    const cursor = event.target.result;
                    if (cursor && results.length < limit) {
                        results.push(cursor.value);
                        cursor.continue();
                    } else {
                        resolve(results);
                    }
                };
                request.onerror = () => reject(request.error);
            });
        },

        // ===== Cache Operations =====
        async cacheGet(key) {
            const item = await this.get('cache', key);
            if (!item) return null;

            // Check expiration
            if (item.expires && item.expires < Date.now()) {
                await this.delete('cache', key);
                return null;
            }

            return item.data;
        },

        async cacheSet(key, data, ttlMinutes = 60) {
            const item = {
                key,
                data,
                expires: Date.now() + (ttlMinutes * 60 * 1000),
                timestamp: Date.now()
            };
            await this.put('cache', item);
        },

        async cacheDelete(key) {
            await this.delete('cache', key);
        },

        async cacheClear() {
            await this.clear('cache');
        },

        // ===== Log Operations =====
        async saveLogs(logs) {
            await this.open();
            const tx = this.db.transaction('logs', 'readwrite');
            const store = tx.objectStore('logs');

            // Add timestamps if missing
            const now = Date.now();
            for (const log of logs) {
                if (!log._dbTimestamp) {
                    log._dbTimestamp = now;
                }
                store.put(log);
            }

            return new Promise((resolve, reject) => {
                tx.oncomplete = () => resolve();
                tx.onerror = () => reject(tx.error);
            });
        },

        async getRecentLogs(minutes = 30, limit = 1000) {
            const cutoff = Date.now() - (minutes * 60 * 1000);
            return this.getRange('logs', 'timestamp', cutoff, Date.now(), limit);
        },

        async getLogsByTraceId(traceId) {
            return this.getByIndex('logs', 'traceId', traceId);
        },

        async purgeOldLogs(maxAgeMinutes = 60) {
            const cutoff = Date.now() - (maxAgeMinutes * 60 * 1000);
            const oldLogs = await this.getRange('logs', 'timestamp', 0, cutoff, 10000);

            const tx = this.db.transaction('logs', 'readwrite');
            const store = tx.objectStore('logs');

            for (const log of oldLogs) {
                store.delete(log.id);
            }

            return new Promise((resolve, reject) => {
                tx.oncomplete = () => resolve(oldLogs.length);
                tx.onerror = () => reject(tx.error);
            });
        },

        // ===== Stats Operations =====
        async saveStats(key, data) {
            await this.put('stats', {
                key,
                data,
                timestamp: Date.now()
            });
        },

        async getStats(key) {
            const item = await this.get('stats', key);
            return item ? item.data : null;
        },

        // ===== Configuration Operations =====
        async saveRoutes(routes) {
            await this.open();
            const tx = this.db.transaction('routes', 'readwrite');
            const store = tx.objectStore('routes');

            for (const route of routes) {
                store.put({
                    routeId: route.routeId,
                    data: route,
                    timestamp: Date.now()
                });
            }

            return new Promise((resolve, reject) => {
                tx.oncomplete = () => resolve();
                tx.onerror = () => reject(tx.error);
            });
        },

        async getCachedRoutes() {
            const items = await this.getAll('routes');
            return items.map(i => i.data);
        },

        async saveClusters(clusters) {
            await this.open();
            const tx = this.db.transaction('clusters', 'readwrite');
            const store = tx.objectStore('clusters');

            for (const cluster of clusters) {
                store.put({
                    clusterId: cluster.clusterId,
                    data: cluster,
                    timestamp: Date.now()
                });
            }

            return new Promise((resolve, reject) => {
                tx.oncomplete = () => resolve();
                tx.onerror = () => reject(tx.error);
            });
        },

        async getCachedClusters() {
            const items = await this.getAll('clusters');
            return items.map(i => i.data);
        },

        // ===== Maintenance =====
        async cleanup() {
            // Clean expired cache entries
            const cache = await this.getAll('cache');
            const now = Date.now();

            let cleaned = 0;
            for (const item of cache) {
                if (item.expires && item.expires < now) {
                    await this.delete('cache', item.key);
                    cleaned++;
                }
            }

            // Clean old logs (keep last hour)
            const purgedLogs = await this.purgeOldLogs(60);

            return { cleanedCache: cleaned, purgedLogs };
        },

        async getStats() {
            await this.open();
            const stats = {};

            for (const storeName of this.db.objectStoreNames) {
                const tx = this.db.transaction(storeName, 'readonly');
                const store = tx.objectStore(storeName);
                const count = await new Promise((resolve) => {
                    const req = store.count();
                    req.onsuccess = () => resolve(req.result);
                });
                stats[storeName] = count;
            }

            return stats;
        },

        // ===== Backup / Export =====
        async exportAll() {
            const data = {};
            for (const storeName of Object.keys(this.stores)) {
                data[storeName] = await this.getAll(storeName, 10000);
            }
            return data;
        },

        async importAll(data) {
            for (const [storeName, items] of Object.entries(data)) {
                if (this.stores[storeName]) {
                    await this.clear(storeName);
                    for (const item of items) {
                        await this.put(storeName, item);
                    }
                }
            }
        }
    };

    // Auto-open on first use
    window.DashboardIndexedDB.open().catch(err => {
        console.warn('[IndexedDB] Failed to open:', err);
    });
})();
