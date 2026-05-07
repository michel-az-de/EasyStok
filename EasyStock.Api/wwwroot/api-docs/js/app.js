// Bootstrap + dispatcher + event delegation.

import * as store from './store.js';
import * as router from './router.js';
import { fetchSpec } from './parser.js';
import { renderBoot } from './views/boot.js';
import { renderDashboard } from './views/dashboard.js';
import { renderModule } from './views/module.js';
import { renderSchemas } from './views/schemas.js';
import { openLoginModal } from './components/auth-modal.js';
import { openSearch } from './components/search.js';
import { collectFormData, executeRequest, renderResult } from './components/try-it.js';
import { copyToClipboard } from './components/code.js';
import { logout, isExpired } from './auth.js';
import * as sound from './sound.js';
import { MODULES } from './modules-config.js';

const root = document.getElementById('root');
const toastEl = document.getElementById('es-toast');

// ─── helpers ─────────────────────────────────────────────────────────────────
function applyTheme(theme) {
    document.documentElement.setAttribute('data-theme', theme === 'light' ? 'light' : 'dark');
}

function applyStage(state) {
    const stage = state.parsing ? 'loading'
        : state.parserError ? 'error'
        : state.booting ? 'boot'
        : 'app';
    document.body.dataset.stage = stage;
}

export function showToast(text, kind = 'info', duration = 3000) {
    if (!toastEl) return;
    toastEl.textContent = text;
    toastEl.dataset.kind = kind;
    toastEl.dataset.visible = 'true';
    clearTimeout(showToast._t);
    showToast._t = setTimeout(() => { delete toastEl.dataset.visible; }, duration);
}
window.__esToast = showToast;

function escHtml(s) {
    return String(s == null ? '' : s).replace(/[&<>"']/g, c => ({
        '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
    })[c]);
}

// ─── render dispatcher ───────────────────────────────────────────────────────
function render(state) {
    applyTheme(state.theme);
    applyStage(state);

    if (state.parsing) {
        root.innerHTML = `
            <div class="es-loading" role="status" aria-live="polite">
                <div class="es-spinner" aria-hidden="true">[<span class="es-spinner-bar"></span>]</div>
                <span class="es-loading-text">Carregando OpenAPI spec…</span>
            </div>`;
        return;
    }
    if (state.parserError) {
        root.innerHTML = `
            <div class="es-error" role="alert">
                <h2>Falha ao carregar a especificação</h2>
                <pre>${escHtml(state.parserError)}</pre>
                <div class="es-error-actions">
                    <button id="es-retry" type="button">Tentar novamente</button>
                    <a href="/swagger" class="es-link">Abrir Swagger clássico</a>
                </div>
            </div>`;
        document.getElementById('es-retry')?.addEventListener('click', () => bootstrap(true));
        return;
    }
    if (!state.spec) return;

    if (state.booting) {
        renderBoot(root, state);
        return;
    }

    switch (state.route.page) {
        case 'module':
        case 'endpoint':
            renderModule(root, state);
            break;
        case 'schemas':
            renderSchemas(root, state);
            break;
        case 'home':
        default:
            renderDashboard(root, state);
            break;
    }
}

