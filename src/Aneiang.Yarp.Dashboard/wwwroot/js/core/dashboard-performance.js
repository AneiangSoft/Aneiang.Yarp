/**
 * Dashboard Performance Module
 * Provides virtual scrolling, DOM pooling, performance monitoring, and batching
 */
(function() {
    'use strict';

    window.DashboardPerformance = {
        // ===== Virtual Scroller =====
        VirtualScroller: class {
            constructor(container, options = {}) {
                this.container = container;
                this.options = {
                    itemHeight: options.itemHeight || 48,
                    overscan: options.overscan || 5,
                    renderFn: options.renderFn || this.defaultRenderFn,
                    ...options
                };

                this.scrollContainer = options.scrollContainer || container.parentElement;
                this.visibleItems = new Map();
                this.itemPool = [];
                this.poolSize = options.poolSize || 100;

                this.state = {
                    startIndex: 0,
                    endIndex: 0,
                    scrollTop: 0,
                    totalHeight: 0,
                    data: [],
                    containerHeight: 0
                };

                this.init();
            }

            init() {
                // Create wrapper structure
                this.wrapper = document.createElement('div');
                this.wrapper.className = 'virtual-scroll-wrapper';
                this.wrapper.style.cssText = 'position: relative; width: 100%;';

                this.content = document.createElement('div');
                this.content.className = 'virtual-scroll-content';
                this.content.style.cssText = 'position: absolute; top: 0; left: 0; right: 0;';

                this.wrapper.appendChild(this.content);

                // Clear container and add wrapper
                this.container.innerHTML = '';
                this.container.appendChild(this.wrapper);

                // Measure container
                this.updateContainerHeight();

                // Setup scroll handler with RAF throttling
                this.scrollHandler = this.throttleByRaf(() => this.updateVisibleRange());
                this.scrollContainer.addEventListener('scroll', this.scrollHandler, { passive: true });

                // Setup resize observer
                if (window.ResizeObserver) {
                    this.resizeObserver = new ResizeObserver(this.debounce(() => {
                        this.updateContainerHeight();
                        this.updateVisibleRange();
                    }, 100));
                    this.resizeObserver.observe(this.scrollContainer);
                }

                // Pre-initialize DOM pool
                this.initPool();
            }

            initPool() {
                const fragment = document.createDocumentFragment();
                for (let i = 0; i < this.poolSize; i++) {
                    const el = document.createElement('div');
                    el.style.cssText = `position: absolute; left: 0; right: 0; height: ${this.options.itemHeight}px;`;
                    el.className = 'virtual-item';
                    el.dataset.poolId = i;
                    this.itemPool.push({ el, inUse: false });
                    fragment.appendChild(el);
                }
                this.content.appendChild(fragment);
            }

            getPooledItem() {
                const item = this.itemPool.find(p => !p.inUse);
                if (item) {
                    item.inUse = true;
                    return item.el;
                }
                // If pool exhausted, create new element
                const el = document.createElement('div');
                el.style.cssText = `position: absolute; left: 0; right: 0; height: ${this.options.itemHeight}px;`;
                el.className = 'virtual-item';
                this.content.appendChild(el);
                return el;
            }

            releaseItem(el) {
                const poolItem = this.itemPool.find(p => p.el === el);
                if (poolItem) {
                    poolItem.inUse = false;
                    el.style.display = 'none';
                    el.innerHTML = '';
                } else {
                    // Not from pool, remove it
                    el.remove();
                }
            }

            updateContainerHeight() {
                this.state.containerHeight = this.scrollContainer.clientHeight;
            }

            setData(data) {
                this.state.data = data;
                this.state.totalHeight = data.length * this.options.itemHeight;
                this.wrapper.style.height = `${this.state.totalHeight}px`;
                this.updateVisibleRange();
            }

            updateVisibleRange() {
                const { scrollTop, containerHeight, data } = this.state;
                const { itemHeight, overscan, renderFn } = this.options;

                const startIndex = Math.max(0, Math.floor(scrollTop / itemHeight) - overscan);
                const visibleCount = Math.ceil(containerHeight / itemHeight) + overscan * 2;
                const endIndex = Math.min(data.length, startIndex + visibleCount);

                // Check if update needed
                if (Math.abs(startIndex - this.state.startIndex) < overscan / 2 &&
                    Math.abs(endIndex - this.state.endIndex) < overscan / 2) {
                    return;
                }

                this.state.startIndex = startIndex;
                this.state.endIndex = endIndex;

                // Calculate which items to show/hide
                const neededIndices = new Set();
                for (let i = startIndex; i < endIndex; i++) {
                    neededIndices.add(i);
                }

                // Release items that are no longer visible
                for (const [index, el] of this.visibleItems) {
                    if (!neededIndices.has(index)) {
                        this.releaseItem(el);
                        this.visibleItems.delete(index);
                    }
                }

                // Render new items
                for (let i = startIndex; i < endIndex; i++) {
                    if (!this.visibleItems.has(i)) {
                        const el = this.getPooledItem();
                        el.style.display = 'block';
                        el.style.top = `${i * itemHeight}px`;
                        renderFn(el, data[i], i);
                        this.visibleItems.set(i, el);
                    }
                }

                // Update content transform
                this.content.style.transform = `translateY(${startIndex * itemHeight}px)`;
            }

            scrollToIndex(index, behavior = 'smooth') {
                const { itemHeight } = this.options;
                this.scrollContainer.scrollTo({
                    top: index * itemHeight,
                    behavior
                });
            }

            destroy() {
                this.scrollContainer.removeEventListener('scroll', this.scrollHandler);
                if (this.resizeObserver) {
                    this.resizeObserver.disconnect();
                }
                this.container.innerHTML = '';
                this.visibleItems.clear();
                this.itemPool = [];
            }

            // ===== Utilities =====
            throttleByRaf(fn) {
                let ticking = false;
                return (...args) => {
                    if (!ticking) {
                        requestAnimationFrame(() => {
                            fn.apply(this, args);
                            ticking = false;
                        });
                        ticking = true;
                    }
                };
            }

            debounce(fn, delay) {
                let timeout;
                return (...args) => {
                    clearTimeout(timeout);
                    timeout = setTimeout(() => fn.apply(this, args), delay);
                };
            }

            defaultRenderFn(el, data, index) {
                el.textContent = JSON.stringify(data);
            }
        },

        // ===== Batched Updates =====
        Batcher: class {
            constructor() {
                this.updates = new Map();
                this.scheduled = false;
            }

            schedule(key, fn) {
                this.updates.set(key, fn);
                if (!this.scheduled) {
                    this.scheduled = true;
                    requestAnimationFrame(() => this.flush());
                }
            }

            flush() {
                this.updates.forEach(fn => fn());
                this.updates.clear();
                this.scheduled = false;
            }
        },

        // ===== Memory Monitor =====
        MemoryMonitor: {
            isWatching: false,
            snapshots: [],
            maxSnapshots: 50,

            start() {
                if (this.isWatching || !performance.memory) return;
                this.isWatching = true;
                this.takeSnapshot();
                this.interval = setInterval(() => this.takeSnapshot(), 30000);
            },

            stop() {
                this.isWatching = false;
                clearInterval(this.interval);
            },

            takeSnapshot() {
                if (!performance.memory) return;

                const snapshot = {
                    timestamp: Date.now(),
                    usedJSHeapSize: performance.memory.usedJSHeapSize,
                    totalJSHeapSize: performance.memory.totalJSHeapSize,
                    jsHeapSizeLimit: performance.memory.jsHeapSizeLimit
                };

                this.snapshots.push(snapshot);

                if (this.snapshots.length > this.maxSnapshots) {
                    this.snapshots.shift();
                }

                // Check for memory leaks
                if (this.snapshots.length >= 3) {
                    this.checkForLeak();
                }
            },

            checkForLeak() {
                const recent = this.snapshots.slice(-3);
                const growing = recent.every((snap, i) =>
                    i === 0 || snap.usedJSHeapSize > recent[i - 1].usedJSHeapSize
                );

                if (growing) {
                    const growthRate = (recent[2].usedJSHeapSize - recent[0].usedJSHeapSize) / recent[0].usedJSHeapSize;
                    if (growthRate > 0.3) {
                        console.warn('[MemoryMonitor] Potential memory leak detected:', {
                            growthRate: `${(growthRate * 100).toFixed(1)}%`,
                            usedHeap: this.formatBytes(recent[2].usedJSHeapSize)
                        });
                    }
                }
            },

            formatBytes(bytes) {
                if (bytes === 0) return '0 B';
                const k = 1024;
                const sizes = ['B', 'KB', 'MB', 'GB'];
                const i = Math.floor(Math.log(bytes) / Math.log(k));
                return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
            },

            getStats() {
                if (!this.snapshots.length) return null;
                const latest = this.snapshots[this.snapshots.length - 1];
                const oldest = this.snapshots[0];
                return {
                    current: this.formatBytes(latest.usedJSHeapSize),
                    peak: this.formatBytes(Math.max(...this.snapshots.map(s => s.usedJSHeapSize))),
                    trend: latest.usedJSHeapSize > oldest.usedJSHeapSize ? 'increasing' : 'stable'
                };
            }
        },

        // ===== Long Task Monitor =====
        LongTaskMonitor: {
            observer: null,
            longTasks: [],

            start() {
                if (!('PerformanceObserver' in window)) return;
                if (!window.PerformanceLongTaskTiming) return;

                this.observer = new PerformanceObserver(list => {
                    for (const entry of list.getEntries()) {
                        this.longTasks.push({
                            duration: entry.duration,
                            startTime: entry.startTime,
                            name: entry.name
                        });

                        if (entry.duration > 100) {
                            console.warn('[LongTaskMonitor] Long task detected:', {
                                duration: `${entry.duration.toFixed(1)}ms`,
                                type: entry.name
                            });
                        }
                    }
                });

                this.observer.observe({ entryTypes: ['longtask'] });
            },

            stop() {
                if (this.observer) {
                    this.observer.disconnect();
                }
            },

            getStats() {
                if (!this.longTasks.length) return null;
                const count = this.longTasks.length;
                const avgDuration = this.longTasks.reduce((a, b) => a + b.duration, 0) / count;
                const maxDuration = Math.max(...this.longTasks.map(t => t.duration));
                return { count, avgDuration: avgDuration.toFixed(1), maxDuration: maxDuration.toFixed(1) };
            }
        },

        // ===== Web Vitals Monitor =====
        WebVitals: {
            metrics: {},

            init() {
                this.observeFCP();
                this.observeLCP();
                this.observeCLS();
                this.observeFID();
            },

            observeFCP() {
                if (!('PerformanceObserver' in window)) return;
                new PerformanceObserver(list => {
                    for (const entry of list.getEntries()) {
                        if (entry.name === 'first-contentful-paint') {
                            this.metrics.fcp = entry.startTime;
                            console.log('[WebVitals] FCP:', entry.startTime.toFixed(0) + 'ms');
                        }
                    }
                }).observe({ entryTypes: ['paint'] });
            },

            observeLCP() {
                if (!('PerformanceObserver' in window) || !window.LargestContentfulPaint) return;
                new PerformanceObserver(list => {
                    const entries = list.getEntries();
                    const lastEntry = entries[entries.length - 1];
                    this.metrics.lcp = lastEntry.startTime;
                    console.log('[WebVitals] LCP:', lastEntry.startTime.toFixed(0) + 'ms');
                }).observe({ entryTypes: ['largest-contentful-paint'] });
            },

            observeCLS() {
                if (!('PerformanceObserver' in window)) return;
                let clsValue = 0;
                new PerformanceObserver(list => {
                    for (const entry of list.getEntries()) {
                        if (!entry.hadRecentInput) {
                            clsValue += entry.value;
                        }
                    }
                    this.metrics.cls = clsValue;
                }).observe({ entryTypes: ['layout-shift'] });
            },

            observeFID() {
                if (!('PerformanceObserver' in window) || !window.PerformanceEventTiming) return;
                new PerformanceObserver(list => {
                    for (const entry of list.getEntries()) {
                        this.metrics.fid = entry.processingStart - entry.startTime;
                        console.log('[WebVitals] FID:', this.metrics.fid.toFixed(0) + 'ms');
                    }
                }).observe({ entryTypes: ['first-input'] });
            },

            getMetrics() {
                return { ...this.metrics };
            }
        },

        // ===== FPS Monitor =====
        FPSMonitor: {
            frames: [],
            isRunning: false,
            rafId: null,

            start() {
                if (this.isRunning) return;
                this.isRunning = true;
                this.frames = [];
                this.lastTime = performance.now();
                this.measure();
            },

            measure() {
                this.rafId = requestAnimationFrame((time) => {
                    if (!this.isRunning) return;

                    const delta = time - this.lastTime;
                    this.lastTime = time;

                    this.frames.push({ time, delta });

                    if (this.frames.length > 120) {
                        this.frames.shift();
                    }

                    this.measure();
                });
            },

            stop() {
                this.isRunning = false;
                cancelAnimationFrame(this.rafId);
            },

            getFPS() {
                if (this.frames.length < 2) return 0;
                const avgDelta = this.frames.reduce((a, b) => a + b.delta, 0) / this.frames.length;
                return Math.round(1000 / avgDelta);
            },

            getStats() {
                if (this.frames.length < 20) return null;
                const recent = this.frames.slice(-30);
                const avgDelta = recent.reduce((a, b) => a + b.delta, 0) / recent.length;
                const minDelta = Math.min(...recent.map(f => f.delta));
                const maxDelta = Math.max(...recent.map(f => f.delta));
                return {
                    fps: Math.round(1000 / avgDelta),
                    minFPS: Math.round(1000 / maxDelta),
                    maxFPS: Math.round(1000 / minDelta),
                    drops: recent.filter(f => f.delta > 20).length
                };
            }
        },

        // ===== Initialize All Monitors =====
        initAll() {
            this.WebVitals.init();
            this.MemoryMonitor.start();
            this.LongTaskMonitor.start();
            this.FPSMonitor.start();

            // Log initial performance
            window.addEventListener('load', () => {
                setTimeout(() => {
                    const perfData = performance.getEntriesByType('navigation')[0];
                    if (perfData) {
                        console.log('[Performance] Page Load:', {
                            TTFB: perfData.responseStart.toFixed(0) + 'ms',
                            DOMReady: perfData.domContentLoadedEventEnd.toFixed(0) + 'ms',
                            LoadComplete: perfData.loadEventEnd.toFixed(0) + 'ms'
                        });
                    }
                }, 0);
            });
        },

        // ===== Cleanup All Monitors =====
        cleanupAll() {
            this.MemoryMonitor.stop();
            this.LongTaskMonitor.stop();
            this.FPSMonitor.stop();
        }
    };

    // Auto-initialize on DOM ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', () => {
            window.DashboardPerformance.initAll();
        });
    } else {
        window.DashboardPerformance.initAll();
    }
})();
