/**
 * Dashboard Service Worker
 * Provides offline support and caching strategies
 */

const CACHE_VERSION = 'v2';
const STATIC_CACHE = `yarp-dashboard-static-${CACHE_VERSION}`;
const API_CACHE = `yarp-dashboard-api-${CACHE_VERSION}`;
const IMAGE_CACHE = `yarp-dashboard-images-${CACHE_VERSION}`;

// Cache size limits
const MAX_API_CACHE_SIZE = 50;   // Max API responses to cache
const MAX_IMAGE_CACHE_SIZE = 30; // Max images to cache

// Static assets to cache on install
const CONTENT_PATH = '/_content/Aneiang.Yarp.Dashboard';
const STATIC_ASSETS = [
    '/',
    '/dashboard',
    `${CONTENT_PATH}/styles/dashboard-editor.css`,
    `${CONTENT_PATH}/styles/inter-font.css`,
    `${CONTENT_PATH}/js/app/dashboard-bootstrap.js`,
    `${CONTENT_PATH}/js/core/dashboard-api.js`,
    `${CONTENT_PATH}/js/core/dashboard-core.js`,
    `${CONTENT_PATH}/js/core/dashboard-dom.js`,
    `${CONTENT_PATH}/js/core/dashboard-state.js`,
    `${CONTENT_PATH}/js/core/dashboard-utils.js`,
    `${CONTENT_PATH}/js/core/dashboard-performance.js`,
    `${CONTENT_PATH}/js/core/dashboard-indexeddb.js`,
    `${CONTENT_PATH}/js/core/dashboard-worker.js`,
    `${CONTENT_PATH}/js/modules/dashboard-home.js`,
    `${CONTENT_PATH}/js/modules/dashboard-routes.js`,
    `${CONTENT_PATH}/js/modules/dashboard-clusters.js`,
    `${CONTENT_PATH}/js/modules/dashboard-logs.js`
];

// ===== Install Event =====
self.addEventListener('install', event => {
    console.log('[SW] Installing...');

    event.waitUntil(
        caches.open(STATIC_CACHE)
            .then(cache => {
                console.log('[SW] Caching static assets');
                return cache.addAll(STATIC_ASSETS);
            })
            .then(() => {
                console.log('[SW] Installation complete');
                return self.skipWaiting();
            })
            .catch(err => {
                console.error('[SW] Install failed:', err);
            })
    );
});

// ===== Activate Event =====
self.addEventListener('activate', event => {
    console.log('[SW] Activating...');

    event.waitUntil(
        caches.keys()
            .then(cacheNames => {
                return Promise.all(
                    cacheNames
                        .filter(name => name.startsWith('yarp-dashboard-') && !name.includes(CACHE_VERSION))
                        .map(name => {
                            console.log('[SW] Deleting old cache:', name);
                            return caches.delete(name);
                        })
                );
            })
            .then(() => {
                console.log('[SW] Activation complete');
                return self.clients.claim();
            })
    );
});

// ===== Fetch Event =====
self.addEventListener('fetch', event => {
    const { request } = event;
    const url = new URL(request.url);

    // Skip non-GET requests
    if (request.method !== 'GET') {
        return;
    }

    // Skip cross-origin requests
    if (url.origin !== self.location.origin) {
        return;
    }

    // Route to appropriate strategy
    if (isAPIRequest(request)) {
        event.respondWith(apiStrategy(request));
    } else if (isImageRequest(request)) {
        event.respondWith(imageStrategy(request));
    } else if (isStaticAsset(request)) {
        event.respondWith(staticStrategy(request));
    } else {
        event.respondWith(networkFirstStrategy(request));
    }
});

// ===== Request Classification =====
function isAPIRequest(request) {
    return request.url.includes('/api/') ||
           request.url.includes('/dashboard/api/');
}

function isImageRequest(request) {
    return request.destination === 'image' ||
           /\.(png|jpg|jpeg|gif|webp|svg|ico)$/i.test(request.url);
}

function isStaticAsset(request) {
    return request.destination === 'script' ||
           request.destination === 'style' ||
           /\.(js|css|woff2?)$/i.test(request.url);
}

// ===== Cache Size Management =====
async function trimCache(cache, maxSize) {
    const keys = await cache.keys();
    if (keys.length >= maxSize) {
        // Delete oldest entries (first in the cache)
        const toDelete = keys.length - maxSize + 1;
        for (let i = 0; i < toDelete; i++) {
            await cache.delete(keys[i]);
        }
    }
}

// ===== Caching Strategies =====

// Cache First - for static assets (no size limit, static assets are finite)
async function staticStrategy(request) {
    const cache = await caches.open(STATIC_CACHE);
    const cached = await cache.match(request);

    if (cached) {
        // Return cached and update in background
        updateCacheInBackground(cache, request);
        return cached;
    }

    try {
        const response = await fetch(request);
        if (response.ok) {
            cache.put(request, response.clone());
        }
        return response;
    } catch (err) {
        console.error('[SW] Fetch failed for static asset:', request.url, err);
        return new Response('Offline', { status: 503 });
    }
}

