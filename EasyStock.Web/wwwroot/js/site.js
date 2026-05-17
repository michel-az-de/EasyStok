// EasyStok — landing publica
// Burger menu, newsletter inline, toast dismissal, scroll smooth, tracking simples.

(function () {
    'use strict';

    function ready(fn) {
        if (document.readyState !== 'loading') fn();
        else document.addEventListener('DOMContentLoaded', fn);
    }

    function getCsrfToken() {
        const meta = document.querySelector('meta[name="csrf-token"]');
        return meta ? meta.getAttribute('content') : '';
    }

    ready(function () {
        // -------- Burger menu (mobile) --------
        const burger = document.querySelector('.site-burger');
        const mobileNav = document.getElementById('site-mobile-nav');
        if (burger && mobileNav) {
            burger.addEventListener('click', function () {
                const isOpen = !mobileNav.hidden;
                mobileNav.hidden = isOpen;
                burger.setAttribute('aria-expanded', String(!isOpen));
            });
            mobileNav.addEventListener('click', function (e) {
                if (e.target.tagName === 'A') mobileNav.hidden = true;
            });
        }

        // -------- Toast dismissal --------
        document.querySelectorAll('.site-toast-close').forEach(function (btn) {
            btn.addEventListener('click', function () {
                const toast = btn.closest('.site-toast');
                if (toast) toast.remove();
            });
        });

        // -------- Newsletter inline --------
        const form = document.getElementById('site-newsletter');
        if (form) {
            const status = document.querySelector('.site-newsletter-status');
            form.addEventListener('submit', async function (ev) {
                ev.preventDefault();
                if (status) { status.className = 'site-newsletter-status'; status.textContent = 'Enviando...'; }

                const fd = new FormData(form);
                try {
                    const res = await fetch('/newsletter', {
                        method: 'POST',
                        body: fd,
                        headers: { 'X-Requested-With': 'fetch' }
                    });
                    const data = await res.json().catch(() => ({}));
                    if (res.ok && data.success) {
                        if (status) { status.className = 'site-newsletter-status ok'; status.textContent = data.message || 'Beleza, você está na lista.'; }
                        form.reset();
                    } else {
                        if (status) { status.className = 'site-newsletter-status err'; status.textContent = data.message || 'Não deu pra inscrever agora. Tenta de novo.'; }
                    }
                } catch (err) {
                    if (status) { status.className = 'site-newsletter-status err'; status.textContent = 'Erro de rede. Tenta de novo.'; }
                }
            });
        }

        // -------- Scroll smooth para ancoras --------
        document.querySelectorAll('a[href^="#"]').forEach(function (a) {
            a.addEventListener('click', function (ev) {
                const id = a.getAttribute('href');
                if (id && id.length > 1) {
                    const el = document.querySelector(id);
                    if (el) {
                        ev.preventDefault();
                        el.scrollIntoView({ behavior: 'smooth', block: 'start' });
                        history.replaceState(null, '', id);
                    }
                }
            });
        });

        // -------- Tracking client-side simples (data-track) --------
        // Apenas console.debug por enquanto; integrar Plausible/GA4 depois.
        document.querySelectorAll('[data-track]').forEach(function (el) {
            el.addEventListener('click', function () {
                if (window.console && console.debug) {
                    console.debug('[track]', el.getAttribute('data-track'));
                }
            });
        });
    });
})();
