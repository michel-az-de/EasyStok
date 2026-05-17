// Endpoint detail: cabeçalho HUD + tabs (briefing/schema/examples/try/code).
// Fase 2: briefing/schema/examples completos. Try It e Code com placeholder; Fase 3 implementa.

import { md, escapeHtml } from '../components/markdown.js';
import { schemaTree } from '../components/schema-tree.js';
import { icon } from '../components/icons.js';
import { resolveRef } from '../parser.js';
import { renderTryIt, defaultBody, requiresIdempotency } from '../components/try-it.js';
import { generateCurl, generateFetch } from '../components/code.js';

const TABS = [
    { id: 'briefing', label: 'Visão geral', icon: 'book' },
    { id: 'schema',   label: 'Schema',      icon: 'grid' },
    { id: 'examples', label: 'Exemplos',    icon: 'flag' },
    { id: 'try',      label: 'Testar',      icon: 'play' },
    { id: 'code',     label: 'Código',      icon: 'copy' }
];

export function renderEndpointEmpty(mod, eps) {
    return `
        <div class="es-endpoint-empty">
            <span class="es-endpoint-empty-icon">${icon(mod.icon, 64)}</span>
            <h2>${escapeHtml(mod.name)}</h2>
            <p>${eps.length} endpoints disponíveis — selecione um na lista ao lado.</p>
            <div class="es-endpoint-empty-tip">
                <kbd>↑</kbd> <kbd>↓</kbd> navega · <kbd>Enter</kbd> abre · <kbd>/</kbd> filtra
            </div>
        </div>
    `;
}

export function renderEndpoint(spec, ep, state) {
    const tab = state.route.tab || 'briefing';
    const requiresAuth = (ep.security && ep.security.length > 0) || (ep.security && ep.security.length === 0 && (spec.raw.security || []).length > 0);

    const responseCodes = Object.keys(ep.responses).sort();

    return `
        <article class="es-endpoint" data-method="${ep.method}">
            <header class="es-ep-head">
                <div class="es-ep-head-row">
                    <span class="es-ep-method es-method-${ep.method.toLowerCase()}">${ep.method}</span>
                    <code class="es-ep-path">${formatPath(ep.path)}</code>
                </div>
                ${ep.summary ? `<h2 class="es-ep-summary">${escapeHtml(ep.summary)}</h2>` : ''}
                <div class="es-ep-meta">
                    <span class="es-ep-tag">tag: <a href="#/m/${encodeURIComponent(state.route.module)}">${escapeHtml(ep.tag)}</a></span>
                    ${requiresAuth ? '<span class="es-badge es-badge-auth">AUTH BEARER</span>' : '<span class="es-badge es-badge-public">public</span>'}
                    ${ep.deprecated ? '<span class="es-badge es-badge-deprecated">DEPRECATED</span>' : ''}
                    <span class="es-ep-codes">
                        ${responseCodes.map(c => `<span class="es-ep-code es-ep-code-${codeClass(c)}">${escapeHtml(c)}</span>`).join('')}
                    </span>
                </div>
            </header>
            <nav class="es-ep-tabs" role="tablist">
                ${TABS.map(t => `
                    <a class="es-ep-tab ${tab === t.id ? 'es-ep-tab-on' : ''}"
                       role="tab"
                       aria-selected="${tab === t.id}"
                       href="${tabHref(state.route, t.id)}"
                       data-nav>
                        ${icon(t.icon, 14)}
                        <span>${t.label}</span>
                    </a>
                `).join('')}
            </nav>
            <div class="es-ep-tabpanel" role="tabpanel">
                ${renderTab(spec, ep, tab, state)}
            </div>
        </article>
    `;
}

function tabHref(route, tabId) {
    const base = `#/m/${encodeURIComponent(route.module)}/${route.method}/${encodeURIComponent(route.endpointId)}`;
    return tabId === 'briefing' ? base : `${base}?tab=${tabId}`;
}

function renderTab(spec, ep, tab, state) {
    switch (tab) {
        case 'briefing':  return renderBriefing(spec, ep);
        case 'schema':    return renderSchemaTab(spec, ep);
        case 'examples':  return renderExamples(spec, ep, state);
        case 'try':       return renderTryIt(spec, ep, state);
        case 'code':      return renderCode(spec, ep, state);
        default:          return renderBriefing(spec, ep);
    }
}

