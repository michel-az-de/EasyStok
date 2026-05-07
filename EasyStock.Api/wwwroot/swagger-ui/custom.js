/**
 * EasyStock Swagger UI — toolbar customizada.
 * Features:
 *   • Botão Console → redireciona pra /api-docs/ (interface dark sci-fi alternativa)
 *   • Language switcher EN / PT-BR (troca entre os 2 docs OpenAPI)
 *   • Theme toggle Dark (default) / Light
 * Persistência via localStorage.
 */
(function () {
    'use strict';

    /* ── Storage keys ─────────────────────────────────────────────────────── */
    const LANG_KEY  = 'es_swagger_lang';
    const THEME_KEY = 'es_swagger_theme';

    /* ── Language packs ───────────────────────────────────────────────────── */
    const LANGS = {
        en: {
            code:       'en',
            label:      'EN',
            docName:    'v1-en',
            docTitle:   'EasyStock API (English)',
        },
        ptbr: {
            code:       'ptbr',
            label:      'PT-BR',
            docName:    'v1-ptbr',
            docTitle:   'EasyStock API (Português)',
        }
    };

    /* ── Helpers ──────────────────────────────────────────────────────────── */
    function getStoredLang()  { return localStorage.getItem(LANG_KEY)  || 'ptbr'; }
    function getStoredTheme() { return localStorage.getItem(THEME_KEY) || 'dark'; }

    function applyTheme(theme) {
        document.body.classList.add('es-theme-transition');
        document.documentElement.setAttribute('data-theme', theme === 'light' ? 'light' : 'dark');
        // Meta theme-color pra browsers mobile
        let meta = document.querySelector('meta[name="theme-color"]');
        if (!meta) {
            meta = document.createElement('meta');
            meta.name = 'theme-color';
            document.head.appendChild(meta);
        }
        meta.content = theme === 'light' ? '#f5f7fb' : '#0b0908';
        localStorage.setItem(THEME_KEY, theme);
        setTimeout(() => document.body.classList.remove('es-theme-transition'), 300);
    }

    function selectLang(code) {
        localStorage.setItem(LANG_KEY, code);
        const lang = LANGS[code];
        if (!lang) return;
        if (window.ui && typeof window.ui.specActions !== 'undefined') {
            window.ui.specActions.updateUrl(`/swagger/${lang.docName}/swagger.json`);
            window.ui.specActions.download(`/swagger/${lang.docName}/swagger.json`);
        } else {
            const url = new URL(window.location.href);
            url.searchParams.set('urls.primaryName', lang.docTitle);
            window.location.replace(url.toString());
        }
        updateButtons(code);
    }

    function updateButtons(activeLang) {
        document.querySelectorAll('#es-lang-btn').forEach(btn => {
            btn.classList.toggle('active', btn.dataset.lang === activeLang);
        });
    }

    function updateThemeButton(theme) {
        const btn = document.getElementById('es-theme-btn');
        if (btn) btn.textContent = theme === 'dark' ? '☀ Light' : '🌙 Dark';
    }

    /* ── Toolbar injection ────────────────────────────────────────────────── */
    function injectToolbar() {
        if (document.getElementById('es-toolbar')) return;

        const wrapper = document.querySelector('.swagger-ui .topbar .topbar-wrapper');
        if (!wrapper) return;

        const currentLang  = getStoredLang();
        const currentTheme = getStoredTheme();

        const toolbar = document.createElement('div');
        toolbar.id = 'es-toolbar';

        /* Botão Console — destacado, redireciona pra /api-docs/ */
        const consoleLink = document.createElement('a');
        consoleLink.id = 'es-console-link';
        consoleLink.href = '/api-docs/';
        consoleLink.textContent = 'Console';
        consoleLink.title = 'Abrir EasyStock Console (dark sci-fi)';
        toolbar.appendChild(consoleLink);

        /* Separador */
        const sep0 = document.createElement('span');
        sep0.id = 'es-lang-sep';
        sep0.textContent = '·';
        toolbar.appendChild(sep0);

        /* Language buttons */
        Object.values(LANGS).forEach((lang, idx) => {
            if (idx > 0) {
                const sep = document.createElement('span');
                sep.id = 'es-lang-sep';
                sep.textContent = '|';
                toolbar.appendChild(sep);
            }
            const btn = document.createElement('button');
            btn.id        = 'es-lang-btn';
            btn.dataset.lang = lang.code;
            btn.textContent  = lang.label;
            btn.title        = `Switch to ${lang.docTitle}`;
            btn.classList.toggle('active', lang.code === currentLang);
            btn.addEventListener('click', () => selectLang(lang.code));
            toolbar.appendChild(btn);
        });

        /* Separador */
        const sep2 = document.createElement('span');
        sep2.id = 'es-lang-sep';
        sep2.textContent = '·';
        toolbar.appendChild(sep2);

        /* Theme toggle */
        const themeBtn = document.createElement('button');
        themeBtn.id = 'es-theme-btn';
        themeBtn.textContent = currentTheme === 'dark' ? '☀ Light' : '🌙 Dark';
        themeBtn.title = 'Toggle dark/light theme';
        themeBtn.addEventListener('click', () => {
            const next = document.documentElement.getAttribute('data-theme') === 'dark' ? 'light' : 'dark';
            applyTheme(next);
            updateThemeButton(next);
        });
        toolbar.appendChild(themeBtn);

        wrapper.appendChild(toolbar);
    }

    /* ── Initialize on DOM ready ──────────────────────────────────────────── */
    function tryInject() {
        const topbar = document.querySelector('.swagger-ui .topbar .topbar-wrapper');
        if (!topbar) return false;
        injectToolbar();
        const currentLang = getStoredLang();
        const lang = LANGS[currentLang];
        if (lang && window.ui) {
            const currentUrl = window.ui.getState()?.spec?.url || '';
            if (currentUrl && !currentUrl.includes(lang.docName)) {
                selectLang(currentLang);
            }
        }
        return true;
    }

    function init() {
        applyTheme(getStoredTheme());

        // Tenta injetar imediato (caso DOM já esteja pronto)
        if (tryInject()) return;

        // Senão observa mutações (Swagger UI carrega via React após boot)
        const observer = new MutationObserver(() => {
            if (tryInject()) {
                // Continua observando — Swagger UI re-renderiza às vezes e perde a toolbar
            }
        });
        observer.observe(document.body, { childList: true, subtree: true });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
