/**
 * AI Assistant — floating chat panel with SSE streaming, sessions, and Function Calling.
 * Features: reasoning display, rich markdown, collapsible tools, copy/regenerate.
 */
(function() {
    'use strict';

    const state = {
        panelOpen: false,
        sessionId: null,
        isStreaming: false,
        sessions: [],
        historyVisible: false,
        abortController: null,
        lastUserMessage: null
    };

    // Active reasoning block element (streamed thinking text)
    let reasoningEl = null;

    // Use global __() defined in layout; fallback to key
    const __ = (typeof window.__ === 'function') ? window.__ : (key) => key;

    // Preset prompt templates shown as quick actions on the welcome screen
    const QUICK_PROMPTS = {
        list_routes: () => __('ai.prompt.listRoutes') || 'List all routes',
        list_clusters: () => __('ai.prompt.listClusters') || 'List all clusters',
        health: () => __('ai.prompt.health') || 'Show cluster health status',
        traffic: () => __('ai.prompt.traffic') || 'Show traffic statistics',
        plugins: () => __('ai.prompt.plugins') || 'List all plugins',
        history: () => __('ai.prompt.history') || 'Show recent config history',
        waf: () => __('ai.prompt.waf') || 'Show WAF configuration',
        trafficDetail: () => __('ai.prompt.trafficDetail') || 'Show detailed traffic stats for the last hour'
    };

    const QUICK_BTNS = `
        <div class="ai-quick-actions">
            <button class="ai-quick-btn" data-action="list_routes"><i class="bi bi-signpost"></i> ${__('ai.quick.routes') || 'Routes'}</button>
            <button class="ai-quick-btn" data-action="list_clusters"><i class="bi bi-diagram-3"></i> ${__('ai.quick.clusters') || 'Clusters'}</button>
            <button class="ai-quick-btn" data-action="health"><i class="bi bi-heart-pulse"></i> ${__('ai.quick.health') || 'Health'}</button>
            <button class="ai-quick-btn" data-action="traffic"><i class="bi bi-graph-up"></i> ${__('ai.quick.traffic') || 'Traffic'}</button>
            <button class="ai-quick-btn" data-action="plugins"><i class="bi bi-puzzle"></i> ${__('ai.quick.plugins') || 'Plugins'}</button>
            <button class="ai-quick-btn" data-action="history"><i class="bi bi-clock-history"></i> ${__('ai.quick.history') || 'History'}</button>
            <button class="ai-quick-btn" data-action="waf"><i class="bi bi-shield-check"></i> ${__('ai.quick.waf') || 'WAF'}</button>
        </div>`;

    function init() {
        createFloatingButton();
        createChatPanel();
        loadConfig();
    }

    // ─── UI Creation ───

    function createFloatingButton() {
        const btn = document.createElement('button');
        btn.id = 'ai-float-btn';
        btn.className = 'ai-float-btn';
        btn.innerHTML = '<i class="bi bi-robot"></i><span class="ai-status-dot"></span>';
        btn.title = __('ai.btn.open') || 'AI Assistant';
        btn.addEventListener('click', togglePanel);
        document.body.appendChild(btn);
    }

    function createChatPanel() {
        const panel = document.createElement('div');
        panel.id = 'ai-chat-panel';
        panel.className = 'ai-chat-panel';
        panel.innerHTML = `
            <div class="ai-chat-header">
                <div class="ai-chat-title">
                    <i class="bi bi-robot"></i>
                    <span>${__('ai.welcome.title') || 'AI Assistant'}</span>
                </div>
                <div class="ai-chat-actions">
                    <button class="ai-icon-btn" id="ai-btn-history" title="${__('ai.btn.history') || 'History'}">
                        <i class="bi bi-clock-history"></i>
                    </button>
                    <button class="ai-icon-btn" id="ai-btn-max" title="${__('ai.btn.maximize') || 'Maximize'}">
                        <i class="bi bi-arrows-fullscreen"></i>
                    </button>
                    <button class="ai-icon-btn" id="ai-btn-new" title="${__('ai.btn.newChat') || 'New Chat'}">
                        <i class="bi bi-plus-lg"></i>
                    </button>
                    <button class="ai-icon-btn" id="ai-btn-close" title="${__('ai.btn.close') || 'Close'}">
                        <i class="bi bi-x-lg"></i>
                    </button>
                </div>
            </div>
            <div class="ai-chat-history" id="ai-history-list" style="display:none;">
                <div class="ai-history-header">${__('ai.history.title') || 'Chat History'}</div>
                <div class="ai-history-items" id="ai-history-items"></div>
            </div>
            <div class="ai-chat-body" id="ai-chat-body">
                <div class="ai-welcome">
                    <i class="bi bi-robot"></i>
                    <p>${__('ai.welcome.message') || 'Hello! I can help you manage the YARP gateway.'}</p>
                    ${QUICK_BTNS}
                </div>
            </div>
            <div class="ai-chat-footer">
                <textarea id="ai-chat-input" class="ai-chat-input" rows="1"
                    placeholder="${__('ai.input.placeholder') || 'Type a message...'}"></textarea>
                <button class="ai-send-btn" id="ai-btn-send">
                    <i class="bi bi-send"></i>
                </button>
            </div>
        `;
        document.body.appendChild(panel);

        // Event listeners
        document.getElementById('ai-btn-close').addEventListener('click', () => togglePanel(false));
        document.getElementById('ai-btn-new').addEventListener('click', newChat);
        document.getElementById('ai-btn-history').addEventListener('click', toggleHistory);
        document.getElementById('ai-btn-max').addEventListener('click', toggleMaximize);
        document.getElementById('ai-btn-send').addEventListener('click', sendCurrentMessage);
        const input = document.getElementById('ai-chat-input');
        input.addEventListener('keydown', (e) => {
            if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                sendCurrentMessage();
            }
        });
        input.addEventListener('input', autoResize);

        // Quick actions
        bindQuickActions(panel);
    }

    function bindQuickActions(container) {
        container.querySelectorAll('.ai-quick-btn').forEach(btn => {
            btn.addEventListener('click', () => {
                const action = btn.dataset.action;
                const prompt = (QUICK_PROMPTS[action] || (() => action))();
                sendMessage(prompt);
            });
        });
    }

    // ─── Panel Toggle ───

    function togglePanel(force) {
        const panel = document.getElementById('ai-chat-panel');
        const btn = document.getElementById('ai-float-btn');
        const open = force !== undefined ? force : !state.panelOpen;
        state.panelOpen = open;
        panel.classList.toggle('open', open);
        btn.classList.toggle('active', open);
        // Reset fullscreen when closing so the floating launcher reappears
        if (!open) setMaximized(false);
        if (open) {
            loadSessions();
            setTimeout(() => document.getElementById('ai-chat-input').focus(), 100);
        }
    }

    function setMaximized(isMax) {
        const panel = document.getElementById('ai-chat-panel');
        const maxBtn = document.getElementById('ai-btn-max');
        const floatBtn = document.getElementById('ai-float-btn');
        panel.classList.toggle('maximized', isMax);
        if (maxBtn) maxBtn.innerHTML = isMax
            ? '<i class="bi bi-fullscreen-exit"></i>'
            : '<i class="bi bi-arrows-fullscreen"></i>';
        // Hide the floating launcher while the panel is fullscreen
        if (floatBtn) floatBtn.style.display = isMax ? 'none' : '';
    }

    function toggleMaximize() {
        const panel = document.getElementById('ai-chat-panel');
        setMaximized(!panel.classList.contains('maximized'));
        const input = document.getElementById('ai-chat-input');
        if (input) input.focus();
    }

    // ─── Chat ───

    async function sendCurrentMessage() {
        const input = document.getElementById('ai-chat-input');
        const message = input.value.trim();
        if (!message || state.isStreaming) return;
        input.value = '';
        autoResize.call(input);
        await sendMessage(message);
    }

    async function sendMessage(message) {
        appendMessage('user', message);
        state.lastUserMessage = message;
        setStreaming(true);

        state.abortController = new AbortController();
        try {
            await streamChat(message);
        } catch (e) {
            if (e.name !== 'AbortError') {
                appendMessage('error', e.message);
            }
        } finally {
            setStreaming(false);
            state.abortController = null;
        }
    }

    // Regenerate the last assistant reply (keeps the user message in place)
    async function regenerate() {
        if (state.isStreaming) return;
        const body = document.getElementById('ai-chat-body');
        const userEls = body.querySelectorAll('.ai-msg-user');
        const lastUser = userEls[userEls.length - 1];
        if (!lastUser) return;
        const message = state.lastUserMessage;
        if (!message) return;

        // Remove everything rendered after the last user message
        let el = lastUser.nextSibling;
        while (el) { const n = el.nextSibling; el.remove(); el = n; }
        reasoningEl = null;

        setStreaming(true);
        state.abortController = new AbortController();
        try {
            await streamChat(message);
        } catch (e) {
            if (e.name !== 'AbortError') {
                appendMessage('error', e.message);
            }
        } finally {
            setStreaming(false);
            state.abortController = null;
        }
    }

    function copyToClipboard(text) {
        if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(text).then(() => {
                showToast(__('ai.copied') || 'Copied');
            }).catch(() => {});
        } else {
            // Fallback for older browsers
            const ta = document.createElement('textarea');
            ta.value = text;
            document.body.appendChild(ta);
            ta.select();
            try { document.execCommand('copy'); showToast(__('ai.copied') || 'Copied'); } catch {}
            document.body.removeChild(ta);
        }
    }

    function showToast(msg) {
        let toast = document.getElementById('ai-toast');
        if (!toast) {
            toast = document.createElement('div');
            toast.id = 'ai-toast';
            toast.className = 'ai-toast';
            document.body.appendChild(toast);
        }
        toast.textContent = msg;
        toast.classList.add('show');
        clearTimeout(toast._timer);
        toast._timer = setTimeout(() => toast.classList.remove('show'), 1600);
    }

    async function streamChat(message) {
        const url = (window.__dashboard?.basePath || '') + '/api/ai/chat';
        const response = await fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ sessionId: state.sessionId, message }),
            signal: state.abortController.signal
        });

        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        if (!response.body) throw new Error('Response body is null');

        const reader = response.body.getReader();
        const decoder = new TextDecoder();
        let buffer = '';
        let assistantMsg = null;

        while (true) {
            const { done, value } = await reader.read();
            if (done) break;
            buffer += decoder.decode(value, { stream: true });
            const lines = buffer.split('\n');
            buffer = lines.pop();

            for (const line of lines) {
                if (!line.startsWith('data: ')) continue;
                let data;
                try { data = JSON.parse(line.substring(6)); }
                catch { continue; }

                if (data.type === 'session') {
                    state.sessionId = data.sessionId;
                } else if (data.type === 'reasoning') {
                    appendReasoning(data.content || '');
                } else if (data.type === 'chunk') {
                    // Auto-collapse the thinking block once the final answer starts
                    if (reasoningEl && reasoningEl.classList.contains('has-content') && !reasoningEl.classList.contains('collapsed')) {
                        reasoningEl.classList.add('collapsed');
                    }
                    if (!assistantMsg) assistantMsg = appendMessage('assistant', '');
                    assistantMsg.appendContent(data.content);
                } else if (data.type === 'tool_call') {
                    if (data.isWriteTool) {
                        // Write tools need explicit user confirmation before executing.
                        const res = await confirmWriteTool(data.toolName, data.toolArgs || '{}');
                        if (res.confirmed) {
                            const execResult = await executeWriteTool(data.toolName, data.toolArgs || '{}');
                            showWriteResult(res.element, execResult);
                        }
                    } else {
                        appendToolCall(data.toolName, data.toolArgs, data.isWriteTool);
                    }
                } else if (data.type === 'tool_result') {
                    appendToolResult(data.toolName, data.toolResult, data.elapsedMs);
                } else if (data.type === 'error') {
                    appendMessage('error', data.error);
                } else if (data.type === 'done') {
                    if (data.sessionId) state.sessionId = data.sessionId;
                    if (assistantMsg) assistantMsg.setStreaming(false);
                    closeReasoning();
                    // Hide read-tool process blocks once the final answer is complete,
                    // but keep write-tool confirmation cards (they show the operation result).
                    const body = document.getElementById('ai-chat-body');
                    if (body) {
                        body.querySelectorAll('.ai-tool-call, .ai-tool-result').forEach(el => el.remove());
                    }
                    loadSessions();
                }
            }
        }
        // Safety: clear streaming state if the stream ends without a done event
        if (assistantMsg) assistantMsg.setStreaming(false);
    }

    function setStreaming(on) {
        state.isStreaming = on;
        const sendBtn = document.getElementById('ai-btn-send');
        const input = document.getElementById('ai-chat-input');
        if (on) {
            sendBtn.innerHTML = '<i class="bi bi-stop-circle"></i>';
            sendBtn.classList.add('stop');
            input.disabled = false;
        } else {
            sendBtn.innerHTML = '<i class="bi bi-send"></i>';
            sendBtn.classList.remove('stop');
            input.focus();
        }
    }

    // ─── Reasoning (thinking) Display ───

    function ensureReasoningBlock() {
        const body = document.getElementById('ai-chat-body');
        if (!reasoningEl || !body.contains(reasoningEl)) {
            reasoningEl = document.createElement('div');
            reasoningEl.className = 'ai-reasoning';
            reasoningEl.innerHTML = `
                <div class="ai-reasoning-header">
                    <i class="bi bi-lightbulb ai-reasoning-bulb"></i>
                    <span class="ai-reasoning-title">${__('ai.reasoning.title') || 'Thinking...'}</span>
                    <i class="bi bi-chevron-down ai-reasoning-arrow"></i>
                </div>
                <div class="ai-reasoning-body"><span class="ai-reasoning-content"></span></div>
            `;
            reasoningEl.querySelector('.ai-reasoning-header').addEventListener('click', () => {
                reasoningEl.classList.toggle('collapsed');
            });
            body.appendChild(reasoningEl);
            body.scrollTop = body.scrollHeight;
        }
        return reasoningEl.querySelector('.ai-reasoning-content');
    }

    function appendReasoning(chunk) {
        const el = ensureReasoningBlock();
        el.textContent += chunk;
        reasoningEl.classList.add('has-content');
        const body = document.getElementById('ai-chat-body');
        body.scrollTop = body.scrollHeight;
    }

    function closeReasoning() {
        // Remove the thinking block entirely once the answer is done,
        // so it doesn't leave behind a residual collapsed bar.
        if (reasoningEl) {
            reasoningEl.remove();
            reasoningEl = null;
        }
    }

    // ─── Message Rendering ───

    function appendMessage(role, content) {
        const body = document.getElementById('ai-chat-body');
        // Remove welcome if present
        const welcome = body.querySelector('.ai-welcome');
        if (welcome) welcome.remove();

        const msg = document.createElement('div');
        msg.className = `ai-msg ai-msg-${role}`;
        if (role === 'user') msg.dataset.userMsg = '1';

        const avatar = document.createElement('div');
        avatar.className = 'ai-msg-avatar';
        const icons = { user: 'bi-person-fill', assistant: 'bi-robot', error: 'bi-exclamation-triangle' };
        avatar.innerHTML = `<i class="bi ${icons[role] || 'bi-robot'}"></i>`;

        const bubble = document.createElement('div');
        bubble.className = 'ai-msg-bubble';
        if (role === 'error') {
            bubble.innerHTML = `<div class="ai-error-text">${escapeHtml(content)}</div>` +
                `<button class="ai-error-retry"><i class="bi bi-arrow-clockwise"></i> ${__('ai.msg.retry') || 'Retry'}</button>`;
        } else {
            bubble.innerHTML = renderMarkdown(content);
        }

        msg.appendChild(avatar);
        msg.appendChild(bubble);
        body.appendChild(msg);
        body.scrollTop = body.scrollHeight;

        // Store raw content for incremental streaming + copy
        bubble._rawContent = content;

        // Error retry: re-send the last user message
        if (role === 'error') {
            bubble.querySelector('.ai-error-retry').addEventListener('click', () => {
                const lastMsg = state.lastUserMessage;
                if (lastMsg) {
                    bubble.closest('.ai-msg')?.remove();
                    setStreaming(true);
                    state.abortController = new AbortController();
                    streamChat(lastMsg).catch(e => {
                        if (e.name !== 'AbortError') appendMessage('error', e.message);
                    }).finally(() => {
                        setStreaming(false);
                        state.abortController = null;
                    });
                }
            });
        }

        // Hover actions for assistant messages
        if (role === 'assistant') {
            const actions = document.createElement('div');
            actions.className = 'ai-msg-actions';
            actions.innerHTML = `
                <button class="ai-msg-action" data-act="copy" title="${__('ai.msg.copy') || 'Copy'}"><i class="bi bi-clipboard"></i></button>
                <button class="ai-msg-action" data-act="regen" title="${__('ai.msg.regenerate') || 'Regenerate'}"><i class="bi bi-arrow-clockwise"></i></button>
            `;
            bubble.appendChild(actions);
            actions.querySelector('[data-act="copy"]').addEventListener('click', () => copyToClipboard(bubble._rawContent || ''));
            actions.querySelector('[data-act="regen"]').addEventListener('click', () => regenerate());
        }

        return {
            appendContent(chunk) {
                bubble._rawContent += chunk;
                bubble.innerHTML = renderMarkdown(bubble._rawContent);
                // Re-attach actions after innerHTML replacement
                if (role === 'assistant') reattachMsgActions(bubble);
                msg.classList.add('streaming');
                body.scrollTop = body.scrollHeight;
            },
            setStreaming(on) {
                msg.classList.toggle('streaming', on);
            }
        };
    }

    function reattachMsgActions(bubble) {
        const actions = document.createElement('div');
        actions.className = 'ai-msg-actions';
        actions.innerHTML = `
            <button class="ai-msg-action" data-act="copy" title="${__('ai.msg.copy') || 'Copy'}"><i class="bi bi-clipboard"></i></button>
            <button class="ai-msg-action" data-act="regen" title="${__('ai.msg.regenerate') || 'Regenerate'}"><i class="bi bi-arrow-clockwise"></i></button>
        `;
        bubble.appendChild(actions);
        actions.querySelector('[data-act="copy"]').addEventListener('click', () => copyToClipboard(bubble._rawContent || ''));
        actions.querySelector('[data-act="regen"]').addEventListener('click', () => regenerate());
    }

    function appendToolCall(name, args, isWrite) {
        const body = document.getElementById('ai-chat-body');
        const el = document.createElement('div');
        el.className = 'ai-tool-call';
        const icon = isWrite ? 'bi-shield-lock' : 'bi-info-circle';
        const label = isWrite ? (__('ai.tool.confirmRequired') || 'Confirmation required') : (__('ai.tool.executing') || 'Executing...');
        el.innerHTML = `
            <div class="ai-tool-header">
                <i class="bi ${icon}"></i>
                <span class="ai-tool-name">${escapeHtml(name)}</span>
                <span class="ai-tool-status ${isWrite ? 'write' : 'read'}">${label}</span>
                <i class="bi bi-chevron-down ai-tool-arrow"></i>
            </div>
            <pre class="ai-tool-args">${escapeHtml(formatJson(args))}</pre>
        `;
        el.querySelector('.ai-tool-header').addEventListener('click', () => el.classList.toggle('collapsed'));
        body.appendChild(el);
        body.scrollTop = body.scrollHeight;
    }

    function appendToolResult(name, result, elapsedMs) {
        const body = document.getElementById('ai-chat-body');
        const el = document.createElement('div');
        el.className = 'ai-tool-result';
        const elapsedText = (elapsedMs != null) ? formatDuration(elapsedMs) : (__('ai.tool.result') || 'Result');
        el.innerHTML = `
            <div class="ai-tool-header">
                <i class="bi bi-check-circle-fill"></i>
                <span class="ai-tool-name">${escapeHtml(name)}</span>
                <span class="ai-tool-status read">${elapsedText}</span>
                <i class="bi bi-chevron-down ai-tool-arrow"></i>
            </div>
            <pre class="ai-tool-args">${escapeHtml(formatJson(result))}</pre>
        `;
        el.querySelector('.ai-tool-header').addEventListener('click', () => el.classList.toggle('collapsed'));
        body.appendChild(el);
        body.scrollTop = body.scrollHeight;
    }

    // ─── Write-tool confirmation & execution ───

    async function confirmWriteTool(name, args) {
        let data;
        try { data = JSON.parse(args); } catch { data = { raw: args }; }
        const summary = describeWriteAction(name, data);
        return new Promise((resolve) => {
            const body = document.getElementById('ai-chat-body');
            const el = document.createElement('div');
            el.className = 'ai-confirm-inline';
            el.innerHTML = `
                <div class="ai-confirm-inline-head">
                    <i class="bi bi-shield-exclamation"></i>
                    <span class="ai-confirm-inline-tool">${escapeHtml(name)}</span>
                    <span class="ai-confirm-inline-summary">${escapeHtml(summary)}</span>
                </div>
                <pre class="ai-confirm-inline-args">${escapeHtml(formatJson(args))}</pre>
                <div class="ai-confirm-inline-status" style="display:none;"></div>
                <div class="ai-confirm-inline-btns">
                    <button class="ai-confirm-inline-cancel">${__('ai.action.cancel') || 'Cancel'}</button>
                    <button class="ai-confirm-inline-ok">${__('ai.action.confirm') || 'Confirm'}</button>
                </div>
            `;
            body.appendChild(el);
            body.scrollTop = body.scrollHeight;
            const statusEl = el.querySelector('.ai-confirm-inline-status');
            const btns = el.querySelector('.ai-confirm-inline-btns');

            el.querySelector('.ai-confirm-inline-cancel').addEventListener('click', () => {
                statusEl.className = 'ai-confirm-inline-status cancelled';
                statusEl.textContent = __('ai.action.cancelled') || 'Cancelled';
                statusEl.style.display = 'inline-flex';
                el.querySelector('.ai-confirm-inline-cancel').disabled = true;
                btns.remove();
                resolve({ confirmed: false, element: el });
            });
            el.querySelector('.ai-confirm-inline-ok').addEventListener('click', () => {
                statusEl.className = 'ai-confirm-inline-status executing';
                statusEl.innerHTML = '<i class="bi bi-arrow-clockwise spin"></i> ' + (__('ai.action.executing') || 'Executing...');
                statusEl.style.display = 'inline-flex';
                el.querySelector('.ai-confirm-inline-ok').disabled = true;
                btns.remove();
                resolve({ confirmed: true, element: el });
            });
        });
    }

    // Show the execution outcome inside the confirmation card (success / failure)
    function showWriteResult(el, execResult) {
        let parsed = null;
        try { parsed = JSON.parse(execResult); } catch {}
        const okExec = parsed && parsed.success === true;
        const statusEl = el.querySelector('.ai-confirm-inline-status');
        if (statusEl) {
            statusEl.className = 'ai-confirm-inline-status ' + (okExec ? 'success' : 'error');
            statusEl.innerHTML = okExec
                ? '<i class="bi bi-check-circle"></i> ' + (__('ai.action.executed') || 'Executed')
                : '<i class="bi bi-x-circle"></i> ' + (__('ai.action.failed') || 'Failed');
            statusEl.style.display = 'inline-flex';
        }
        const detail = document.createElement('pre');
        detail.className = 'ai-confirm-inline-result';
        detail.textContent = formatJson(execResult);
        el.appendChild(detail);
        const body = document.getElementById('ai-chat-body');
        if (body) body.scrollTop = body.scrollHeight;
    }

    function describeWriteAction(name, data) {
        const d = data || {};
        switch (name) {
            case 'toggle_route':
                return (d.enabled ? __('ai.write.enableRoute') || 'Enable route' : __('ai.write.disableRoute') || 'Disable route') + `: ${d.routeId || '?'}`;
            case 'create_route':
                return (__('ai.write.createRoute') || 'Create route') + `: ${d.routeId || '?'} → ${d.path || ''} → ${d.clusterId || ''}`;
            case 'delete_route':
                return (__('ai.write.deleteRoute') || 'Delete route') + `: ${d.routeId || '?'}`;
            case 'delete_cluster':
                return (__('ai.write.deleteCluster') || 'Delete cluster') + `: ${d.clusterId || '?'}`;
            default:
                return name;
        }
    }

    async function executeWriteTool(name, args) {
        try {
            const resp = await fetch((window.__dashboard?.basePath || '') + '/api/ai/tool/execute', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ toolName: name, arguments: args })
            });
            if (!resp.ok) return JSON.stringify({ error: `HTTP ${resp.status}` });
            const json = await resp.json();
            return json.result || JSON.stringify(json);
        } catch (e) {
            return JSON.stringify({ error: e.message });
        }
    }

    // ─── Markdown Rendering ───

    function renderMarkdown(text) {
        if (!text) return '';
        const lines = text.split('\n');
        let html = '';
        let i = 0;
        const listStack = []; // {tag, indent}

        const closeLists = (indent) => {
            while (listStack.length && listStack[listStack.length - 1].indent >= indent) {
                html += `</${listStack.pop().tag}>`;
            }
        };

        while (i < lines.length) {
            const line = lines[i];
            const trimmed = line.trim();

            // Fenced code block
            const fence = line.match(/^\s*(```|~~~)(\w*)\s*$/);
            if (fence) {
                closeLists(0);
                const marker = fence[1];
                const lang = fence[2];
                i++;
                const code = [];
                while (i < lines.length && !lines[i].trim().startsWith(marker)) {
                    code.push(lines[i]);
                    i++;
                }
                i++; // skip closing fence
                html += renderCodeBlock(code.join('\n'), lang);
                continue;
            }

            // Horizontal rule
            if (/^\s*((?:- *){3,}|(?:\* *){3,}|(?:_ *){3,})\s*$/.test(line)) {
                closeLists(0);
                html += '<hr class="ai-md-hr">';
                i++;
                continue;
            }

            // Table
            if (trimmed.includes('|') && i + 1 < lines.length && isTableDivider(lines[i + 1])) {
                closeLists(0);
                const header = parseTableRow(line);
                const aligns = parseTableAligns(lines[i + 1]);
                i += 2;
                const rows = [];
                while (i < lines.length && lines[i].trim().includes('|')) {
                    rows.push(parseTableRow(lines[i]));
                    i++;
                }
                html += renderTable(header, aligns, rows);
                continue;
            }

            // Blockquote
            if (/^\s*>/.test(line)) {
                closeLists(0);
                const quote = [];
                while (i < lines.length && /^\s*>/.test(lines[i])) {
                    quote.push(lines[i].replace(/^\s*>\s?/, ''));
                    i++;
                }
                html += `<blockquote class="ai-md-quote">${renderMarkdown(quote.join('\n'))}</blockquote>`;
                continue;
            }

            // Headers
            const h = line.match(/^\s*(#{1,4})\s+(.+)$/);
            if (h) {
                closeLists(0);
                const level = Math.min(h[1].length + 1, 5);
                html += `<h${level} class="ai-md-h${level}">${renderInline(h[2])}</h${level}>`;
                i++;
                continue;
            }

            // List items (unordered / ordered / task)
            const ul = line.match(/^(\s*)[-*+]\s+(.*)$/);
            const ol = line.match(/^(\s*)(\d+)[.)]\s+(.*)$/);
            if (ul || ol) {
                const indent = ul ? ul[1].length : ol[1].length;
                const tag = ul ? 'ul' : 'ol';
                const content = ul ? ul[2] : ol[3];
                closeLists(indent);
                if (!listStack.length || listStack[listStack.length - 1].tag !== tag) {
                    html += `<${tag} class="ai-md-list">`;
                    listStack.push({ tag, indent });
                }
                const task = content.match(/^\[([ xX])\]\s+(.*)$/);
                if (task && tag === 'ul') {
                    const checked = task[1] === 'x' || task[1] === 'X';
                    html += `<li class="ai-md-task${checked ? ' done' : ''}">` +
                        `<input type="checkbox" disabled ${checked ? 'checked' : ''}>${renderInline(task[2])}</li>`;
                } else {
                    html += `<li>${renderInline(content)}</li>`;
                }
                i++;
                continue;
            }

            // Empty line
            if (!trimmed) {
                closeLists(0);
                html += '<div class="ai-md-gap"></div>';
                i++;
                continue;
            }

            // Paragraph (accumulate consecutive text lines)
            closeLists(0);
            const para = [];
            while (i < lines.length && lines[i].trim() && !isBlockStart(lines[i])) {
                para.push(lines[i].trim());
                i++;
            }
            // Fallback: consume the line as plain text if it was flagged as a block
            // start but no earlier branch handled it (e.g. a table row without a
            // divider) — guarantees the outer loop always advances.
            if (para.length === 0 && i < lines.length) {
                para.push(lines[i].trim());
                i++;
            }
            html += `<p class="ai-md-p">${renderInline(para.join('<br>'))}</p>`;
        }

        // Close any remaining lists
        while (listStack.length) html += `</${listStack.pop().tag}>`;
        return html;
    }

    function isBlockStart(line) {
        const t = line.trim();
        return /^(```|~~~|#{1,4}\s|>|[-*+]\s|\d+[.)]\s)/.test(t)
            || /^\s*((?:- *){3,}|(?:\* *){3,}|(?:_ *){3,})\s*$/.test(t)
            || /^\s*\|.+\|/.test(t);
    }

    function renderCodeBlock(code, lang) {
        const langLabel = lang ? `<span class="ai-code-lang">${escapeHtml(lang)}</span>` : '';
        const escaped = escapeHtml(code);
        const highlighted = lang ? highlightCode(escaped) : escaped;
        return `<div class="ai-code-block-wrap"><div class="ai-code-head">${langLabel}</div>` +
            `<pre class="ai-code-block"><code>${highlighted}</code></pre></div>`;
    }

    // Lightweight syntax highlighting (applied on already-escaped text)
    function highlightCode(esc) {
        let s = esc;
        // Strings (escaped quotes)
        s = s.replace(/(&quot;.*?&quot;|&#39;.*?&#39;)/g, '<span class="tok-str">$1</span>');
        // Comments
        s = s.replace(/(\/\/[^\n]*|#.*$)/gm, '<span class="tok-com">$1</span>');
        // Numbers
        s = s.replace(/\b(\d+(?:\.\d+)?)\b/g, '<span class="tok-num">$1</span>');
        // Keywords
        s = s.replace(/\b(const|let|var|function|return|if|else|for|while|do|class|import|export|new|async|await|try|catch|finally|throw|public|private|protected|static|readonly|string|int|long|double|bool|void|namespace|using|true|false|null|undefined|this|typeof|extends|switch|case|break|continue)\b/g,
            '<span class="tok-kw">$1</span>');
        return s;
    }

    function renderTable(headers, aligns, rows) {
        const alignAttr = (a) => a ? ` style="text-align:${a}"` : '';
        let h = '<table class="ai-md-table"><thead><tr>';
        headers.forEach((c, idx) => {
            h += `<th${alignAttr(aligns[idx])}>${renderInline(c)}</th>`;
        });
        h += '</tr></thead><tbody>';
        rows.forEach(r => {
            h += '<tr>';
            headers.forEach((_, idx) => {
                h += `<td${alignAttr(aligns[idx])}>${renderInline(r[idx] || '')}</td>`;
            });
            h += '</tr>';
        });
        return h + '</tbody></table>';
    }

    function parseTableRow(line) {
        return line.trim().replace(/^\|/, '').replace(/\|$/, '').split('|').map(c => c.trim());
    }

    function isTableDivider(line) {
        return /^\s*\|?[\s:|-]+\|?\s*$/.test(line) && line.includes('-');
    }

    function parseTableAligns(line) {
        return parseTableRow(line).map(c => {
            const l = c.startsWith(':'), r = c.endsWith(':');
            return l && r ? 'center' : r ? 'right' : l ? 'left' : null;
        });
    }

    function renderInline(text) {
        let s = escapeHtml(text);
        // Links [text](url)
        s = s.replace(/\[([^\]]+)\]\(([^)\s]+)(?:\s+&quot;([^&]*)&quot;)?\)/g, (m, t, u, title) => {
            const safe = /^(https?:|mailto:|#|\/)/.test(u) ? u : '#' + u;
            return `<a href="${safe}" target="_blank" rel="noopener"${title ? ` title="${title}"` : ''}>${t}</a>`;
        });
        // Inline code (before other formatting)
        s = s.replace(/`([^`]+)`/g, '<code class="ai-inline-code">$1</code>');
        // Bold
        s = s.replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>');
        // Italic
        s = s.replace(/(^|[^*])\*([^*]+)\*/g, '$1<em>$2</em>');
        // Strikethrough
        s = s.replace(/~~([^~]+)~~/g, '<del>$1</del>');
        return s;
    }

    function escapeHtml(s) {
        if (s == null) return '';
        const div = document.createElement('div');
        div.textContent = String(s);
        return div.innerHTML;
    }

    function formatJson(str) {
        try { return JSON.stringify(JSON.parse(str), null, 2); }
        catch { return str; }
    }

    function formatDuration(ms) {
        if (ms == null) return '';
        return ms < 1000 ? Math.round(ms) + ' ms' : (ms / 1000).toFixed(1) + ' s';
    }

    function autoResize() {
        this.style.height = 'auto';
        this.style.height = Math.min(this.scrollHeight, 120) + 'px';
    }

    // ─── Sessions ───

    async function loadSessions() {
        try {
            const resp = await fetch((window.__dashboard?.basePath || '') + '/api/ai/sessions');
            if (!resp.ok) return;
            state.sessions = await resp.json();
            renderSessions();
        } catch {}
    }

    function renderSessions() {
        const container = document.getElementById('ai-history-items');
        if (!container) return;
        if (state.sessions.length === 0) {
            container.innerHTML = `<div class="ai-history-empty">${__('ai.history.empty') || 'No sessions'}</div>`;
            return;
        }
        container.innerHTML = state.sessions.map(s => `
            <div class="ai-history-item ${s.sessionId === state.sessionId ? 'active' : ''}" data-id="${s.sessionId}">
                <span class="ai-history-label">${escapeHtml(s.lastMessage || s.sessionId)}</span>
                <button class="ai-history-delete" data-id="${s.sessionId}">
                    <i class="bi bi-trash"></i>
                </button>
            </div>
        `).join('');
        container.querySelectorAll('.ai-history-item').forEach(item => {
            item.addEventListener('click', (e) => {
                if (e.target.closest('.ai-history-delete')) return;
                loadSessionMessages(item.dataset.id);
            });
        });
        container.querySelectorAll('.ai-history-delete').forEach(btn => {
            btn.addEventListener('click', (e) => {
                e.stopPropagation();
                deleteSession(btn.dataset.id);
            });
        });
    }

    function toggleHistory() {
        const el = document.getElementById('ai-history-list');
        state.historyVisible = !state.historyVisible;
        el.style.display = state.historyVisible ? 'flex' : 'none';
        if (state.historyVisible) loadSessions();
    }

    async function loadSessionMessages(sessionId) {
        state.sessionId = sessionId;
        state.lastUserMessage = null;
        reasoningEl = null;
        const body = document.getElementById('ai-chat-body');
        body.innerHTML = '';
        try {
            const resp = await fetch(`${window.__dashboard?.basePath || ''}/api/ai/sessions/${sessionId}/messages`);
            if (!resp.ok) return;
            const messages = await resp.json();
            messages.forEach(m => {
                if (m.role === 'user' || m.role === 'assistant') {
                    appendMessage(m.role, m.content);
                    if (m.role === 'user') state.lastUserMessage = m.content;
                }
            });
        } catch {}
        toggleHistory();
    }

    async function deleteSession(sessionId) {
        try {
            await fetch(`${window.__dashboard?.basePath || ''}/api/ai/sessions/${sessionId}`, { method: 'DELETE' });
            if (state.sessionId === sessionId) newChat();
            loadSessions();
        } catch {}
    }

    function newChat() {
        state.sessionId = null;
        state.lastUserMessage = null;
        reasoningEl = null;
        const body = document.getElementById('ai-chat-body');
        body.innerHTML = `
            <div class="ai-welcome">
                <i class="bi bi-robot"></i>
                <p>${__('ai.welcome.message') || 'Hello! How can I help you?'}</p>
                ${QUICK_BTNS}
            </div>
        `;
        bindQuickActions(body);
    }

    // ─── Config ───

    async function loadConfig() {
        try {
            const resp = await fetch((window.__dashboard?.basePath || '') + '/api/ai/config');
            if (!resp.ok) return;
            const config = await resp.json();
            const btn = document.getElementById('ai-float-btn');
            if (config.isConfigured && config.enabled) {
                btn.classList.add('online');
            } else {
                btn.classList.add('offline');
            }
        } catch {}
    }

    // ─── Export ───

    window.DashboardAI = { init, togglePanel, sendMessage };
})();
