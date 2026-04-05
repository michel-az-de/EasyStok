/**
 * EasyStock Toast System
 * window.showToast(message, type) — type: 'success' | 'error' | 'warning' | 'info'
 */
(function () {
    const COLORS = {
        success: 'bg-emerald-600',
        error: 'bg-red-600',
        warning: 'bg-amber-500',
        info: 'bg-blue-600'
    };
    const ICONS = {
        success: '✓',
        error: '✕',
        warning: '⚠',
        info: 'ℹ'
    };

    let container;

    function getContainer() {
        if (!container) {
            container = document.createElement('div');
            container.id = 'toast-container';
            container.style.cssText = 'position:fixed;bottom:24px;left:24px;z-index:9999;display:flex;flex-direction:column-reverse;gap:8px;';
            document.body.appendChild(container);
        }
        return container;
    }

    window.showToast = function (message, type = 'info') {
        const c = getContainer();
        const bg = COLORS[type] || COLORS.info;
        const icon = ICONS[type] || ICONS.info;

        const el = document.createElement('div');
        el.style.cssText = 'display:flex;align-items:center;gap:10px;color:white;padding:12px 16px;border-radius:12px;box-shadow:0 8px 24px rgba(0,0,0,.15);min-width:280px;max-width:360px;transition:all .3s;';
        el.className = bg;

        el.innerHTML = `
            <span style="font-weight:700;font-size:16px;">${icon}</span>
            <span style="font-size:14px;font-weight:500;flex:1;">${message}</span>
            <button onclick="this.parentElement.remove()" style="opacity:.7;font-size:18px;line-height:1;cursor:pointer;background:none;border:none;color:inherit;">&times;</button>
        `;

        c.appendChild(el);

        setTimeout(() => {
            el.style.opacity = '0';
            el.style.transform = 'translateY(8px)';
            setTimeout(() => el.remove(), 300);
        }, 3000);
    };

    // Auto-show toasts set by server via data-toast attribute on body
    document.addEventListener('DOMContentLoaded', function () {
        const toastData = document.body.dataset.toast;
        if (toastData) {
            const [type, ...msgParts] = toastData.split('|');
            window.showToast(msgParts.join('|') || type, type);
        }
    });
})();
