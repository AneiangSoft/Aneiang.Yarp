/**
 * AI Chat Widget — Enterprise-grade
 * SSE streaming, markdown rendering, tool calling, action confirmation,
 * session history, and i18n support.
 */
(function () {
    'use strict';

    // ==================== State ====================
    var S = {
        isOpen: false,
        isStreaming: false,
        sessionId: null,
        available: false,
        messages: [] // in-memory message log for current view
    };

    // ==================== Public API ====================
    window.AIChat = {

        init: async function () {
            try {
                var resp = await DashboardApi.endpoints.getAIStatus();
                var status = resp?.data || resp;
                S.available = status?.available === true;
            } catch (e) {
                S.available = false;
            }
            if (!S.available) {
                var fab = document.getElementById('ai-chat-fab');
                if (fab) fab.style.display = 'none';
                return;
            }
            S.sessionId = localStorage.getItem('ai_chat_session') || null;
            if (S.sessionId) {
                await _loadSessionMessages();
            } else {
                _renderWelcome();
            }
            // Apply i18n to static elements
            _applyI18n();
            // Keyboard shortcut
            document.addEventListener('keydown', function (e) {
                if (e.ctrlKey && e.key === '/') { e.preventDefault(); AIChat.toggle(); }
            });
        },

        toggle: function () { S.isOpen ? this.close() : this.open(); },

        open: function () {
            var win = document.getElementById('ai-chat-window');
            if (!win) return;
            win.classList.add('open');
            S.isOpen = true;
            var input = document.getElementById('ai-chat-input');
            if (input) setTimeout(function () { input.focus(); }, 150);
            _scrollBottom();
        },

        close: function () {
            var win = document.getElementById('ai-chat-window');
            if (win) win.classList.remove('open');
            S.isOpen = false;
        },

        clearSession: function () {
            S.sessionId = null;
            S.messages = [];
            localStorage.removeItem('ai_chat_session');
            _renderWelcome();
        },

        sendQuickAction: function (prompt) {
            var input = document.getElementById('ai-chat-input');
            if (input) { input.value = prompt; this.send(); }
        },

        // Auto-resize textarea (called from oninput)
        _autoResize: function (el) {
            el.style.height = 'auto';
            el.style.height = Math.min(el.scrollHeight, 100) + 'px';
        },

        // ==================== Send Message ====================
        send: async function () {
            if (S.isStreaming) return;
            var input = document.getElementById('ai-chat-input');
            var text = input ? input.value.trim() : '';
            if (!text) return;
            input.value = '';
            input.style.height = 'auto';

            // Remove welcome screen
            _removeWelcome();
            // Add user message
            _appendMsg('user', text);
            S.messages.push({ role: 'user', content: text, time: new Date() });
            // Typing indicator
            _showTyping();
            S.isStreaming = true;
            _setSendDisabled(true);

            try {
                if (!S.sessionId) {
                    S.sessionId = 's-' + Date.now().toString(36);
                    localStorage.setItem('ai_chat_session', S.sessionId);
                }

                var baseUrl = (window.__dashboard?.basePath || '');
                var token = DashboardApi.getToken();
                var resp = await fetch(baseUrl + '/api/ai/chat', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                        'Authorization': token ? 'Bearer ' + token : ''
                    },
                    body: JSON.stringify({
                        sessionId: S.sessionId,
                        messages: [{ role: 'user', content: text }],
                        locale: _getLocale()
                    })
                });
                if (!resp.ok) throw new Error('HTTP ' + resp.status);

                var newSid = resp.headers.get('X-Session-Id');
                if (newSid) {
                    S.sessionId = newSid;
                    localStorage.setItem('ai_chat_session', newSid);
                }

                _removeTyping();
                var el = _appendMsg('assistant', '');
                var contentEl = el.querySelector('.ai-chat-bubble');

                var reader = resp.body.getReader();
                var decoder = new TextDecoder();
                var fullText = '';
                var buffer = '';
                var pendingAction = null;

                while (true) {
                    var result = await reader.read();
                    if (result.done) break;
                    buffer += decoder.decode(result.value, { stream: true });
                    var lines = buffer.split('\n');
                    buffer = lines.pop() || '';
                    for (var i = 0; i < lines.length; i++) {
                        var line = lines[i].trim();
                        if (!line.startsWith('data: ')) continue;
                        var data = line.substring(6);
                        if (data === '[DONE]') continue;
                        try {
                            var parsed = JSON.parse(data);
                            if (parsed.type === 'pending_action' && parsed.pendingAction) {
                                pendingAction = parsed.pendingAction;
                                continue;
                            }
                            if (parsed.content) {
                                fullText += parsed.content;
                                contentEl.innerHTML = _renderMd(fullText);
                                _scrollBottom();
                            }
                        } catch (ex) { /* skip */ }
                    }
                }

                contentEl.innerHTML = _renderMd(fullText);
                if (pendingAction) _renderActionCard(pendingAction, contentEl);

                S.messages.push({ role: 'assistant', content: fullText, time: new Date() });
                _scrollBottom();

            } catch (e) {
                _removeTyping();
                _appendError(e.message || _t('ai.error.requestFailed', '请求失败，请检查 AI 设置。'));
            }
            S.isStreaming = false;
            _setSendDisabled(false);
        },

        // ==================== Confirm / Cancel Action ====================
        confirmAction: async function (toolName, argumentsJson, callId) {
            if (S.isStreaming) return;
            S.isStreaming = true;
            _setSendDisabled(true);

            var card = document.querySelector('.ai-action-card[data-call-id="' + callId + '"]');
            if (card) {
                card.querySelectorAll('button').forEach(function (b) { b.disabled = true; });
                var st = card.querySelector('.ai-action-status');
                if (st) st.textContent = _t('ai.action.executing', '执行中…');
            }
            _showTyping();

            try {
                var baseUrl = (window.__dashboard?.basePath || '');
                var token = DashboardApi.getToken();
                var resp = await fetch(baseUrl + '/api/ai/chat/confirm-action', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                        'Authorization': token ? 'Bearer ' + token : ''
                    },
                    body: JSON.stringify({
                        sessionId: S.sessionId,
                        toolName: toolName,
                        arguments: argumentsJson,
                        callId: callId,
                        locale: _getLocale()
                    })
                });
                if (!resp.ok) throw new Error('HTTP ' + resp.status);

                _removeTyping();
                var el = _appendMsg('assistant', '');
                var contentEl = el.querySelector('.ai-chat-bubble');
                var reader = resp.body.getReader();
                var decoder = new TextDecoder();
                var fullText = '';
                var buffer = '';

                while (true) {
                    var result = await reader.read();
                    if (result.done) break;
                    buffer += decoder.decode(result.value, { stream: true });
                    var lines = buffer.split('\n');
                    buffer = lines.pop() || '';
                    for (var i = 0; i < lines.length; i++) {
                        var line = lines[i].trim();
                        if (!line.startsWith('data: ')) continue;
                        var data = line.substring(6);
                        if (data === '[DONE]') continue;
                        try {
                            var parsed = JSON.parse(data);
                            if (parsed.content) {
                                fullText += parsed.content;
                                contentEl.innerHTML = _renderMd(fullText);
                                _scrollBottom();
                            }
                        } catch (ex) { /* skip */ }
                    }
                }
                contentEl.innerHTML = _renderMd(fullText);
                S.messages.push({ role: 'assistant', content: fullText, time: new Date() });

                if (card) {
                    var st = card.querySelector('.ai-action-status');
                    if (st) st.textContent = _t('ai.action.executed', '✅ 已执行');
                    card.classList.add('executed');
                }
                _scrollBottom();
            } catch (e) {
                _removeTyping();
                _appendError(_t('ai.error.executionFailed', '执行失败: ') + (e.message || ''));
                if (card) {
                    var st = card.querySelector('.ai-action-status');
                    if (st) st.textContent = _t('ai.action.failed', '❌ 失败');
                }
            }
            S.isStreaming = false;
            _setSendDisabled(false);
        },

        cancelAction: function (callId) {
            var card = document.querySelector('.ai-action-card[data-call-id="' + callId + '"]');
            if (card) {
                card.querySelectorAll('button').forEach(function (b) { b.disabled = true; });
                var st = card.querySelector('.ai-action-status');
                if (st) st.textContent = _t('ai.action.cancelled', '已取消');
                card.classList.add('cancelled');
            }
        },

        // ==================== History ====================
        showHistory: async function () {
            var win = document.getElementById('ai-chat-window');
            if (!win) return;

            // Toggle
            var existing = win.querySelector('.ai-history-overlay');
            if (existing) { existing.remove(); return; }

            var overlay = document.createElement('div');
            overlay.className = 'ai-history-overlay';

            // Header
            overlay.innerHTML =
                '<div class="ai-history-header">' +
                    '<div class="ai-history-title"><i class="bi bi-clock-history"></i> ' + _t('ai.history.title', '对话历史') + '</div>' +
                    '<button class="ai-history-close" onclick="this.closest(\'.ai-history-overlay\').remove()"><i class="bi bi-x-lg"></i></button>' +
                '</div>' +
                '<div class="ai-history-list"><div style="text-align:center;padding:24px;color:#94a3b8"><i class="bi bi-hourglass-split"></i></div></div>';

            win.appendChild(overlay);

            try {
                var sessions = [];
                try {
                    sessions = await DashboardApi.endpoints.getAISessions(20);
                    if (!Array.isArray(sessions)) sessions = [];
                } catch (e) { sessions = []; }

                var list = overlay.querySelector('.ai-history-list');
                list.innerHTML = '';

                // Current session
                if (S.messages.length > 0) {
                    var curTitle = _t('ai.history.current', '当前对话');
                    if (S.messages.length > 0 && S.messages[0].role === 'user') {
                        var preview = S.messages[0].content;
                        curTitle = preview.length > 30 ? preview.substring(0, 30) + '…' : preview;
                    }
                    var curEl = document.createElement('div');
                    curEl.className = 'ai-hist-item active';
                    curEl.innerHTML =
                        '<div class="ai-hist-item-body">' +
                            '<div class="ai-hist-item-title"><i class="bi bi-chat-fill me-1" style="color:#7c3aed"></i>' + _esc(curTitle) + '</div>' +
                            '<div class="ai-hist-item-meta">' + _t('ai.history.current', '当前对话') + ' · ' + S.messages.length + ' msgs</div>' +
                        '</div>';
                    list.appendChild(curEl);
                }

                // Filter current session
                var filtered = sessions.filter(function (s) {
                    return s && (s.sessionId || '') !== (S.sessionId || '');
                });

                if (filtered.length === 0 && S.messages.length === 0) {
                    list.innerHTML = '<div class="ai-history-empty"><i class="bi bi-chat-square-text"></i>' + _t('ai.history.empty', '暂无历史记录') + '</div>';
                } else {
                    for (var i = 0; i < filtered.length; i++) {
                        var s = filtered[i];
                        var sid = s.sessionId || '';
                        var title = s.title || (sid.length > 14 ? sid.substring(0, 14) + '…' : sid);
                        var date = s.lastActivity ? new Date(s.lastActivity).toLocaleDateString([], { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' }) : '';
                        var count = s.messageCount || 0;

                        var item = document.createElement('div');
                        item.className = 'ai-hist-item';
                        item.setAttribute('data-sid', sid);
                        item.innerHTML =
                            '<div class="ai-hist-item-body" onclick="AIChat.loadSession(\'' + _esc(sid) + '\')">' +
                                '<div class="ai-hist-item-title">' + _esc(title) + '</div>' +
                                '<div class="ai-hist-item-meta">' + count + ' msgs · ' + date + '</div>' +
                            '</div>' +
                            '<button class="ai-hist-del" onclick="event.stopPropagation();AIChat.deleteSession(\'' + _esc(sid) + '\')" title="Delete"><i class="bi bi-trash3"></i></button>';
                        list.appendChild(item);
                    }
                }
            } catch (e) {
                console.warn('[AIChat] History load error:', e);
            }
        },

        loadSession: async function (sessionId) {
            var overlay = document.querySelector('.ai-history-overlay');
            if (overlay) overlay.remove();

            var container = document.getElementById('ai-chat-messages');
            if (!container) return;
            container.innerHTML = '';

            S.sessionId = sessionId;
            S.messages = [];
            localStorage.setItem('ai_chat_session', sessionId);
            await _loadSessionMessages();
        },

        deleteSession: async function (sessionId) {
            if (!confirm(_t('ai.history.confirmDelete', '确认删除此对话？'))) return;
            try {
                await DashboardApi.endpoints.deleteAISession(sessionId);
                var item = document.querySelector('.ai-hist-item[data-sid="' + sessionId + '"]');
                if (item) item.remove();

                var list = document.querySelector('.ai-history-list');
                if (list && list.querySelectorAll('.ai-hist-item').length === 0) {
                    list.innerHTML = '<div class="ai-history-empty"><i class="bi bi-chat-square-text"></i>' + _t('ai.history.empty', '暂无历史记录') + '</div>';
                }
                if (sessionId === S.sessionId) AIChat.clearSession();
            } catch (e) {
                console.warn('[AIChat] Delete failed:', e);
            }
        }
    };

    // ==================== Private: Rendering ====================

    function _t(key, fallback) {
        return (window.__ && window.I18N && window.I18N[key]) ? window.I18N[key] : fallback;
    }

    function _applyI18n() {
        // Apply data-i18n-title attributes
        var win = document.getElementById('ai-chat-window');
        if (!win) return;
        win.querySelectorAll('[data-i18n-title]').forEach(function (el) {
            var key = el.getAttribute('data-i18n-title');
            if (window.I18N && window.I18N[key]) el.title = window.I18N[key];
        });
        win.querySelectorAll('[data-i18n-placeholder]').forEach(function (el) {
            var key = el.getAttribute('data-i18n-placeholder');
            if (window.I18N && window.I18N[key]) el.placeholder = window.I18N[key];
        });
    }

    function _renderWelcome() {
        var container = document.getElementById('ai-chat-messages');
        if (!container) return;
        var btns = [
            { icon: 'bi-heart-pulse', label: _t('ai.quick.health', '查看健康状态'), prompt: _t('ai.quick.healthPrompt', '查看所有目的地和熔断器的健康状态') },
            { icon: 'bi-bar-chart', label: _t('ai.quick.traffic', '今日流量'), prompt: _t('ai.quick.trafficPrompt', '查看最近1小时的流量和错误率统计') },
            { icon: 'bi-list-ul', label: _t('ai.quick.routes', '路由列表'), prompt: _t('ai.quick.routesPrompt', '列出所有路由及其状态') },
            { icon: 'bi-shield-check', label: _t('ai.quick.waf', 'WAF 状态'), prompt: _t('ai.quick.wafPrompt', '查看当前 WAF 配置') }
        ];
        var btnHtml = '';
        for (var i = 0; i < btns.length; i++) {
            btnHtml += '<button class="ai-welcome-btn" onclick="AIChat.sendQuickAction(\'' + _escJs(btns[i].prompt) + '\')">' +
                '<i class="bi ' + btns[i].icon + '"></i>' + _esc(btns[i].label) + '</button>';
        }
        container.innerHTML =
            '<div class="ai-welcome">' +
                '<div class="ai-welcome-icon"><i class="bi bi-robot"></i></div>' +
                '<h5>' + _t('ai.welcome.title', 'AI 助手') + '</h5>' +
                '<p class="ai-welcome-desc">' + _t('ai.welcome.desc', '我可以查询网关状态、管理路由和集群、控制熔断器、配置 WAF 等') + '</p>' +
                '<div class="ai-welcome-actions">' + btnHtml + '</div>' +
            '</div>';
    }

    function _removeWelcome() {
        var w = document.querySelector('#ai-chat-messages .ai-welcome');
        if (w) w.remove();
    }

    function _appendMsg(role, content) {
        var container = document.getElementById('ai-chat-messages');
        if (!container) return null;

        var row = document.createElement('div');
        row.className = 'ai-chat-msg-row ' + role;

        // Avatar
        var avatar = document.createElement('div');
        avatar.className = 'ai-msg-avatar ' + (role === 'user' ? 'user-av' : 'bot');
        avatar.innerHTML = role === 'user' ? '<i class="bi bi-person-fill"></i>' : '<i class="bi bi-robot"></i>';

        // Bubble wrapper
        var wrap = document.createElement('div');
        wrap.style.cssText = 'flex:1;min-width:0';

        var bubble = document.createElement('div');
        bubble.className = 'ai-chat-bubble';
        bubble.innerHTML = role === 'user' ? _esc(content) : _renderMd(content);

        // Meta row (time + copy)
        var meta = document.createElement('div');
        meta.className = 'ai-msg-meta';
        var time = document.createElement('span');
        time.className = 'ai-chat-time';
        time.textContent = new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
        meta.appendChild(time);

        if (role === 'assistant' && content) {
            var copyBtn = document.createElement('button');
            copyBtn.className = 'ai-msg-copy';
            copyBtn.innerHTML = '<i class="bi bi-clipboard"></i>';
            copyBtn.onclick = function () {
                var text = bubble.textContent || bubble.innerText || '';
                navigator.clipboard.writeText(text).then(function () {
                    copyBtn.innerHTML = '<i class="bi bi-check2"></i>';
                    copyBtn.classList.add('copied');
                    setTimeout(function () {
                        copyBtn.innerHTML = '<i class="bi bi-clipboard"></i>';
                        copyBtn.classList.remove('copied');
                    }, 2000);
                });
            };
            meta.appendChild(copyBtn);
        }

        wrap.appendChild(bubble);
        wrap.appendChild(meta);
        row.appendChild(avatar);
        row.appendChild(wrap);
        container.appendChild(row);
        _scrollBottom();
        return row;
    }

    function _appendError(msg) {
        var container = document.getElementById('ai-chat-messages');
        if (!container) return;
        var div = document.createElement('div');
        div.className = 'ai-msg-error';
        div.innerHTML = '<i class="bi bi-exclamation-triangle-fill"></i> ' + _esc(msg);
        container.appendChild(div);
        _scrollBottom();
    }

    function _showTyping() {
        var container = document.getElementById('ai-chat-messages');
        if (!container) return;
        var row = document.createElement('div');
        row.className = 'ai-chat-msg-row assistant';
        row.id = 'ai-typing-row';
        var avatar = document.createElement('div');
        avatar.className = 'ai-msg-avatar bot';
        avatar.innerHTML = '<i class="bi bi-robot"></i>';
        var wrap = document.createElement('div');
        wrap.style.cssText = 'flex:1;min-width:0';
        var bubble = document.createElement('div');
        bubble.className = 'ai-chat-bubble';
        bubble.innerHTML = '<div class="ai-chat-typing"><span></span><span></span><span></span></div>';
        wrap.appendChild(bubble);
        row.appendChild(avatar);
        row.appendChild(wrap);
        container.appendChild(row);
        _scrollBottom();
    }

    function _removeTyping() {
        var el = document.getElementById('ai-typing-row');
        if (el) el.remove();
    }

    function _setSendDisabled(disabled) {
        var btn = document.getElementById('ai-chat-send');
        if (btn) btn.disabled = disabled;
    }

    function _scrollBottom() {
        var c = document.getElementById('ai-chat-messages');
        if (c) c.scrollTop = c.scrollHeight;
    }

    function _renderActionCard(action, parentEl) {
        var card = document.createElement('div');
        card.className = 'ai-action-card';
        card.setAttribute('data-call-id', action.callId);
        var argsObj = {};
        try { argsObj = JSON.parse(action.arguments); } catch (e) { }
        var argsHtml = '';
        var keys = Object.keys(argsObj);
        for (var i = 0; i < keys.length; i++) {
            var k = keys[i];
            var v = argsObj[k];
            if (typeof v === 'object') v = JSON.stringify(v);
            argsHtml += '<div class="ai-action-arg"><span class="ai-action-arg-key">' +
                _esc(k.replace(/_/g, ' ')) + ':</span> <span class="ai-action-arg-val">' +
                _esc(String(v)) + '</span></div>';
        }
        card.innerHTML =
            '<div class="ai-action-header"><i class="bi bi-shield-exclamation"></i> ' + _t('ai.action.title', '操作确认') + '</div>' +
            '<div class="ai-action-desc">' + _esc(action.description) + '</div>' +
            (argsHtml ? '<div class="ai-action-args">' + argsHtml + '</div>' : '') +
            '<div class="ai-action-buttons">' +
                '<button class="btn btn-sm btn-success" onclick="AIChat.confirmAction(\'' +
                    _esc(action.toolName) + '\',\'' + _esc(action.arguments).replace(/'/g, "\\'") + '\',\'' +
                    _esc(action.callId) + '\')"><i class="bi bi-check-circle"></i> ' + _t('ai.action.confirm', '确认执行') + '</button>' +
                '<button class="btn btn-sm btn-outline-secondary" onclick="AIChat.cancelAction(\'' +
                    _esc(action.callId) + '\')"><i class="bi bi-x-circle"></i> ' + _t('ai.action.cancel', '取消') + '</button>' +
            '</div>' +
            '<div class="ai-action-status"></div>';
        parentEl.appendChild(card);
    }

    // ==================== Private: Session Loading ====================

    async function _loadSessionMessages() {
        try {
            var resp = await DashboardApi.endpoints.getSessionMessages(S.sessionId, 50);
            var messages = resp?.data || resp || [];
            if (!Array.isArray(messages) || messages.length === 0) {
                _renderWelcome();
                return;
            }
            var container = document.getElementById('ai-chat-messages');
            if (!container) return;
            container.innerHTML = '';
            S.messages = [];
            for (var i = 0; i < messages.length; i++) {
                var msg = messages[i];
                _appendMsg(msg.role, msg.content || '');
                S.messages.push({ role: msg.role, content: msg.content || '', time: msg.createdAt ? new Date(msg.createdAt) : new Date() });
            }
            _scrollBottom();
        } catch (e) {
            console.warn('[AIChat] Load session failed:', e);
            _renderWelcome();
        }
    }

    // ==================== Private: Markdown ====================

    function _renderMd(text) {
        if (!text) return '';
        var html = _esc(text);
        // Code blocks
        html = html.replace(/```(\w*)\n([\s\S]*?)```/g, '<pre><code>$2</code></pre>');
        // Inline code
        html = html.replace(/`([^`]+)`/g, '<code>$1</code>');
        // Bold
        html = html.replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>');
        // Italic
        html = html.replace(/\*(.+?)\*/g, '<em>$1</em>');
        // Headings
        html = html.replace(/^### (.+)$/gm, '<strong style="font-size:14px">$1</strong>');
        html = html.replace(/^## (.+)$/gm, '<strong style="font-size:15px">$1</strong>');
        // Tables
        html = _renderTables(html);
        // Line breaks (but not inside <pre>)
        html = html.replace(/\n/g, '<br>');
        return html;
    }

    function _renderTables(html) {
        var lines = html.split('<br>');
        var out = [];
        var tableLines = [];
        var inTable = false;
        for (var i = 0; i < lines.length; i++) {
            var line = lines[i].trim();
            if (line.startsWith('|') && line.endsWith('|')) {
                if (!inTable) { inTable = true; tableLines = []; }
                tableLines.push(line);
            } else {
                if (inTable) { out.push(_buildTbl(tableLines)); inTable = false; tableLines = []; }
                out.push(lines[i]);
            }
        }
        if (inTable) out.push(_buildTbl(tableLines));
        return out.join('');
    }

    function _buildTbl(lines) {
        if (lines.length < 2) return lines.join('<br>');
        var html = '<table>';
        var headers = lines[0].split('|').filter(function (c) { return c.trim(); });
        html += '<thead><tr>';
        for (var h = 0; h < headers.length; h++) html += '<th>' + headers[h].trim() + '</th>';
        html += '</tr></thead><tbody>';
        for (var r = 2; r < lines.length; r++) {
            var cells = lines[r].split('|').filter(function (c) { return c.trim(); });
            html += '<tr>';
            for (var c = 0; c < cells.length; c++) html += '<td>' + cells[c].trim() + '</td>';
            html += '</tr>';
        }
        html += '</tbody></table>';
        return html;
    }

    // ==================== Private: Utils ====================

    function _esc(text) {
        if (!text) return '';
        return String(text).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    function _escJs(str) {
        return str.replace(/\\/g, '\\\\').replace(/'/g, "\\'").replace(/\n/g, '\\n');
    }

    function _getLocale() {
        return (window.__dashboard && window.__dashboard.CURRENT_LOCALE) ||
               (window.CURRENT_LOCALE) ||
               localStorage.getItem('dashboard_locale') || 'zh-CN';
    }

})();