function renderBriefing(spec, ep) {
    const params = ep.parameters || [];
    const groups = ['path', 'query', 'header', 'cookie'].map(loc => ({
        loc,
        items: params.filter(p => p.in === loc)
    })).filter(g => g.items.length);

    const responseRows = Object.entries(ep.responses).sort().map(([code, resp]) => `
        <tr>
            <td><span class="es-ep-code es-ep-code-${codeClass(code)}">${escapeHtml(code)}</span></td>
            <td>${escapeHtml(resp.description || '')}</td>
            <td>${respMediaTypes(resp)}</td>
        </tr>
    `).join('');

    return `
        <div class="es-tab-grid">
            ${ep.description ? `<section class="es-tab-block">
                <h3>Descrição</h3>
                <div class="es-md">${md(ep.description)}</div>
            </section>` : ''}
            ${groups.length ? `<section class="es-tab-block">
                <h3>Parâmetros</h3>
                ${groups.map(g => {
                    const hasDesc = g.items.some(p => p.description);
                    return `
                    <h4 class="es-param-loc">${g.loc}</h4>
                    <table class="es-param-table">
                        <thead><tr><th>nome</th><th>tipo</th><th>req</th>${hasDesc ? '<th>descrição</th>' : ''}</tr></thead>
                        <tbody>
                            ${g.items.map(p => `
                                <tr>
                                    <td><code>${escapeHtml(p.name)}</code></td>
                                    <td><span class="es-st-type">${escapeHtml((p.schema && p.schema.type) || 'string')}${p.schema && p.schema.format ? ':' + escapeHtml(p.schema.format) : ''}</span></td>
                                    <td>${p.required ? '<span class="es-st-req-badge">req</span>' : '<span class="es-mute">—</span>'}</td>
                                    ${hasDesc ? `<td>${escapeHtml(p.description || '')}</td>` : ''}
                                </tr>
                            `).join('')}
                        </tbody>
                    </table>`;
                }).join('')}
            </section>` : ''}
            <section class="es-tab-block">
                <h3>Respostas</h3>
                <table class="es-resp-table">
                    <thead><tr><th>código</th><th>descrição</th><th>media types</th></tr></thead>
                    <tbody>${responseRows}</tbody>
                </table>
            </section>
        </div>
    `;
}

function respMediaTypes(resp) {
    if (!resp.content) return '<span class="es-mute">—</span>';
    return Object.keys(resp.content).map(m => `<code>${escapeHtml(m)}</code>`).join(' ');
}

function renderSchemaTab(spec, ep) {
    const reqBody = ep.requestBody;
    const reqSchema = reqBody?.content?.['application/json']?.schema;
    const okResp = ep.responses['200'] || ep.responses['201'] || ep.responses['default'];
    const okSchema = okResp?.content?.['application/json']?.schema;

    return `
        <div class="es-tab-grid">
            <section class="es-tab-block">
                <h3>Request body${reqBody?.required ? ' <span class="es-st-req-badge">required</span>' : ''}</h3>
                ${reqSchema
                    ? schemaTree(spec, reqSchema)
                    : `<p class="es-mute">${reqBody ? 'sem schema JSON' : 'esse endpoint não recebe body'}</p>`}
            </section>
            <section class="es-tab-block">
                <h3>Response 200/201</h3>
                ${okSchema
                    ? schemaTree(spec, okSchema)
                    : '<p class="es-mute">sem schema JSON para a resposta de sucesso</p>'}
            </section>
        </div>
    `;
}

function renderExamples(spec, ep, state) {
    // Examples vêm de:
    // 1) requestBody.content[*].examples (objeto nomeado)
    // 2) requestBody.content[*].example (single)
    // 3) requestBody.content[*].schema.example
    // 4) responses[*].content[*].examples
    // 5) responses[*].content[*].example
    // 6) responses[*].content[*].schema.example

    const reqExamples = collectExamples(ep.requestBody?.content?.['application/json'], spec);
    const respGroups = Object.entries(ep.responses)
        .map(([code, resp]) => ({ code, examples: collectExamples(resp.content?.['application/json'], spec) }))
        .filter(g => g.examples.length);

    if (reqExamples.length === 0 && respGroups.length === 0) {
        return `<p class="es-mute es-tab-empty">Sem exemplos cadastrados. Consulte a aba <strong>Schema</strong> para ver a estrutura ou <strong>Testar</strong> para executar.</p>`;
    }

    return `
        <div class="es-tab-grid">
            ${reqExamples.length ? `<section class="es-tab-block">
                <h3>Request</h3>
                ${renderExampleSwitcher(reqExamples, 'req')}
            </section>` : ''}
            ${respGroups.map(g => `<section class="es-tab-block">
                <h3>Response <span class="es-ep-code es-ep-code-${codeClass(g.code)}">${escapeHtml(g.code)}</span></h3>
                ${renderExampleSwitcher(g.examples, `resp-${g.code}`)}
            </section>`).join('')}
        </div>
    `;
}

