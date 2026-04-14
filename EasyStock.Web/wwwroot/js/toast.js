/**
 * EasyStock Toast System v2
 * window.showToast(message, type, duration?)
 * type: 'success' | 'error' | 'warning' | 'info'
 */
(function () {
    'use strict';

    const CONFIG = {
        success: { bg: '#059669', icon: '✓' },
        error:   { bg: '#DC2626', icon: '✕' },
        warning: { bg: '#D97706', icon: '⚠' },
        info:    { bg: '#4F46E5', icon: 'ℹ' },
    };

    const DURATION = { success: 3500, error: 6000, warning: 5000, info: 3500 };

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

    window.showToast = function (message, type, duration) {
        type = type || 'info';
        const cfg = CONFIG[type] || CONFIG.info;
        const dur = (duration != null) ? duration : (DURATION[type] || 3500);
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
            var sep  = toastData.indexOf('|');
            var type = sep >= 0 ? toastData.slice(0, sep) : toastData;
            var msg  = sep >= 0 ? toastData.slice(sep + 1) : toastData;
            window.showToast(msg, type);
        }
    });
})();
