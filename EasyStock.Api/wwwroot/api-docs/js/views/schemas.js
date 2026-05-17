// Schemas codex — placeholder Fase 2; expansão na Fase 4.

import { renderHud } from '../components/hud.js';
import { escapeHtml } from '../components/markdown.js';
import { schemaTree } from '../components/schema-tree.js';
import { endpointsUsingSchema } from '../parser.js';
import { icon } from '../components/icons.js';
import { moduleForTag } from '../modules-config.js';

export function renderSchemas(root, state) {
    const { spec, route } = state;
    const names = Object.keys(spec.schemas).sort();
    const selected = route.schema && spec.schemas[route.schema] ? route.schema : null;

    root.innerHTML = `
        ${renderHud(state)}
        <main class="es-main es-page-schemas">
            <aside class="es-rail" aria-label="Schemas">
                <header class="es-rail-head">
                    <a href="#/" class="es-rail-back" data-nav>${icon('arrow-left', 16)} <span>Dashboard</span></a>
                    <div class="es-rail-title">
                        <span class="es-rail-icon">${icon('book', 24)}</span>
                        <span><strong>Schemas codex</strong><small>${names.length} entidades</small></span>
                    </div>
                </header>
                <ul class="es-rail-list" role="list">
                    ${names.map(n => `
                        <li>
                            <a class="es-rail-item ${selected === n ? 'es-rail-item-active' : ''}"
                               href="#/schemas/${encodeURIComponent(n)}" data-nav>
                                <code class="es-rail-path">${escapeHtml(n)}</code>
                            </a>
                        </li>
                    `).join('')}
                </ul>
            </aside>
            <section class="es-detail">
                ${selected ? renderSchemaDetail(spec, selected) : renderSchemaIntro(names.length)}
            </section>
        </main>
    `;
}

function renderSchemaIntro(total) {
    return `
        <div class="es-endpoint-empty">
            <span class="es-endpoint-empty-icon">${icon('book', 64)}</span>
            <h2>Schemas Codex</h2>
            <p>${total} entidades indexadas. Selecione um schema à esquerda para ver propriedades, tipos, exemplos e endpoints relacionados.</p>
        </div>
    `;
}

function renderSchemaDetail(spec, name) {
    const sch = spec.schemas[name];
    const used = endpointsUsingSchema(spec, name);
    return `
        <article class="es-schema-detail">
            <header class="es-ep-head">
                <div class="es-ep-head-row">
                    <span class="es-method es-method-get">SCHEMA</span>
                    <code class="es-ep-path">${escapeHtml(name)}</code>
                </div>
                ${sch.description ? `<p class="es-ep-summary">${escapeHtml(sch.description)}</p>` : ''}
                <div class="es-ep-meta">
                    <span class="es-ep-tag">usado em ${used.length} endpoints</span>
                </div>
            </header>
            <div class="es-tab-grid">
                <section class="es-tab-block">
                    <h3>Estrutura</h3>
                    ${schemaTree(spec, sch)}
                </section>
                <section class="es-tab-block">
                    <h3>Endpoints que referenciam</h3>
                    ${used.length === 0 ? '<p class="es-mute">— nenhum —</p>' : `
                        <ul class="es-eps">
                            ${used.slice(0, 50).map(ep => `
                                <li class="es-ep es-method-${ep.method.toLowerCase()}">
                                    <a href="#/m/${encodeURIComponent(moduleForTag(ep.tag).id)}/${ep.method}/${encodeURIComponent(ep.id)}" data-nav>
                                        <span class="es-method">${ep.method}</span>
                                        <code class="es-path">${escapeHtml(ep.path)}</code>
                                        <span class="es-summary">${escapeHtml(ep.summary || '')}</span>
                                    </a>
                                </li>
                            `).join('')}
                        </ul>
                    `}
                    ${used.length > 50 ? `<p class="es-mute">+ ${used.length - 50} (truncado)</p>` : ''}
                </section>
            </div>
        </article>
    `;
}