// ─── event delegation: clicks ────────────────────────────────────────────────
document.addEventListener('click', async (e) => {
    sound.unlock();

    const navLink = e.target.closest('a[data-nav]');
    if (navLink) {
        const href = navLink.getAttribute('href') || '';
        if (href.startsWith('#/')) {
            e.preventDefault();
            sound.play('click');
            location.hash = href;
        }
        return;
    }

    if (e.target.closest('#es-hud-theme')) {
        store.set({ theme: store.get().theme === 'dark' ? 'light' : 'dark' });
        sound.play('click');
        return;
    }
    if (e.target.closest('#es-hud-sound')) {
        const next = !store.get().soundOn;
        store.set({ soundOn: next });
        if (next) sound.play('success');
        showToast(next ? 'Sons ligados' : 'Sons desligados', 'info', 1400);
        return;
    }
    if (e.target.closest('#es-hud-lang')) {
        const next = store.get().lang === 'ptbr' ? 'en' : 'ptbr';
        store.set({ lang: next });
        sound.play('click');
        bootstrap(true, next);
        return;
    }
    if (e.target.closest('#es-hud-search')) {
        sound.play('open');
        openSearch();
        return;
    }
    if (e.target.closest('#es-hud-auth')) {
        if (store.get().auth?.token) {
            logout();
            sound.play('close');
            showToast('Sessão encerrada', 'info', 1400);
        } else {
            sound.play('open');
            openLoginModal();
        }
        return;
    }

    const filterBtn = e.target.closest('[data-filter]');
    if (filterBtn) {
        store.set({ _methodFilter: filterBtn.dataset.filter });
        sound.play('click');
        return;
    }

    const exTab = e.target.closest('.es-ex-tab');
    if (exTab) {
        const key = exTab.dataset.exKey;
        const idx = exTab.dataset.exIdx;
        document.querySelectorAll(`.es-ex-tab[data-ex-key="${key}"]`).forEach(t => {
            t.classList.toggle('es-ex-tab-on', t.dataset.exIdx === idx);
        });
        document.querySelectorAll(`pre[data-ex-key="${key}"]`).forEach(p => {
            p.toggleAttribute('hidden', p.dataset.exIdx !== idx);
        });
        sound.play('hover');
        return;
    }

    const action = e.target.closest('[data-action]');
    if (action) {
        const act = action.dataset.action;
        if (act === 'open-login') {
            sound.play('open');
            openLoginModal();
            return;
        }
        if (act === 'format-body') {
            const ta = document.querySelector('.es-tryit-body');
            if (ta) {
                try {
                    const obj = JSON.parse(ta.value);
                    ta.value = JSON.stringify(obj, null, 2);
                    updateBodyBytes();
                    sound.play('success');
                    showToast('JSON formatado', 'success', 1200);
                } catch {
                    sound.play('error');
                    showToast('Body não é JSON válido', 'error', 2000);
                }
            }
            return;
        }
        if (act === 'reset-body') {
            const ta = document.querySelector('.es-tryit-body');
            if (ta) {
                ta.value = ta.dataset.default || '';
                updateBodyBytes();
                writePermalinkBody(ta.value);
                sound.play('click');
                showToast('Restaurado', 'info', 1200);
            }
            return;
        }
        if (act === 'copy' || act === 'copy-response') {
            let text = '';
            if (act === 'copy') {
                const id = action.dataset.payloadId;
                const block = document.querySelector(`pre[data-payload-id="${id}"]`);
                text = block?.textContent || '';
            } else {
                const target = document.getElementById('es-tryit-result');
                try {
                    const p = JSON.parse(target?.dataset.payload || '{}');
                    text = p.body || '';
                } catch {}
            }
            const ok = await copyToClipboard(text);
            sound.play(ok ? 'success' : 'error');
            showToast(ok ? 'Copiado' : 'Falha ao copiar', ok ? 'success' : 'error', 1400);
            return;
        }
    }
});

// ─── form submit (Try It) ────────────────────────────────────────────────────
document.addEventListener('submit', async (e) => {
    const form = e.target;
    if (form.id !== 'es-tryit-form') return;
    e.preventDefault();

    const epId = form.closest('.es-tryit')?.dataset.epId;
    const state = store.get();
    const ep = state.spec?.byOperationId?.get(epId);
    if (!ep) {
        showToast('Endpoint não encontrado', 'error');
        return;
    }

    const fireBtn = form.querySelector('#es-tryit-fire');
    const fireSpan = fireBtn?.querySelector('span');
    const target = document.getElementById('es-tryit-result');
    sound.play('fire');
    if (fireBtn) {
        fireBtn.disabled = true;
        if (fireSpan) fireSpan.textContent = 'FIRING…';
        fireBtn.classList.add('es-shake');
        setTimeout(() => fireBtn.classList.remove('es-shake'), 400);
    }

    try {
        if (isExpired()) {
            showToast('Token expirado — refaça o login', 'error', 2400);
        }
        const formData = collectFormData(form);
        const result = await executeRequest(ep, state, formData);
        renderResult(target, result);
        sound.play(result.ok ? 'success' : 'error');
    } catch (err) {
        target.hidden = false;
        target.innerHTML = `<div class="es-tryit-result-card es-tryit-result-server">
            <header class="es-tryit-result-head"><span class="es-tryit-result-status">ERRO</span></header>
            <pre class="es-code">${escHtml(err.message || String(err))}</pre>
        </div>`;
        sound.play('error');
    } finally {
        if (fireBtn) { fireBtn.disabled = false; if (fireSpan) fireSpan.textContent = 'FIRE'; }
    }
});

// ─── input listeners (bytes count + permalink debounced) ────────────────────
let permalinkTimer = null;
document.addEventListener('input', (e) => {
    if (e.target.matches('.es-tryit-body')) {
        updateBodyBytes();
        clearTimeout(permalinkTimer);
        permalinkTimer = setTimeout(() => writePermalinkBody(e.target.value), 600);
    }
});

