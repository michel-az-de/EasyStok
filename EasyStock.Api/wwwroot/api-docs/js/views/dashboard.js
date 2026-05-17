// Dashboard: splash com info markdown + grid de 8 módulos.

import { modulesWithCounts } from '../modules-config.js';
import { escapeHtml } from '../components/markdown.js';
import { icon } from '../components/icons.js';
import { renderHud } from '../components/hud.js';

export function renderDashboard(root, state) {
    const { spec } = state;
    const { info, counts } = spec;
    const mods = modulesWithCounts(spec);

    const cacheBadge = spec._fromCache
        ? `<span class="es-badge es-badge-cache">${spec._offline ? 'OFFLINE' : 'CACHE'}</span>`
        : '';

    root.innerHTML = `
        ${renderHud(state)}
        <main class="es-main es-page-dashboard">
            <section class="es-splash">
                <div class="es-splash-grid">
                    <aside class="es-splash-headline">
                        <span class="es-section-tag">// visão geral</span>
                        <h1>${escapeHtml(info.title || 'EasyStock API')} ${cacheBadge}</h1>
                        <p class="es-splash-meta">${mods.filter(m => m.count).length} módulos · ${counts.tags} tags · ${formatVersion(info.version)}</p>
                    </aside>
                    <div class="es-quickstart">
                        <span class="es-section-tag">// quick start</span>
                        <ol class="es-qs-steps">
                            <li>
                                <span class="es-qs-num">01</span>
                                <span class="es-qs-body">
                                    <strong>Registre empresa e usuário</strong>
                                    <code>POST /api/empresas/registrar</code>
                                </span>
                            </li>
                            <li>
                                <span class="es-qs-num">02</span>
                                <span class="es-qs-body">
                                    <strong>Faça login e copie o token</strong>
                                    <code>POST /api/auth/login</code>
                                </span>
                            </li>
                            <li>
                                <span class="es-qs-num">03</span>
                                <span class="es-qs-body">
                                    <strong>Clique em Authorize e explore</strong>
                                    <a href="#/m/auth" data-nav class="es-qs-cta">Ir para Auth →</a>
                                </span>
                            </li>
                        </ol>
                    </div>
                </div>
            </section>
            <section class="es-modules">
                <header class="es-section-header">
                    <span class="es-section-tag">// módulos</span>
                    <h2>Escolha um módulo para explorar</h2>
                    <p>${spec.byTag.size} tags agrupadas em ${mods.filter(m => m.count).length} módulos ativos. Use Ctrl+K para buscar por endpoints, schemas e tags.</p>
                </header>
                <div class="es-modules-grid">
                    ${mods.map((m, i) => renderCard(m, i)).join('')}
                </div>
            </section>
            <section class="es-foot">
                <a href="#/schemas" class="es-foot-link" data-nav>${icon('book', 16)} Schemas codex (${counts.schemas})</a>
                <a href="/swagger" class="es-foot-link" target="_blank" rel="noopener">${icon('grid', 16)} Swagger clássico</a>
                <span class="es-foot-meta">${spec._fromCache ? `Cache local · ${new Date().toLocaleTimeString('pt-BR')}` : 'Spec live'}</span>
            </section>
        </main>
    `;
}

function formatVersion(v) {
    const s = String(v || '1');
    return /^v/i.test(s) ? escapeHtml(s) : 'v' + escapeHtml(s);
}

function renderCard(m, i) {
    const dim = !m.count;
    return `
        <a class="es-module-card" data-accent="${m.accent}" data-mod="${escapeHtml(m.id)}" data-dim="${dim}" href="#/m/${encodeURIComponent(m.id)}">
            <span class="es-module-num">0${i + 1}</span>
            <span class="es-module-icon">${icon(m.icon, 32)}</span>
            <span class="es-module-name">${escapeHtml(m.name)}</span>
            <p class="es-module-desc">${escapeHtml(m.desc)}</p>
            <div class="es-module-meta">
                <span class="es-module-count">${m.count}</span>
                <span class="es-module-count-label">endpoints</span>
            </div>
            <span class="es-module-arrow" aria-hidden="true">${icon('chevron', 18)}</span>
        </a>
    `;
}
