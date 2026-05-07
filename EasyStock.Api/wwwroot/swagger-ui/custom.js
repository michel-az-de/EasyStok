/**
 * EasyStok API – Custom Swagger UI JavaScript
 * Features:
 *  - Botao "Site" linkando pra landing publica (easystok.com.br ou /home em dev)
 *  - Botao "Documentacao" linkando pra README/docs no repo
 *  - Seletor de idioma (EN / PT-BR)
 *  - Toggle de tema (light / dark)
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

    /* ── External links ───────────────────────────────────────────────────── */
    /**
     * Resolve URL da landing publica baseado no host atual.
     * Em prod (easystok.com.br ou app.easystok.com.br): aponta pro dominio publico.
     * Em dev/staging: aponta pra propria origem na rota raiz "/".
     */
    function resolveSiteUrl() {
        const host = window.location.hostname || '';
        // Producao com dominios separados — landing fica no apex
        if (host === 'app.easystok.com.br') return 'https://easystok.com.br/';
        // Producao com dominio unico ou outros casos
        if (host.endsWith('.easystok.com.br') || host === 'easystok.com.br') {
            return 'https://easystok.com.br/';
        }
        // Dev/staging/azurewebsites — landing roda no proprio EasyStock.Web,
        // mas a API e um host separado, entao aponta pro dominio publico oficial.
        // Felipe pode trocar pra "/" se rodar API e Web no mesmo host.
        return 'https://easystok.com.br/';
    }

    const EXTERNAL_LINKS = [
        {
            id:   'es-site-btn',
            label:'🏠 Site',
            title:'Abrir landing publica do EasyStok',
            cta:  true,
            href: resolveSiteUrl(),
            target: '_blank'
        },
        {
            id:   'es-repo-btn',
            label:'📖 Repo',
            title:'Repositorio publico no GitHub',
            href: 'https://github.com/michel-az-de/EasyStok',
            target: '_blank'
        }
    ];

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
        updateLangButtons(code);
    }

    function updateLangButtons(activeLang) {
        document.querySelectorAll('#es-toolbar [data-lang]').forEach(btn => {
            btn.classList.toggle('active', btn.dataset.lang === activeLang);
        });
    }

    function updateThemeButton(theme) {
        const btn = document.getElementById('es-theme-btn');
        if (btn) btn.textContent = theme === 'dark' ? '☀️ Light' : '🌙 Dark';
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

        // Botoes de links externos (Site, Repo) — destacam vinculo com landing
        EXTERNAL_LINKS.forEach(link => {
            const a = document.createElement('a');
            a.id = link.id;
            a.href = link.href;
            a.textContent = link.label;
            a.title = link.title;
            a.target = link.target;
            a.rel = 'noopener noreferrer';
            if (link.cta) a.classList.add('es-cta');
            toolbar.appendChild(a);
        });

        // Separador antes dos toggles
        const sepLinks = document.createElement('span');
        sepLinks.id = 'es-lang-sep';
        sepLinks.textContent = '|';
        toolbar.appendChild(sepLinks);

        // Botoes de idioma
        Object.values(LANGS).forEach((lang, idx) => {
            if (idx > 0) {
                const sep = document.createElement('span');
                sep.id = 'es-lang-sep';
                sep.textContent = '·';
                toolbar.appendChild(sep);
            }
            const btn = document.createElement('button');
            btn.dataset.lang = lang.code;
            btn.textContent  = lang.label;
            btn.title        = `Switch to ${lang.docTitle}`;
            btn.classList.toggle('active', lang.code === currentLang);
            btn.addEventListener('click', () => selectLang(lang.code));
            toolbar.appendChild(btn);
        });

        // Separador antes do theme
        const sepTheme = document.createElement('span');
        sepTheme.id = 'es-lang-sep';
        sepTheme.textContent = '|';
        toolbar.appendChild(sepTheme);

        // Toggle de tema
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
        applyTheme(getStoredTheme());

        const observer = new MutationObserver(() => {
            const topbar = document.querySelector('.swagger-ui .topbar .topbar-wrapper');
            if (topbar) {
                injectToolbar();
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
