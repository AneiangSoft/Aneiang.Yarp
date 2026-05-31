/**
 * Service Worker Registration Module
 */
(function() {
    'use strict';

    window.DashboardServiceWorker = {
        registration: null,
        isUpdateAvailable: false,

        // ===== Registration =====
        async register() {
            if (!('serviceWorker' in navigator)) {
                console.log('[SW] Service Worker not supported');
                return false;
            }

            try {
                this.registration = await navigator.serviceWorker.register('/service-worker.js');

                console.log('[SW] Registered:', this.registration.scope);

                // Handle updates
                this.registration.addEventListener('updatefound', () => {
                    const newWorker = this.registration.installing;
                    newWorker.addEventListener('statechange', () => {
                        if (newWorker.state === 'installed' && navigator.serviceWorker.controller) {
                            console.log('[SW] Update available');
                            this.isUpdateAvailable = true;
                            this.showUpdateNotification();
                        }
                    });
                });

                // Listen for messages from SW
                navigator.serviceWorker.addEventListener('message', event => {
                    this.handleMessage(event.data);
                });

                return true;

            } catch (err) {
                console.error('[SW] Registration failed:', err);
                return false;
            }
        },

        // ===== Update Handling =====
        showUpdateNotification() {
            // Dispatch custom event for UI to handle
            window.dispatchEvent(new CustomEvent('sw-update-available', {
                detail: { apply: () => this.applyUpdate() }
            }));

            // Also show console notification
            console.log('%c[SW] New version available!', 'background: #4CAF50; color: white; padding: 4px 8px; border-radius: 4px;');
            console.log('Run DashboardServiceWorker.applyUpdate() to update');
        },

        async applyUpdate() {
            if (!this.registration || !this.registration.waiting) {
                return;
            }

            // Tell SW to skip waiting
            this.registration.waiting.postMessage({ type: 'SKIP_WAITING' });

            // Reload page after update
            window.location.reload();
        },

        async checkForUpdate() {
            if (!this.registration) return;
            await this.registration.update();
        },

        // ===== Cache Management =====
        async clearCache() {
            if (!this.registration) return;

            this.registration.active?.postMessage({ type: 'CLEAR_CACHE' });
            console.log('[SW] Cache cleared');
        },

        async getCacheStats() {
            if (!this.registration || !this.registration.active) {
                return null;
            }

            return new Promise((resolve) => {
                const channel = new MessageChannel();
                channel.port1.onmessage = event => {
                    resolve(event.data);
                };

                this.registration.active.postMessage(
                    { type: 'GET_CACHE_STATS' },
                    [channel.port2]
                );
            });
        },

        async cacheUrls(urls) {
            if (!this.registration || !this.registration.active) {
                return;
            }

            this.registration.active.postMessage({
                type: 'CACHE_URLS',
                payload: { urls }
            });
        },

        // ===== Message Handling =====
        handleMessage(data) {
            switch (data.type) {
                case 'SYNC_LOGS':
                    // Triggered when SW wants to sync logs
                    window.dispatchEvent(new CustomEvent('sw-sync-logs'));
                    break;
            }
        },

        // ===== Background Sync =====
        async sync(tag) {
            if (!('sync' in this.registration)) {
                console.log('[SW] Background Sync not supported');
                return false;
            }

            try {
                await this.registration.sync.register(tag);
                return true;
            } catch (err) {
                console.error('[SW] Sync registration failed:', err);
                return false;
            }
        },

        // ===== Push Notifications =====
        async subscribeToPush() {
            if (!('pushManager' in this.registration)) {
                console.log('[SW] Push not supported');
                return null;
            }

            try {
                const subscription = await this.registration.pushManager.subscribe({
                    userVisibleOnly: true,
                    applicationServerKey: this.urlBase64ToUint8Array(
                        'BEl62iTMgUSt5oqmZ1ILdM4JH5xOJkE-wRuENN8Ef8hLJBJmK2-1Tt_1P7x2_yuYhYxJQXJmK2-1Tt_1P7x2_yuY'
                    )
                });

                console.log('[SW] Push subscription:', subscription);
                return subscription;

            } catch (err) {
                console.error('[SW] Push subscription failed:', err);
                return null;
            }
        },

        urlBase64ToUint8Array(base64String) {
            const padding = '='.repeat((4 - base64String.length % 4) % 4);
            const base64 = (base64String + padding)
                .replace(/\-/g, '+')
                .replace(/_/g, '/');

            const rawData = window.atob(base64);
            const outputArray = new Uint8Array(rawData.length);

            for (let i = 0; i < rawData.length; ++i) {
                outputArray[i] = rawData.charCodeAt(i);
            }

            return outputArray;
        },

        // ===== Connection Status =====
        isOnline() {
            return navigator.onLine;
        },

        async checkConnection() {
            if (!navigator.onLine) {
                return { online: false, type: 'offline' };
            }

            try {
                const start = performance.now();
                const response = await fetch('/api/health', {
                    method: 'HEAD',
                    cache: 'no-store'
                });
                const latency = Math.round(performance.now() - start);

                return {
                    online: true,
                    latency,
                    serverReachable: response.ok
                };
            } catch (err) {
                return {
                    online: true,
                    serverReachable: false,
                    error: err.message
                };
            }
        }
    };

    // Auto-register on load
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', () => {
            window.DashboardServiceWorker.register();
        });
    } else {
        window.DashboardServiceWorker.register();
    }

    // Handle online/offline events
    window.addEventListener('online', () => {
        console.log('[App] Connection restored');
        window.dispatchEvent(new CustomEvent('connection-restored'));
    });

    window.addEventListener('offline', () => {
        console.log('[App] Connection lost');
        window.dispatchEvent(new CustomEvent('connection-lost'));
    });
})();