function updateBodyBytes() {
    const ta = document.querySelector('.es-tryit-body');
    const out = document.getElementById('es-tryit-bytes');
    if (ta && out) out.textContent = ta.value ? new Blob([ta.value]).size + ' bytes' : '';
}

// Atualiza só a URL via replaceState — não dispara re-render (preserva result panel)
function writePermalinkBody(text) {
    try {
        const route = store.get().route;
        if (route.page !== 'endpoint') return;
        const url = new URL(location.href);
        const hash = url.hash || '#/';
        const [pathPart, queryPart] = hash.slice(2).split('?');
        const params = new URLSearchParams(queryPart || '');
        if (text && text.trim()) {
            params.set('body', btoa(unescape(encodeURIComponent(text))));
        } else {
            params.delete('body');
        }
        const q = params.toString();
        const newHash = q ? `#/${pathPart}?${q}` : `#/${pathPart}`;
        if (location.hash !== newHash) {
            history.replaceState({}, '', newHash);
        }
    } catch {}
}

// ─── keyboard shortcuts ─────────────────────────────────────────────────────
document.addEventListener('keydown', (e) => {
    const inField = e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA' || e.target.isContentEditable;

    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
        e.preventDefault();
        sound.play('open');
        openSearch();
        return;
    }
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'l' && !inField) {
        e.preventDefault();
        store.set({ theme: store.get().theme === 'dark' ? 'light' : 'dark' });
        return;
    }
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'i' && !inField) {
        e.preventDefault();
        const next = store.get().lang === 'ptbr' ? 'en' : 'ptbr';
        store.set({ lang: next });
        bootstrap(true, next);
        return;
    }
    if (!inField && e.key === 'Escape' && store.get().route.page !== 'home') {
        location.hash = '#/';
        return;
    }
    // Atalhos contextuais por número (1-8) na home
    if (!inField && !e.ctrlKey && !e.metaKey && !e.altKey && /^[1-8]$/.test(e.key) && store.get().route.page === 'home') {
        const idx = parseInt(e.key, 10) - 1;
        const mod = MODULES[idx];
        if (mod) {
            sound.play('click');
            location.hash = `#/m/${encodeURIComponent(mod.id)}`;
        }
        return;
    }
    // T no endpoint vai pra try
    if (!inField && e.key.toLowerCase() === 't' && store.get().route.page === 'endpoint') {
        e.preventDefault();
        const url = new URL(location.href);
        const hash = url.hash || '#/';
        const [p, q] = hash.slice(2).split('?');
        const params = new URLSearchParams(q || '');
        params.set('tab', 'try');
        location.hash = `#/${p}?${params.toString()}`;
        return;
    }
});

// ─── health ping ─────────────────────────────────────────────────────────────
async function pingHealth() {
    if (store.get().booting || store.get().parsing) return;
    const start = performance.now();
    try {
        const resp = await fetch('/health/live', { method: 'GET', cache: 'no-store' });
        const ms = Math.round(performance.now() - start);
        store.set({ health: { ok: resp.ok, lastCheck: Date.now(), ms } });
    } catch {
        const ms = Math.round(performance.now() - start);
        store.set({ health: { ok: false, lastCheck: Date.now(), ms } });
    }
}
setInterval(pingHealth, 30_000);
setTimeout(pingHealth, 4_000);

// ─── error boundary ─────────────────────────────────────────────────────────
window.addEventListener('error', (ev) => {
    showToast(`Erro: ${ev.message}`, 'error', 5000);
});
window.addEventListener('unhandledrejection', (ev) => {
    showToast(`Erro: ${ev.reason?.message || ev.reason}`, 'error', 5000);
});

// ─── bootstrap ───────────────────────────────────────────────────────────────
async function bootstrap(forceReload = false, langOverride) {
    store.set({ parsing: true, parserError: null });
    try {
        const lang = langOverride || store.get().lang;
        const spec = await fetchSpec(lang);
        store.set({ spec, parsing: false });
        if (spec._fromCache && !forceReload) {
            showToast(`Spec carregada do cache${spec._offline ? ' (offline)' : ''}`, 'info');
        }
    } catch (e) {
        console.error('[bootstrap] parser error', e);
        store.set({ parserError: e.message || String(e), parsing: false, booting: false });
    }
}

// ─── init ────────────────────────────────────────────────────────────────────
applyTheme(store.get().theme);
router.init();
store.subscribe(render);
render(store.get());
bootstrap();
