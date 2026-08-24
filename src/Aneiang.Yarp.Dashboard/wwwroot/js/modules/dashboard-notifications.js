/**
 * Notifications management page - platform endpoints, event subscription
 * matrix, delivery tuning and history. See WebhookSettingsController API.
 */
(function () {
    'use strict';

    var PLATFORMS = [
        { key: 'dingtalk', icon: 'bi-chat-dots-fill',     color: '#0089FF', placeholder: 'https://oapi.dingtalk.com/robot/send?access_token=...' },
        { key: 'wechat',   icon: 'bi-wechat',             color: '#07C160', placeholder: 'https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=...' },
        { key: 'feishu',   icon: 'bi-lark',               color: '#3370FF', placeholder: 'https://open.feishu.cn/open-apis/bot/v2/hook/...' },
        { key: 'slack',    icon: 'bi-slack',              color: '#4A154B', placeholder: 'https://hooks.slack.com/services/...' },
        { key: 'discord',  icon: 'bi-discord',            color: '#5865F2', placeholder: 'https://discord.com/api/webhooks/...' },
        { key: 'telegram', icon: 'bi-telegram',           color: '#2AABEE', placeholder: 'https://api.telegram.org/bot<TOKEN>/sendMessage' },
        { key: 'teams',    icon: 'bi-microsoft-teams',    color: '#5B5FC7', placeholder: 'https://prod-XX.logic.azure.com/...' },
        { key: 'generic',  icon: 'bi-link-45deg',         color: '#6366f1', placeholder: 'https://example.com/webhook' }
    ];

    var EVENT_GROUPS = [
        { key: 'route',  icon: 'bi-signpost-split', color: '#3b82f6', events: ['AddRoute', 'UpdateRoute', 'RemoveRoute', 'SetRouteEnabled'] },
        { key: 'cluster', icon: 'bi-hdd-network',   color: '#8b5cf6', events: ['AddCluster', 'UpdateCluster', 'RemoveCluster'] },
        { key: 'config', icon: 'bi-gear', color: '#f59e0b', events: ['RollbackConfig'] },
        { key: 'alert',  icon: 'bi-exclamation-triangle', color: '#ef4444', events: ['CircuitBreakerOpen', 'RetryExhausted', 'WafBlock', 'ProxyError', 'RateLimitExceeded'] }
    ];

    var state = {
        platforms: {},        // platform -> [{ url, secret }]
        selectedPlatform: null, // platform key shown in the detail panel
        testPassed: {},       // platform -> true when the staged endpoint passed the inline test
        saveTimers: {},       // platform -> debounce timer for auto-saving secret edits
        platformEvents: {},   // platform -> [events] (explicit subscription)
        enabledEvents: [],    // global fallback (config-change events)
        cooldownSeconds: 60,
        retryCount: 2,
        timeoutSeconds: 10,
        loaded: false,
        dirty: false,
        history: [],          // loaded delivery records
        historyFilter: 'all'  // all | success | failed
    };

    function esc(s) {
        return window.DashboardUtils && window.DashboardUtils.escapeHtml ? window.DashboardUtils.escapeHtml(String(s || '')) : String(s || '');
    }

    function platformName(key) {
        return __('webhook.platform.' + key);
    }

    function platformMeta(key) {
        for (var i = 0; i < PLATFORMS.length; i++) {
            if (PLATFORMS[i].key === key) return PLATFORMS[i];
        }
        return null;
    }

    // Auto-save: every change is persisted in place (per-platform endpoint save,
    // immediate event-subscription save, debounced advanced-settings save).
    var autoSaveTimer = null;

    function autoSave(debounceMs) {
        if (debounceMs > 0) {
            if (autoSaveTimer) clearTimeout(autoSaveTimer);
            autoSaveTimer = setTimeout(function () {
                autoSaveTimer = null;
                collectAndSave().catch(showSaveError);
            }, debounceMs);
        } else {
            if (autoSaveTimer) { clearTimeout(autoSaveTimer); autoSaveTimer = null; }
            return collectAndSave().catch(showSaveError);
        }
    }

    function showSaveError(e) {
        window.DashboardModals.showError(__('webhook.saveError') + ': ' + e.message);
    }

    // ─── KPI cards ──────────────────────────────────────────────────────────

    function computeKpis() {
        var configuredPlatforms = 0;
        var totalEndpoints = 0;
        var subscribedEventSet = {};
        PLATFORMS.forEach(function (pd) {
            var eps = state.platforms[pd.key] || [];
            if (eps.length > 0) configuredPlatforms++;
            totalEndpoints += eps.length;
            subscribedEvents(pd.key).forEach(function (ev) { subscribedEventSet[ev] = true; });
        });

        setText('notif-kpi-platforms', configuredPlatforms + ' / ' + PLATFORMS.length);
        setText('notif-kpi-platforms-sub', __('notif.kpi.platforms.sub') + ' ' + (PLATFORMS.length - configuredPlatforms));
        setText('notif-kpi-endpoints', String(totalEndpoints));
        setText('notif-kpi-endpoints-sub', configuredPlatforms > 0 ? __('notif.kpi.endpoints.sub') : __('notif.kpi.noData'));

        var eventCount = Object.keys(subscribedEventSet).length;
        setText('notif-kpi-events', String(eventCount));
        var explicitCount = PLATFORMS.filter(function (pd) { return state.platformEvents.hasOwnProperty(pd.key); }).length;
        setText('notif-kpi-events-sub', explicitCount > 0
            ? explicitCount + ' ' + __('notif.events.explicitCount')
            : __('notif.events.followGlobal'));

        var records = state.history || [];
        if (records.length > 0) {
            var ok = records.filter(function (r) { return r.success; }).length;
            var rate = Math.round(ok * 1000 / records.length) / 10;
            setText('notif-kpi-rate', rate + '%');
            setText('notif-kpi-rate-sub', records.length + ' ' + __('notif.kpi.rate.sub') + ' · OK ' + ok);
        } else {
            setText('notif-kpi-rate', '-');
            setText('notif-kpi-rate-sub', __('notif.kpi.noData'));
        }
    }

    function setText(id, value) {
        var el = document.getElementById(id);
        if (el) el.textContent = value;
    }

    function loadKpiHistory() {
        return window.DashboardApi.endpoints.getWebhookHistory(200).then(function (records) {
            state.history = Array.isArray(records) ? records : [];
            computeKpis();
        }).catch(function () { /* KPI stays at defaults */ });
    }

    // ─── Platform configuration (master-detail layout) ─────────────────────

    function renderPlatformCards() {
        var container = document.getElementById('notif-platform-cards');
        if (!state.selectedPlatform || !platformMeta(state.selectedPlatform)) {
            var firstConfigured = null;
            PLATFORMS.forEach(function (pd) {
                if (!firstConfigured && (state.platforms[pd.key] || []).length > 0) firstConfigured = pd;
            });
            state.selectedPlatform = firstConfigured ? firstConfigured.key : PLATFORMS[0].key;
        }

        container.innerHTML = renderPlatformNav() + renderPlatformDetail();
        updateSummary();
        computeKpis();
    }

    function renderPlatformNav() {
        var html = '<div class="notif-platform-nav">';
        PLATFORMS.forEach(function (pd) {
            var endpoints = state.platforms[pd.key] || [];
            var active = state.selectedPlatform === pd.key;
            html += `
            <button type="button" class="notif-platform-nav-item${active ? ' active' : ''}" data-platform="${pd.key}" title="${esc(platformName(pd.key))}">
                <span class="notif-platform-icon" style="background:${pd.color}1a;color:${pd.color};"><i class="bi ${pd.icon}"></i></span>
                <span class="notif-platform-nav-name">${esc(platformName(pd.key))}</span>
                <span class="notif-nav-count${endpoints.length === 0 ? ' notif-nav-count--zero' : ''}" title="${esc(__('notif.kpi.endpoints'))}">${endpoints.length}</span>
            </button>`;
        });
        return html + '</div>';
    }

    function renderPlatformDetail() {
        var pd = platformMeta(state.selectedPlatform) || PLATFORMS[0];
        var endpoints = state.platforms[pd.key] || [];
        var subscribedCount = subscribedEvents(pd.key).length;
        var configured = endpoints.length > 0;

        var listHtml = '';
        if (endpoints.length === 0) {
            listHtml = '<div class="text-center py-3 text-muted" style="font-size:12px;border:1px dashed #e2e8f0;border-radius:8px;">' +
                '<i class="bi bi-inbox d-block mb-1" style="font-size:18px;opacity:.5;"></i>' +
                esc(__('webhook.noEndpoints')) + '</div>';
        } else {
            endpoints.forEach(function (ep, idx) {
                listHtml += renderEndpointRow(pd, ep, idx);
            });
        }

        return `
        <div class="notif-platform-detail">
            <div class="notif-platform-card" data-platform="${pd.key}">
                <div class="notif-platform-head" style="border-left:4px solid ${pd.color};">
                    <span class="notif-platform-icon" style="background:${pd.color}1a;color:${pd.color};"><i class="bi ${pd.icon}"></i></span>
                    <div class="flex-grow-1" style="min-width:0;">
                        <div class="fw-semibold" style="font-size:14px;color:#1e293b;">${esc(platformName(pd.key))}</div>
                        <small style="color:#64748b;font-size:12px;">${esc(__('webhook.' + pd.key + '.help'))}</small>
                    </div>
                    <span class="notif-platform-badges">
                        <span class="notif-status-dot${configured ? ' notif-status-dot--on' : ''}" title="${configured ? esc(__('notif.platforms.connected')) : esc(__('webhook.noEndpoints'))}"></span>
                        <span class="badge text-bg-light" style="font-size:11px;">${endpoints.length} · ${subscribedCount} ${esc(__('notif.events.unit') || '')}</span>
                    </span>
                </div>
                <div class="notif-endpoint-list" data-list="${pd.key}">${listHtml}</div>
                <div class="notif-add-row">
                    <input type="url" class="form-control form-control-sm notif-new-url" data-platform="${pd.key}" placeholder="${pd.placeholder}">
                    <input type="text" class="form-control form-control-sm notif-new-secret" data-platform="${pd.key}" placeholder="${esc(__('webhook.secret.placeholder.' + pd.key))}">
                    <button type="button" class="btn btn-sm btn-outline-primary notif-test-btn" data-platform="${pd.key}" title="${esc(__('notif.test'))}">
                        <i class="bi bi-send"></i>
                    </button>
                </div>
                <div class="notif-test-row">
                    <button type="button" class="btn btn-sm btn-success notif-save-btn" data-platform="${pd.key}"
                        ${state.testPassed[pd.key] ? '' : 'disabled'} title="${esc(state.testPassed[pd.key] ? __('notif.platform.save') : __('notif.platform.saveDisabled'))}">
                        <i class="bi bi-check-lg me-1"></i>${esc(__('notif.platform.save'))}
                    </button>
                    <span class="notif-test-result flex-grow-1" data-result="${pd.key}"></span>
                </div>
            </div>
        </div>`;
    }

    function renderEndpointRow(pd, ep, idx) {
        var displayUrl = ep.url.length > 60 ? ep.url.substring(0, 60) + '...' : ep.url;
        return `
        <div class="notif-endpoint-item" data-platform="${pd.key}" data-index="${idx}">
            <div class="notif-endpoint-url-row">
                <i class="bi bi-robot" style="color:${pd.color};font-size:14px;"></i>
                <span class="flex-grow-1 text-truncate" style="font-size:12px;font-family:monospace;color:#334155;" title="${esc(ep.url)}">${esc(displayUrl)}</span>
                <button type="button" class="btn btn-sm btn-link text-danger p-0 notif-remove-btn" data-platform="${pd.key}" data-index="${idx}" title="${esc(__('webhook.removeEndpoint'))}">
                    <i class="bi bi-trash3" style="font-size:13px;"></i>
                </button>
            </div>
            <div class="notif-endpoint-secret-row">
                <i class="bi bi-key" style="color:#94a3b8;font-size:11px;"></i>
                <input type="password" class="form-control form-control-sm notif-secret-input" data-platform="${pd.key}" data-index="${idx}"
                    value="${esc(ep.secret || '')}" placeholder="${esc(__('webhook.secret.placeholder.' + pd.key))}"
                    autocomplete="off" style="font-size:12px;font-family:monospace;padding:3px 8px;">
                <button type="button" class="notif-secret-toggle" data-platform="${pd.key}" data-index="${idx}" title="${esc(__('webhook.secret.show'))}">
                    <i class="bi bi-eye"></i>
                </button>
            </div>
        </div>`;
    }

    function updateSummary() {
        var el = document.getElementById('notif-platforms-summary');
        if (!el) return;
        var configured = PLATFORMS.filter(function (pd) { return (state.platforms[pd.key] || []).length > 0; }).length;
        el.textContent = __('notif.platforms.connected') + ' ' + configured + ' / ' + PLATFORMS.length;
    }

    // ─── Event matrix ───────────────────────────────────────────────────────

    function subscribedEvents(platform) {
        // Explicit subscription wins; otherwise fall back to global list.
        if (state.platformEvents.hasOwnProperty(platform)) return state.platformEvents[platform];
        return state.enabledEvents;
    }

    function renderEventMatrix() {
        var headRow = document.getElementById('notif-events-head-row');
        var body = document.getElementById('notif-events-body');

        var headHtml = '<th>' + esc(__('notif.events.eventColumn')) + '</th>';
        PLATFORMS.forEach(function (pd) {
            var inactive = (state.platforms[pd.key] || []).length === 0 ? ' notif-col-inactive' : '';
            var followDot = state.platformEvents.hasOwnProperty(pd.key) ? '' : '<span class="notif-follow-dot" title="' + esc(__('notif.events.followGlobal')) + '"></span>';
            headHtml += `<th class="text-center notif-matrix-head${inactive}" title="${esc(platformName(pd.key))}">` +
                `<i class="bi ${pd.icon}" style="color:${pd.color};font-size:14px;"></i>${followDot}</th>`;
        });
        headRow.innerHTML = headHtml;

        var bodyHtml = '';
        EVENT_GROUPS.forEach(function (group) {
            bodyHtml += `<tr class="notif-group-row"><td colspan="${PLATFORMS.length + 1}">` +
                `<i class="bi ${group.icon} me-1" style="color:${group.color};"></i>` +
                esc(__('notif.events.group.' + group.key)) + `</td></tr>`;

            group.events.forEach(function (eventKey) {
                var eventName = esc(__('webhook.events.' + eventKey));
                bodyHtml += `<tr><td style="font-size:13px;color:#334155;">${eventName}</td>`;
                PLATFORMS.forEach(function (pd) {
                    var inactive = (state.platforms[pd.key] || []).length === 0 ? ' notif-col-inactive' : '';
                    var checked = subscribedEvents(pd.key).indexOf(eventKey) >= 0 ? 'checked' : '';
                    bodyHtml += `<td class="text-center${inactive}">` +
                        `<input type="checkbox" class="form-check-input notif-event-cb" data-platform="${pd.key}" data-event="${eventKey}" ${checked} style="accent-color:#6366f1;">` +
                        `</td>`;
                });
                bodyHtml += '</tr>';
            });
        });
        body.innerHTML = bodyHtml;

        updateEventsMode();
    }

    function updateEventsMode() {
        var el = document.getElementById('notif-events-mode');
        if (!el) return;
        var explicit = PLATFORMS.filter(function (pd) { return state.platformEvents.hasOwnProperty(pd.key); }).length;
        el.textContent = explicit + ' / ' + PLATFORMS.length + ' ' + __('notif.events.explicitCount');
    }

    // ─── Load / save ────────────────────────────────────────────────────────

    function loadSettings() {
        return window.DashboardApi.endpoints.getWebhookSettings().then(function (data) {
            state.platforms = {};
            PLATFORMS.forEach(function (pd) {
                state.platforms[pd.key] = (data[pd.key] || []).map(function (ep) {
                    return { url: ep.url, secret: ep.secret || '' };
                });
            });
            // Preserve unknown custom platform keys so they aren't wiped on save.
            Object.keys(data).forEach(function (key) {
                if (Array.isArray(data[key]) && key.indexOf('-') < 0 && !state.platforms.hasOwnProperty(key)
                    && ['enabledEvents', 'platformEvents', 'cooldownSeconds', 'retryCount', 'timeoutSeconds'].indexOf(key) < 0) {
                    var isKnown = PLATFORMS.some(function (pd) { return pd.key === key; });
                    if (!isKnown && data[key].length > 0 && data[key][0] && data[key][0].url) {
                        state.platforms[key] = data[key].map(function (ep) { return { url: ep.url, secret: ep.secret || '' }; });
                    }
                }
            });

            state.platformEvents = data.platformEvents || {};
            state.enabledEvents = data.enabledEvents || [];
            state.cooldownSeconds = data.cooldownSeconds != null ? data.cooldownSeconds : 60;
            state.retryCount = data.retryCount != null ? data.retryCount : 2;
            state.timeoutSeconds = data.timeoutSeconds != null ? data.timeoutSeconds : 10;
            state.loaded = true;
            state.testPassed = {};

            renderPlatformCards();
            renderEventMatrix();
            setAdvancedInputs();
            computeKpis();
        });
    }

    function setAdvancedInputs() {
        document.getElementById('notif-cooldown').value = state.cooldownSeconds;
        document.getElementById('notif-retry').value = state.retryCount;
        document.getElementById('notif-timeout').value = state.timeoutSeconds;
    }

    function collectAndSave() {
        readAdvancedInputs();

        var platforms = {};
        Object.keys(state.platforms).forEach(function (key) {
            var endpoints = state.platforms[key]
                .map(function (ep) { return { url: ep.url, secret: ep.secret || null }; })
                .filter(function (ep) { return ep.url && ep.url.trim(); });
            if (endpoints.length > 0) platforms[key] = { endpoints: endpoints };
        });

        // Explicit subscriptions are written for every known platform with endpoints.
        var platformEvents = {};
        PLATFORMS.forEach(function (pd) {
            if ((platforms[pd.key] || { endpoints: [] }).endpoints.length > 0) {
                platformEvents[pd.key] = subscribedEvents(pd.key).slice();
            }
        });
        // Global fallback keeps legacy behavior for unknown/custom platforms.
        state.enabledEvents = Array.isArray(state.enabledEvents) ? state.enabledEvents : [];

        return window.DashboardApi.endpoints.saveWebhookSettings({
            platforms: platforms,
            enabledEvents: state.enabledEvents,
            platformEvents: platformEvents,
            cooldownSeconds: state.cooldownSeconds,
            retryCount: state.retryCount,
            timeoutSeconds: state.timeoutSeconds
        });
    }

    function readAdvancedInputs() {
        var cd = parseInt(document.getElementById('notif-cooldown').value, 10);
        var rt = parseInt(document.getElementById('notif-retry').value, 10);
        var to = parseInt(document.getElementById('notif-timeout').value, 10);
        if (!isNaN(cd)) state.cooldownSeconds = Math.max(0, Math.min(3600, cd));
        if (!isNaN(rt)) state.retryCount = Math.max(0, Math.min(5, rt));
        if (!isNaN(to)) state.timeoutSeconds = Math.max(3, Math.min(60, to));
    }

    // ─── Delivery history ───────────────────────────────────────────────────

    function loadHistory() {
        var body = document.getElementById('notif-history-body');
        body.innerHTML = '<tr><td colspan="7" class="text-center text-muted py-4"><span class="spinner-border spinner-border-sm me-2"></span>' + esc(__('common.loading')) + '</td></tr>';

        window.DashboardApi.endpoints.getWebhookHistory(200).then(function (records) {
            state.history = Array.isArray(records) ? records : [];
            renderHistory();
            computeKpis();
        }).catch(function (e) {
            body.innerHTML = '<tr><td colspan="7" class="text-center text-danger py-4">' + esc(e.message) + '</td></tr>';
        });
    }

    function renderHistory() {
        var body = document.getElementById('notif-history-body');
        var records = state.history;

        var summary = document.getElementById('notif-history-summary');
        if (summary) {
            var ok = records.filter(function (r) { return r.success; }).length;
            summary.textContent = records.length + ' · OK ' + ok;
        }

        if (state.historyFilter === 'success') records = records.filter(function (r) { return r.success; });
        else if (state.historyFilter === 'failed') records = records.filter(function (r) { return !r.success; });

        if (records.length === 0) {
            body.innerHTML = '<tr><td colspan="7" class="text-center text-muted py-4">' + esc(__('notif.history.empty')) + '</td></tr>';
            return;
        }

        body.innerHTML = records.map(function (r) {
            var meta = platformMeta(r.platform);
            var statusBadge = r.success
                ? '<span class="badge text-bg-success">OK</span>'
                : '<span class="badge text-bg-danger">' + esc(r.httpStatus || 'ERR') + '</span>';
            var platformCell = meta
                ? `<i class="bi ${meta.icon} me-1" style="color:${meta.color};"></i>`
                : '<i class="bi bi-robot me-1 text-muted"></i>';
            platformCell += esc(platformName(r.platform) !== 'webhook.platform.' + r.platform ? platformName(r.platform) : r.platform);
            return `<tr>
                <td style="font-size:12px;white-space:nowrap;">${esc(r.createdAt)}</td>
                <td style="font-size:12px;white-space:nowrap;">${platformCell}</td>
                <td style="font-size:12px;">${esc(r.eventType)}</td>
                <td style="font-size:12px;max-width:160px;" class="text-truncate">${esc(r.target || '-')}</td>
                <td>${statusBadge}</td>
                <td style="font-size:12px;white-space:nowrap;">${r.durationMs} ms${r.attempts && r.attempts > 1 ? ' (' + r.attempts + 'x)' : ''}</td>
                <td style="font-size:12px;max-width:220px;" class="text-truncate" title="${esc(r.error || '')}">${esc(r.error || '-')}</td>
            </tr>`;
        }).join('');
    }

    function setHistoryFilter(filter, btn) {
        state.historyFilter = filter;
        document.querySelectorAll('#notif-history-filters .btn').forEach(function (b) {
            b.classList.remove('active');
        });
        if (btn) btn.classList.add('active');
        renderHistory();
    }

    // ─── Bindings ───────────────────────────────────────────────────────────

    function bind() {
        // Endpoint save / remove / test / secret toggle / platform nav
        document.getElementById('notif-platform-cards').addEventListener('click', function (e) {
            var navItem = e.target.closest('.notif-platform-nav-item');
            if (navItem) {
                if (navItem.dataset.platform !== state.selectedPlatform) {
                    state.selectedPlatform = navItem.dataset.platform;
                    renderPlatformCards();
                }
                return;
            }

            var saveBtn = e.target.closest('.notif-save-btn');
            if (saveBtn) {
                saveStagedEndpoint(saveBtn.dataset.platform, saveBtn);
                return;
            }

            var removeBtn = e.target.closest('.notif-remove-btn');
            if (removeBtn) {
                var p = removeBtn.dataset.platform;
                var i = parseInt(removeBtn.dataset.index, 10);
                var removed = state.platforms[p].splice(i, 1)[0];
                renderPlatformCards();
                renderEventMatrix();
                persistPlatform(p).catch(function (err) {
                    // Roll back the removal so UI matches persisted state.
                    if (removed) state.platforms[p].splice(i, 0, removed);
                    renderPlatformCards();
                    renderEventMatrix();
                    window.DashboardModals.showError(__('notif.saveFailed') + ': ' + err.message);
                });
                return;
            }

            var testBtn = e.target.closest('.notif-test-btn');
            if (testBtn) {
                testStagedEndpoint(testBtn.dataset.platform, testBtn);
                return;
            }

            var secretToggle = e.target.closest('.notif-secret-toggle');
            if (secretToggle) {
                var input = secretToggle.parentElement.querySelector('.notif-secret-input');
                var icon = secretToggle.querySelector('i');
                if (input.type === 'password') {
                    input.type = 'text';
                    icon.className = 'bi bi-eye-slash';
                    secretToggle.title = __('webhook.secret.hide');
                } else {
                    input.type = 'password';
                    icon.className = 'bi bi-eye';
                    secretToggle.title = __('webhook.secret.show');
                }
                return;
            }
        });

        // Endpoint secret edit → debounced per-platform auto-save
        document.getElementById('notif-platform-cards').addEventListener('input', function (e) {
            var input = e.target.closest('.notif-secret-input');
            if (input) {
                state.platforms[input.dataset.platform][parseInt(input.dataset.index, 10)].secret = input.value;
                schedulePlatformSave(input.dataset.platform);
                return;
            }

            // Any edit to the staged endpoint invalidates a previous test pass.
            var staged = e.target.closest('.notif-new-url, .notif-new-secret');
            if (staged) {
                var platform = staged.dataset.platform;
                if (state.testPassed[platform]) {
                    state.testPassed[platform] = false;
                    var card = staged.closest('.notif-platform-card');
                    var saveBtn = card && card.querySelector('.notif-save-btn');
                    if (saveBtn) {
                        saveBtn.disabled = true;
                        saveBtn.title = __('notif.platform.saveDisabled');
                    }
                }
            }
        });

        // Enter in the "new endpoint URL" input triggers the inline test
        document.getElementById('notif-platform-cards').addEventListener('keydown', function (e) {
            if (e.key !== 'Enter') return;
            var urlInput = e.target.closest('.notif-new-url, .notif-new-secret');
            if (!urlInput) return;
            e.preventDefault();
            var card = urlInput.closest('.notif-platform-card');
            var testBtn = card && card.querySelector('.notif-test-btn');
            if (testBtn) testBtn.click();
        });

        // Event matrix toggles
        document.getElementById('notif-events-body').addEventListener('change', function (e) {
            var cb = e.target.closest('.notif-event-cb');
            if (!cb) return;
            var platform = cb.dataset.platform;
            var event = cb.dataset.event;

            // Materialize explicit subscription list on first interaction.
            if (!state.platformEvents.hasOwnProperty(platform)) {
                state.platformEvents[platform] = state.enabledEvents.slice();
            }
            var list = state.platformEvents[platform];
            var idx = list.indexOf(event);
            if (cb.checked && idx < 0) list.push(event);
            if (!cb.checked && idx >= 0) list.splice(idx, 1);

            updateEventsMode();
            renderPlatformCards();
            renderEventMatrix();
            autoSave(0);
        });

        // Follow-global reset
        document.getElementById('notif-events-default-btn').addEventListener('click', function () {
            state.platformEvents = {};
            renderEventMatrix();
            renderPlatformCards();
            autoSave(0);
            window.DashboardModals.showInfo(__('notif.events.followGlobalDone'));
        });

        // Advanced settings → debounced auto-save
        ['notif-cooldown', 'notif-retry', 'notif-timeout'].forEach(function (id) {
            document.getElementById(id).addEventListener('input', function () { autoSave(800); });
        });

        // History tab lazy load
        document.querySelector('#tab-notif-history').addEventListener('shown.bs.tab', loadHistory);
        document.getElementById('notif-history-refresh-btn').addEventListener('click', loadHistory);
        document.getElementById('notif-history-clear-btn').addEventListener('click', function () {
            window.DashboardModals.showConfirm(__('notif.history.clearConfirm'), async function () {
                try {
                    await window.DashboardApi.endpoints.clearWebhookHistory();
                    loadHistory();
                    window.DashboardModals.showSuccess(__('notif.history.cleared'));
                } catch (e) {
                    window.DashboardModals.showError(e.message);
                }
            }, null, { danger: true });
        });

        // History filters
        document.querySelectorAll('#notif-history-filters .btn').forEach(function (btn) {
            btn.addEventListener('click', function () {
                setHistoryFilter(btn.dataset.filter, btn);
            });
        });

        // Clear cooldown
        document.getElementById('notif-clear-cooldown-btn').addEventListener('click', async function () {
            try {
                await window.DashboardApi.endpoints.clearWebhookCooldown();
                window.DashboardModals.showSuccess(__('notif.advanced.cooldownCleared'));
            } catch (e) {
                window.DashboardModals.showError(e.message);
            }
        });
    }

    // ─── Inline test / per-platform save ────────────────────────────────────

    function setResult(platform, html) {
        var resultEl = document.querySelector('[data-result="' + platform + '"]');
        if (resultEl) resultEl.innerHTML = html;
    }

    function setSaveEnabled(platform, enabled) {
        var saveBtn = document.querySelector('.notif-save-btn[data-platform="' + platform + '"]');
        if (saveBtn) {
            saveBtn.disabled = !enabled;
            saveBtn.title = enabled ? __('notif.platform.save') : __('notif.platform.saveDisabled');
        }
    }

    // Tests ONLY the endpoint currently being staged in the add row.
    // Saving is unlocked only after this test passes.
    function testStagedEndpoint(platform, btn) {
        var card = btn.closest('.notif-platform-card');
        var urlInput = card.querySelector('.notif-new-url');
        var secretInput = card.querySelector('.notif-new-secret');
        var url = urlInput ? urlInput.value.trim() : '';
        var secret = secretInput ? secretInput.value.trim() : '';

        if (!url) {
            setResult(platform, '<i class="bi bi-exclamation-circle-fill text-warning"></i><span class="ms-1" style="color:#b45309;">' + esc(__('webhook.urlRequired')) + '</span>');
            return;
        }
        try { new URL(url); } catch (err) {
            setResult(platform, '<i class="bi bi-exclamation-circle-fill text-warning"></i><span class="ms-1" style="color:#b45309;">' + esc(__('webhook.urlRequired')) + '</span>');
            return;
        }
        var exists = (state.platforms[platform] || []).some(function (ep) { return ep.url === url; });
        if (exists) {
            setResult(platform, '<i class="bi bi-exclamation-circle-fill text-warning"></i><span class="ms-1" style="color:#b45309;">' + esc(__('webhook.urlExists')) + '</span>');
            return;
        }

        state.testPassed[platform] = false;
        setSaveEnabled(platform, false);
        setResult(platform, '<span class="spinner-border spinner-border-sm"></span><span class="ms-1 text-muted">' + esc(__('webhook.testing')) + '</span>');
        btn.disabled = true;

        window.DashboardApi.endpoints.testWebhook({
            platform: platform,
            endpoints: [{ url: url, secret: secret || null }]
        }).then(function (resp) {
            var ok = !!(resp && resp.success);
            state.testPassed[platform] = ok;
            setSaveEnabled(platform, ok);
            if (ok) {
                setResult(platform, '<i class="bi bi-check-circle-fill text-success"></i><span class="ms-1" style="color:#166534;">' +
                    esc(__('webhook.testSuccess')) + ' · ' + esc(__('notif.platform.saveReady')) + '</span>');
            } else {
                setResult(platform, '<i class="bi bi-x-circle-fill text-danger"></i><span class="ms-1" style="color:#dc2626;">' +
                    esc(__('webhook.testFailed')) + (resp && resp.total > 0 ? ' (' + resp.succeeded + '/' + resp.total + ')' : '') + '</span>');
            }
        }).catch(function (e) {
            state.testPassed[platform] = false;
            setSaveEnabled(platform, false);
            setResult(platform, '<i class="bi bi-x-circle-fill text-danger"></i><span class="ms-1" style="color:#dc2626;">' + esc(e.message) + '</span>');
        }).finally(function () {
            btn.disabled = false;
        });
    }

    // Persists the full endpoint list of one platform (per-platform save).
    function persistPlatform(platform) {
        var endpoints = (state.platforms[platform] || [])
            .map(function (ep) { return { url: ep.url, secret: ep.secret || null }; })
            .filter(function (ep) { return ep.url && ep.url.trim(); });
        return window.DashboardApi.endpoints.saveWebhookPlatform({
            platform: platform,
            endpoints: endpoints
        });
    }

    // Debounced auto-save for secret edits of already-saved endpoints.
    function schedulePlatformSave(platform) {
        if (state.saveTimers[platform]) clearTimeout(state.saveTimers[platform]);
        state.saveTimers[platform] = setTimeout(function () {
            state.saveTimers[platform] = null;
            persistPlatform(platform).catch(function (e) {
                window.DashboardModals.showError(__('notif.saveFailed') + ': ' + e.message);
            });
        }, 800);
    }

    // Adds the tested endpoint and persists that platform immediately.
    function saveStagedEndpoint(platform, btn) {
        if (!state.testPassed[platform]) return;

        var card = btn.closest('.notif-platform-card');
        var urlInput = card.querySelector('.notif-new-url');
        var secretInput = card.querySelector('.notif-new-secret');
        var url = urlInput ? urlInput.value.trim() : '';
        var secret = secretInput ? secretInput.value.trim() : '';

        if (!url) { state.testPassed[platform] = false; setSaveEnabled(platform, false); return; }
        var exists = (state.platforms[platform] || []).some(function (ep) { return ep.url === url; });
        if (exists) { window.DashboardModals.showWarning(__('webhook.urlExists')); return; }

        btn.disabled = true;
        btn.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>' + esc(__('webhook.saving'));

        state.platforms[platform].push({ url: url, secret: secret });

        persistPlatform(platform).then(function () {
            state.testPassed[platform] = false;
            if (urlInput) urlInput.value = '';
            if (secretInput) secretInput.value = '';
            renderPlatformCards();
            renderEventMatrix();
            setResult(platform, '<i class="bi bi-check-circle-fill text-success"></i><span class="ms-1" style="color:#166534;">' + esc(__('notif.platform.saved')) + '</span>');
        }).catch(function (e) {
            // Roll back the staged add so UI matches persisted state.
            var idx = state.platforms[platform].findIndex(function (ep) { return ep.url === url; });
            if (idx >= 0) state.platforms[platform].splice(idx, 1);
            renderPlatformCards();
            renderEventMatrix();
            setResult(platform, '<i class="bi bi-x-circle-fill text-danger"></i><span class="ms-1" style="color:#dc2626;">' + esc(__('notif.saveFailed') + ': ' + e.message) + '</span>');
        });
    }

    // ─── Init ───────────────────────────────────────────────────────────────

    document.addEventListener('DOMContentLoaded', function () {
        if (!document.getElementById('notifications-app')) return;
        bind();
        loadSettings().then(loadKpiHistory).catch(function (e) {
            window.DashboardModals.showError(__('webhook.loadFailed') + ': ' + e.message);
        });
    });
})();
