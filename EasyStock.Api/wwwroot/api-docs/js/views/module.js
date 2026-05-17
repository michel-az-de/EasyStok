// Module split view: rail à esquerda com lista de endpoints + main com endpoint detail.

import { moduleById, endpointsForModule } from '../modules-config.js';
import { renderHud } from '../components/hud.js';
import { renderEndpoint, renderEndpointEmpty } from './endpoint.js';
import { icon } from '../components/icons.js';
import { escapeHtml } from '../components/markdown.js';

export function renderModule(root, state) {
    const { spec, route } = state;
    const mod = moduleById(route.module);
    if (!mod) {
        root.innerHTML = `${renderHud(state)}<main class="es-main"><p class="es-empty">Módulo não encontrado: ${escapeHtml(route.module)}</p></main>`;
        return;
    }
    const eps = endpointsForModule(spec, mod.id);

    const railFilter = state._methodFilter || 'ALL';
    const filteredEps = railFilter === 'ALL' ? eps : eps.filter(e => e.method === railFilter);
    const methodSet = new Set(eps.map(e => e.method));
    const methodOrder = ['GET', 'POST', 'PUT', 'PATCH', 'DELETE'].filter(m => methodSet.has(m));

    const selected = route.endpointId
        ? eps.find(e => e.id === route.endpointId && e.method === route.method)
        : eps[0] || null;

    root.innerHTML = `
        ${renderHud(state)}
        <main class="es-main es-page-module" data-accent="${mod.accent}">
            <aside class="es-rail" aria-label="Endpoints do módulo">
                <header class="es-rail-head">
                    <a href="#/" class="es-rail-back" data-nav>${icon('arrow-left', 16)} <span>Dashboard</span></a>
                    <div class="es-rail-title">
                        <span class="es-rail-icon">${icon(mod.icon, 24)}</span>
                        <span>
                            <strong>${escapeHtml(mod.name)}</strong>
                            <small>${eps.length} endpoints</small>
                        </span>
                    </div>
                    <p class="es-rail-desc">${escapeHtml(mod.desc)}</p>
                </header>
                <div class="es-rail-filter">
                    <button class="es-chip ${railFilter === 'ALL' ? 'es-chip-on' : ''}" data-filter="ALL">Todos <span>${eps.length}</span></button>
                    ${methodOrder.map(mt => {
                        const c = eps.filter(e => e.method === mt).length;
                        return `<button class="es-chip es-chip-method es-chip-${mt.toLowerCase()} ${railFilter === mt ? 'es-chip-on' : ''}" data-filter="${mt}">${mt} <span>${c}</span></button>`;
                    }).join('')}
                </div>
                <ul class="es-rail-list" role="list">
                    ${filteredEps.map(ep => renderRailItem(ep, route)).join('') || `<li class="es-rail-empty">Nenhum endpoint neste filtro.</li>`}
                </ul>
            </aside>
            <section class="es-detail">
                ${selected ? renderEndpoint(spec, selected, state) : renderEndpointEmpty(mod, eps)}
            </section>
        </main>
    `;
}

function renderRailItem(ep, route) {
    const active = route.endpointId === ep.id && route.method === ep.method;
    const tab = route.tab && route.tab !== 'briefing' ? `?tab=${route.tab}` : '';
    return `
        <li>
            <a class="es-rail-item ${active ? 'es-rail-item-active' : ''} ${ep.deprecated ? 'es-rail-item-dep' : ''}"
               href="#/m/${encodeURIComponent(route.module)}/${ep.method}/${encodeURIComponent(ep.id)}${tab}"
               data-method="${ep.method}"
               data-nav>
                <span class="es-method es-method-${ep.method.toLowerCase()}">${ep.method}</span>
                <code class="es-rail-path">${escapeHtml(ep.path)}</code>
                ${ep.summary ? `<span class="es-rail-summary">${escapeHtml(ep.summary)}</span>` : ''}
                ${ep.deprecated ? '<span class="es-badge es-badge-deprecated">DEP</span>' : ''}
            </a>
        </li>
    `;
}
