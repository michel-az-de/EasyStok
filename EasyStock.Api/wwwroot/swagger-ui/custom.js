/**
 * EasyStock API – Custom Swagger UI JavaScript
 * Features: Language selector (EN / PT-BR) · Dark/Light theme toggle
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
            label:      '🇺🇸 EN',
            docName:    'v1-en',
            docTitle:   'EasyStock API (English)',
        },
        ptbr: {
            code:       'ptbr',
            label:      '🇧🇷 PT-BR',
            docName:    'v1-ptbr',
            docTitle:   'EasyStock API (Português)',
        }
    };

    /* ── Helpers ──────────────────────────────────────────────────────────── */
    function getStoredLang()  { return localStorage.getItem(LANG_KEY)  || 'ptbr'; }
    function getStoredTheme() { return localStorage.getItem(THEME_KEY) || 'light'; }

    function applyTheme(theme) {
        document.body.classList.add('es-theme-transition');
        document.documentElement.setAttribute('data-theme', theme === 'dark' ? 'dark' : '');
        localStorage.setItem(THEME_KEY, theme);
        setTimeout(() => document.body.classList.remove('es-theme-transition'), 300);
    }

    function selectLang(code) {
        localStorage.setItem(LANG_KEY, code);
        // Switch the Swagger UI to the matching document
        const lang = LANGS[code];
        if (!lang) return;
        // The SwaggerUI instance is exposed on window.ui by our Swagger config
        if (window.ui && typeof window.ui.specActions !== 'undefined') {
            window.ui.specActions.updateUrl(`/swagger/${lang.docName}/swagger.json`);
            window.ui.specActions.download(`/swagger/${lang.docName}/swagger.json`);
        } else {
            // Fallback: reload with spec URL param
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
        if (btn) btn.textContent = theme === 'dark' ? '☀️ Light' : '🌙 Dark';
    }

    /* ── Toolbar injection ────────────────────────────────────────────────── */
    function injectToolbar() {
        if (document.getElementById('es-toolbar')) return; // already injected

        const wrapper = document.querySelector('.swagger-ui .topbar .topbar-wrapper');
        if (!wrapper) return;

        const currentLang  = getStoredLang();
        const currentTheme = getStoredTheme();

        const toolbar = document.createElement('div');
        toolbar.id = 'es-toolbar';

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

        /* Separator */
        const sep2 = document.createElement('span');
        sep2.id = 'es-lang-sep';
        sep2.textContent = '|';
        toolbar.appendChild(sep2);

        /* Theme toggle */
        const themeBtn = document.createElement('button');
        themeBtn.id = 'es-theme-btn';
        themeBtn.textContent = currentTheme === 'dark' ? '☀️ Light' : '🌙 Dark';
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
    function init() {
        /* Apply persisted theme immediately */
        applyTheme(getStoredTheme());

        /* Wait for topbar to render, then inject controls */
        const observer = new MutationObserver(() => {
            const topbar = document.querySelector('.swagger-ui .topbar .topbar-wrapper');
            if (topbar) {
                injectToolbar();
                // If lang differs from displayed doc, switch
                const currentLang = getStoredLang();
                const lang = LANGS[currentLang];
                if (lang && window.ui) {
                    const currentUrl = window.ui.getState()?.spec?.url || '';
                    if (currentUrl && !currentUrl.includes(lang.docName)) {
                        selectLang(currentLang);
                    }
                }
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
