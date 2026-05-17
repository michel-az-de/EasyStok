// HUD topo sticky — logo, search trigger, status auth, theme/lang/sound toggles, ping.
// Renderiza estado puro a partir do store; eventos de toggle delegados pra app.js / handlers globais.

import * as store from '../store.js';
import * as router from '../router.js';
import { icon } from './icons.js';

export function renderHud(state) {
    const { auth, theme, lang, soundOn, health, route } = state;
    const userLabel = auth?.user?.name || auth?.user?.email || '';
    const authText = auth?.token ? `🔐 ${escapeHtml(userLabel || 'authenticated')}` : '🔓 GUEST';
    const themeLabel = theme === 'light' ? 'sun' : 'moon';
    const langLabel = lang === 'ptbr' ? 'PT' : 'EN';

    const healthColor = health.ok === true ? 'var(--basil)'
        : health.ok === false ? 'var(--danger)'
        : 'var(--ink-mute)';
    const healthMs = health.ms != null ? `${health.ms}ms` : '—';

    return `
        <header class="es-hud" role="banner">
            <a class="es-hud-brand" href="#/" data-nav>
                <span class="es-hud-brand-mark"></span>
                <span class="es-hud-brand-text">EASYSTOCK <em>· CONSOLE</em></span>
            </a>
            <button class="es-hud-search" id="es-hud-search" type="button" aria-label="Buscar (Ctrl+K)">
                ${icon('search', 16)}
                <span>Buscar endpoints, schemas, tags…</span>
                <kbd>Ctrl K</kbd>
            </button>
            <nav class="es-hud-nav" aria-label="Status">
                <button class="es-hud-chip" id="es-hud-auth" data-active="${!!auth?.token}" type="button" title="${auth?.token ? 'Logado — clique para sair' : 'Fazer login'}">
                    <span>${authText}</span>
                </button>
                <button class="es-hud-icon" id="es-hud-theme" type="button" title="Alternar tema (Ctrl+L)" aria-label="Tema ${theme}">
                    ${icon(themeLabel, 18)}
                </button>
                <button class="es-hud-icon" id="es-hud-sound" type="button" data-active="${soundOn}" title="${soundOn ? 'Sons ligados' : 'Sons desligados'}" aria-label="Sons ${soundOn ? 'ligados' : 'desligados'}">
                    ${icon(soundOn ? 'sound-on' : 'sound-off', 18)}
                </button>
                <button class="es-hud-chip es-hud-lang" id="es-hud-lang" type="button" title="Alternar idioma (Ctrl+I)">
                    <span>${langLabel}</span>
                </button>
                <span class="es-hud-health" title="Status da API" aria-label="Latência ${healthMs}">
                    <span class="es-hud-pulse" style="background:${healthColor}"></span>
                    <span class="es-hud-health-ms">${healthMs}</span>
                </span>
            </nav>
        </header>
    `;
}

function escapeHtml(s) {
    return String(s ?? '').replace(/[&<>"']/g, c => ({
        '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
    })[c]);
}
