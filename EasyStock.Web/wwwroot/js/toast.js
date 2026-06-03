/**
 * EasyStock Toast System v2
 * window.showToast(message, type, duration?, undoAction?)
 * type: 'success' | 'success-undo' | 'error' | 'warning' | 'info'
 *
 * C3 (2026-05-16): botao "Desfazer" agora APENAS em type === 'success-undo'
 * com undoAction funcao. Antes, qualquer toast com 4o argumento callback
 * mostrava o botao - causou bug "Thatiane viu DESFAZER em busca por codigo
 * inexistente".
 */
(function () {
    'use strict';

    const CONFIG = {
        success:        { bg: '#059669', icon: '✓' },
        'success-undo': { bg: '#059669', icon: '✓' }, // C3: alias visual de success
        error:          { bg: '#DC2626', icon: '✕' },
        warning:        { bg: '#D97706', icon: '⚠' },
        info:           { bg: '#4F46E5', icon: 'ℹ' },
    };

    const DURATION = { success: 3500, 'success-undo': 5000, error: 6000, warning: 5000, info: 3500 };

    let container = null;

    function isMobile() {
        return window.innerWidth < 768;
    }

    function positionContainer() {
        if (!container) return;
        const mobile = isMobile();
        // Bottom nav height on mobile = ~76px (60px nav + safe area)
        const bottomOffset = mobile ? 'calc(env(safe-area-inset-bottom, 0px) + 76px)' : '24px';
        Object.assign(container.style, {
            left:        mobile ? '12px' : 'auto',
            right:       '12px',
            bottom:      bottomOffset,
            width:       mobile ? 'auto' : '360px',
            alignItems:  mobile ? 'stretch' : 'flex-end',
        });
    }

    function getContainer() {
        if (!container) {
            container = document.createElement('div');
            container.id = 'toast-container';
            Object.assign(container.style, {
                position:       'fixed',
                zIndex:         '9998',
                display:        'flex',
                flexDirection:  'column-reverse',
                gap:            '8px',
                pointerEvents:  'none',
            });
            container.setAttribute('role', 'region');
            container.setAttribute('aria-label', 'Notificações');
            container.setAttribute('aria-live', 'polite');
            document.body.appendChild(container);
            window.addEventListener('resize', positionContainer, { passive: true });
        }
        positionContainer();
        return container;
    }

    window.showToast = function (message, type, durationOrOptions, undoAction) {
        type = type || 'info';
        const cfg = CONFIG[type] || CONFIG.info;

        // Aceita assinatura legada (duration: number) ou objeto rico:
        //   { duration?, correlationId?, errorCode?, retryFn?, undoAction? }
        let duration = durationOrOptions;
        let correlationId = null;
        let errorCode = null;
        let retryFn = null;
        if (durationOrOptions && typeof durationOrOptions === 'object') {
            duration = durationOrOptions.duration;
            correlationId = durationOrOptions.correlationId || null;
            errorCode = durationOrOptions.errorCode || null;
            retryFn = typeof durationOrOptions.retryFn === 'function' ? durationOrOptions.retryFn : null;
            undoAction = durationOrOptions.undoAction || undoAction;
        }

        // Force 8s minimum quando há retry/correlationId para o usuário copiar o CID.
        const baseDur = (retryFn || correlationId) ? 8000 : (DURATION[type] || 3500);
        const dur = undoAction ? 5000 : ((duration != null) ? duration : baseDur);
        const c = getContainer();

        // Wrapper handles animation
        const wrapper = document.createElement('div');
        wrapper.className = 'toast';
        Object.assign(wrapper.style, {
            pointerEvents: 'auto',
            borderRadius: '14px',
            overflow:   'hidden',
        });

        if (type === 'error') {
            wrapper.setAttribute('role', 'alert');
            wrapper.setAttribute('aria-live', 'assertive');
            wrapper.setAttribute('aria-atomic', 'true');
        }

        // Body
        const body = document.createElement('div');
        body.className = 'toast__body';
        Object.assign(body.style, {
            display:     'flex',
            alignItems:  'center',
            gap:         '10px',
            color:       '#ffffff',
            padding:     '13px 16px',
            background:  'transparent',
            fontFamily:  'Inter, system-ui, -apple-system, sans-serif',
        });

        const iconEl = document.createElement('span');
        iconEl.textContent = cfg.icon;
        Object.assign(iconEl.style, {
            fontWeight: '700',
            fontSize:   '15px',
            flexShrink: '0',
            lineHeight: '1',
        });

        const msgEl = document.createElement('span');
        msgEl.textContent = message;
        Object.assign(msgEl.style, {
            fontSize:   '13.5px',
            fontWeight: '500',
            flex:       '1',
            lineHeight: '1.4',
        });

        const closeBtn = document.createElement('button');
        closeBtn.innerHTML = '&times;';
        closeBtn.setAttribute('aria-label', 'Fechar');
        Object.assign(closeBtn.style, {
            opacity:    '.7',
            fontSize:   '19px',
            lineHeight: '1',
            cursor:     'pointer',
            background: 'none',
            border:     'none',
            color:      'inherit',
            padding:    '0 0 0 4px',
            flexShrink: '0',
        });

        body.appendChild(iconEl);
        body.appendChild(msgEl);

        // Para erros, anexa código + correlation ID copiável e botão de retry.
        if (errorCode || correlationId) {
            const metaWrap = document.createElement('div');
            Object.assign(metaWrap.style, {
                display:    'flex',
                flexDirection: 'column',
                fontSize:   '10.5px',
                opacity:    '0.85',
                marginLeft: 'auto',
                flexShrink: '0',
                gap:        '2px',
                alignItems: 'flex-end',
            });
            if (errorCode) {
                const codeEl = document.createElement('span');
                codeEl.textContent = errorCode;
                Object.assign(codeEl.style, { fontFamily: "'JetBrains Mono', monospace", fontWeight: '600' });
                metaWrap.appendChild(codeEl);
            }
            if (correlationId) {
                const cidEl = document.createElement('button');
                cidEl.textContent = 'CID: ' + correlationId.slice(0, 8);
                cidEl.title = 'Clique para copiar o ID ' + correlationId;
                Object.assign(cidEl.style, {
                    background: 'none', border: 'none', color: 'inherit',
                    fontFamily: "'JetBrains Mono', monospace", fontSize: '10.5px',
                    cursor: 'pointer', padding: '0', textDecoration: 'underline',
                });
                cidEl.onclick = function () {
                    try { navigator.clipboard.writeText(correlationId); cidEl.textContent = 'copiado ✓'; } catch (e) {}
                };
                metaWrap.appendChild(cidEl);
            }
            body.appendChild(metaWrap);
        }

        if (retryFn) {
            const retryBtn = document.createElement('button');
            retryBtn.textContent = 'Tentar novamente';
            Object.assign(retryBtn.style, {
                background:    'rgba(255,255,255,.22)',
                border:        '1px solid rgba(255,255,255,.45)',
                color:         '#ffffff',
                fontSize:      '12px',
                fontWeight:    '600',
                lineHeight:    '1',
                cursor:        'pointer',
                padding:       '4px 9px',
                borderRadius:  '6px',
                flexShrink:    '0',
                whiteSpace:    'nowrap',
            });
            retryBtn.onclick = function () {
                dismiss();
                try { retryFn(); } catch (e) {}
            };
            body.appendChild(retryBtn);
        }

        // C3 guard: botao "Desfazer" SO em success-undo COM funcao executavel.
        // Antes desse guard, qualquer tipo de toast com 4o argumento callback
        // mostrava "Desfazer" - bug encontrado em busca por codigo inexistente.
        if (type === 'success-undo' && typeof undoAction === 'function') {
            const undoBtn = document.createElement('button');
            undoBtn.textContent = 'Desfazer';
            Object.assign(undoBtn.style, {
                background:    'rgba(255,255,255,.22)',
                border:        '1px solid rgba(255,255,255,.45)',
                color:         '#ffffff',
                fontSize:      '12px',
                fontWeight:    '600',
                lineHeight:    '1',
                cursor:        'pointer',
                padding:       '4px 9px',
                borderRadius:  '6px',
                flexShrink:    '0',
                whiteSpace:    'nowrap',
            });
            undoBtn.onclick = function () {
                dismiss();
                try { undoAction(); } catch (e) { console.warn('[toast] undo falhou:', e); }
            };
            body.appendChild(undoBtn);
        }

        body.appendChild(closeBtn);

        // Progress bar track
        const progressTrack = document.createElement('div');
        progressTrack.className = 'toast__progress-track';
        Object.assign(progressTrack.style, {
            borderTop:   '1px solid rgba(0,0,0,.12)',
        });

        const progressBar = document.createElement('div');
        progressBar.className = 'toast__progress-bar';
        Object.assign(progressBar.style, {
            height:     '3px',
            background: 'rgba(255,255,255,.4)',
            width:      '100%',
            transition: 'width ' + dur + 'ms linear',
        });

        progressTrack.appendChild(progressBar);
        wrapper.appendChild(body);
        wrapper.appendChild(progressTrack);
        c.appendChild(wrapper);

        // Trigger enter animation (double rAF forces layout flush)
        requestAnimationFrame(function () {
            requestAnimationFrame(function () {
                progressBar.style.width = '0%';
            });
        });

        var dismissed = false;
        var autoTimer = null;

        function dismiss() {
            if (dismissed) return;
            dismissed = true;
            clearTimeout(autoTimer);
            wrapper.classList.add('dismissing');
            setTimeout(function () { wrapper.remove(); }, 240);
        }

        closeBtn.onclick = dismiss;
        autoTimer = setTimeout(dismiss, dur);

        // Pause on hover (user is reading)
        wrapper.addEventListener('mouseenter', function () { clearTimeout(autoTimer); });
        wrapper.addEventListener('mouseleave', function () {
            clearTimeout(autoTimer);
            autoTimer = setTimeout(dismiss, 1500);
        });
    };

    // Auto-show server-injected toasts via data-toast on <body>
    document.addEventListener('DOMContentLoaded', function () {
        var toastData = document.body.dataset && document.body.dataset.toast;
        if (toastData) {
            // BUG-22: separa o CorrelationId (formato "type|msg||cid") para o showToast
            // renderizar o CID copiavel, em vez de deixar "||cid" colado na mensagem (ou some-lo).
            var cid = null;
            var cidSep = toastData.indexOf('||');
            if (cidSep >= 0) {
                cid = toastData.slice(cidSep + 2) || null;
                toastData = toastData.slice(0, cidSep);
            }
            var sep  = toastData.indexOf('|');
            var type = sep >= 0 ? toastData.slice(0, sep) : toastData;
            var msg  = sep >= 0 ? toastData.slice(sep + 1) : toastData;
            window.showToast(msg, type, cid ? { correlationId: cid } : undefined);
        }

        // Cross-page toasts via sessionStorage (for fetch-based form submissions)
        try {
            var stored = sessionStorage.getItem('easystok_toast');
            if (stored) {
                sessionStorage.removeItem('easystok_toast');
                var t = JSON.parse(stored);
                if (t && t.msg) window.showToast(t.msg, t.type || 'success');
            }
        } catch(e) {}
    });
})();