// Stale While Revalidate - for API requests (with size limit)
async function apiStrategy(request) {
    const cache = await caches.open(API_CACHE);
    const cached = await cache.match(request);

    // Always fetch from network
    const fetchPromise = fetch(request)
        .then(response => {
            if (response.ok) {
                cache.put(request, response.clone()).then(() => {
                    // Trim API cache to max size
                    trimCache(cache, MAX_API_CACHE_SIZE);
                });
            }
            return response;
        })
        .catch(err => {
            console.warn('[SW] API fetch failed:', request.url);
            // Return cached if available
            if (cached) {
                return cached;
            }
            throw err;
        });

    // Return cached immediately if available
    if (cached) {
        return cached;
    }

    return fetchPromise;
}

// Cache First with fallback - for images (with size limit)
async function imageStrategy(request) {
    const cache = await caches.open(IMAGE_CACHE);
    const cached = await cache.match(request);

    if (cached) {
        return cached;
    }

    try {
        const response = await fetch(request);
        if (response.ok) {
            await cache.put(request, response.clone()).then(() => {
                // Trim image cache to max size
                trimCache(cache, MAX_IMAGE_CACHE_SIZE);
            });
        }
        return response;
    } catch (err) {
        console.warn('[SW] Image fetch failed:', request.url);
        // Return placeholder or empty response
        return new Response('', { status: 404 });
    }
}

// Network First - for HTML pages (no persistent caching)
async function networkFirstStrategy(request) {
    try {
        const networkResponse = await fetch(request);
        return networkResponse;
    } catch (err) {
        console.warn('[SW] Network fetch failed, trying cache:', request.url);
        const cache = await caches.open(STATIC_CACHE);
        const cached = await cache.match(request);

        if (cached) {
            return cached;
        }

        // Return offline page for navigation requests
        if (request.mode === 'navigate') {
            return cache.match('/dashboard');
        }

        throw err;
    }
}

// ===== Background Update =====
async function updateCacheInBackground(cache, request) {
    try {
        const response = await fetch(request);
        if (response.ok) {
            cache.put(request, response);
        }
    } catch (err) {
        // Ignore background update errors
    }
}

// ===== Message Handling =====
self.addEventListener('message', event => {
    const { type, payload } = event.data;

    switch (type) {
        case 'SKIP_WAITING':
            self.skipWaiting();
            break;

        case 'CLEAR_CACHE':
            event.waitUntil(clearAllCaches());
            break;

        case 'GET_CACHE_STATS':
            event.waitUntil(getCacheStats().then(stats => {
                event.ports[0].postMessage(stats);
            }));
            break;

        case 'CACHE_URLS':
            if (payload && payload.urls) {
                event.waitUntil(cacheUrls(payload.urls));
            }
            break;
    }
});

// ===== Cache Management =====
async function clearAllCaches() {
    const cacheNames = await caches.keys();
    return Promise.all(
        cacheNames
            .filter(name => name.startsWith('yarp-dashboard-'))
            .map(name => caches.delete(name))
    );
}

async function getCacheStats() {
    const stats = {
        static: 0,
        api: 0,
        images: 0,
        total: 0
    };

    const [staticCache, apiCache, imageCache] = await Promise.all([
        caches.open(STATIC_CACHE),
        caches.open(API_CACHE),
        caches.open(IMAGE_CACHE)
    ]);

    const [staticKeys, apiKeys, imageKeys] = await Promise.all([
        staticCache.keys(),
        apiCache.keys(),
        imageCache.keys()
    ]);

    stats.static = staticKeys.length;
    stats.api = apiKeys.length;
    stats.images = imageKeys.length;
    stats.total = staticKeys.length + apiKeys.length + imageKeys.length;

    return stats;
}

async function cacheUrls(urls) {
    const cache = await caches.open(STATIC_CACHE);
    return Promise.all(
        urls.map(url =>
            fetch(url)
                .then(response => {
                    if (response.ok) {
                        return cache.put(url, response);
                    }
                })
                .catch(err => console.warn('[SW] Failed to cache:', url, err))
        )
    );
}

// ===== Background Sync =====
self.addEventListener('sync', event => {
    if (event.tag === 'sync-logs') {
        event.waitUntil(syncLogs());
    }
});

async function syncLogs() {
    // Try to sync any pending log operations
    const clients = await self.clients.matchAll();
    clients.forEach(client => {
        client.postMessage({ type: 'SYNC_LOGS' });
    });
}

// ===== Push Notifications (placeholder) =====
self.addEventListener('push', event => {
    const data = event.data?.json() || {};

    event.waitUntil(
        self.registration.showNotification(data.title || 'YARP Dashboard', {
            body: data.body || 'New notification',
            icon: `${CONTENT_PATH}/logo.png`,
            badge: `${CONTENT_PATH}/logo.png`,
            data: data
        })
    );
});

self.addEventListener('notificationclick', event => {
    event.notification.close();

    event.waitUntil(
        self.clients.openWindow('/dashboard')
    );
});