function collectExamples(media, spec) {
    if (!media) return [];
    const out = [];
    if (media.examples) {
        for (const [name, ex] of Object.entries(media.examples)) {
            out.push({ name, summary: ex.summary || name, value: ex.value });
        }
    }
    if (media.example !== undefined) {
        out.push({ name: 'default', summary: 'Default', value: media.example });
    }
    // Resolve $ref do schema antes de buscar example
    const sch = resolveSchema(spec, media.schema);
    if (sch && sch.example !== undefined) {
        out.push({ name: 'schema', summary: 'Do schema', value: sch.example });
    }
    return out;
}

function resolveSchema(spec, schema) {
    if (!spec || !schema) return schema;
    if (typeof schema.$ref === 'string') {
        const r = resolveRef(spec, schema, new Set());
        return r.__resolved || null;
    }
    return schema;
}

function renderExampleSwitcher(examples, key) {
    const tabs = examples.map((ex, i) => `
        <button class="es-ex-tab ${i === 0 ? 'es-ex-tab-on' : ''}" data-ex-key="${key}" data-ex-idx="${i}" type="button">${escapeHtml(ex.summary || ex.name)}</button>
    `).join('');
    const panels = examples.map((ex, i) => `
        <pre class="es-code es-code-json" data-ex-key="${key}" data-ex-idx="${i}" ${i === 0 ? '' : 'hidden'}>${escapeHtml(JSON.stringify(ex.value, null, 2))}</pre>
    `).join('');
    return `
        <div class="es-ex">
            <div class="es-ex-tabs" role="tablist">${tabs}</div>
            <div class="es-ex-panels">${panels}</div>
        </div>
    `;
}

function renderCode(spec, ep, state) {
    // Monta um request "exemplo" com body padrão + auth se houver, pra gerar cURL/fetch reais.
    const params = { path: {}, query: {}, headers: {} };
    const body = ep.requestBody ? (state.route.body || defaultBody(spec, ep)) : null;
    const headers = {};
    if (state.auth?.token) headers['Authorization'] = `Bearer ${state.auth.token.slice(0, 8)}…`;
    else headers['Authorization'] = 'Bearer <SEU_TOKEN>';
    if (requiresIdempotency(ep)) headers['X-Idempotency-Key'] = '<UUID>';
    headers['X-Correlation-Id'] = '<UUID>';

    const curl = generateCurl(ep, params, headers, body);
    const fetchJs = generateFetch(ep, params, headers, body);

    return `
        <div class="es-tab-grid">
            <section class="es-tab-block">
                <h3>cURL <small>${state.auth?.token ? 'token logado · masked' : 'placeholder'}</small>
                    <button type="button" class="es-btn es-btn-ghost es-btn-sm" data-action="copy" data-payload-id="curl-block">${icon('copy', 12)} <span>copy</span></button>
                </h3>
                <pre class="es-code es-code-shell" data-payload-id="curl-block">${escapeHtml(curl)}</pre>
            </section>
            <section class="es-tab-block">
                <h3>fetch JS
                    <button type="button" class="es-btn es-btn-ghost es-btn-sm" data-action="copy" data-payload-id="fetch-block">${icon('copy', 12)} <span>copy</span></button>
                </h3>
                <pre class="es-code es-code-shell" data-payload-id="fetch-block">${escapeHtml(fetchJs)}</pre>
            </section>
            <p class="es-mute" style="font-size:12px">
                Use a aba <strong>Testar</strong> para preencher params e body, executar de verdade e copiar o cURL com os valores reais.
            </p>
        </div>
    `;
}

function formatPath(path) {
    return escapeHtml(path).replace(/{([^}]+)}/g, '<span class="es-path-param">{$1}</span>');
}

function codeClass(code) {
    const c = String(code);
    if (c === '200' || c === '201' || c === '204' || c === 'default') return 'ok';
    if (c.startsWith('3')) return 'redirect';
    if (c === '401' || c === '403') return 'auth';
    if (c === '404' || c === '422') return 'client';
    if (c.startsWith('4')) return 'client';
    if (c.startsWith('5')) return 'server';
    return 'other';
}
