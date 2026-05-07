// Try It — form auto-gerado de path/query/header/body, FIRE, response panel.
// Executa fetch real com auth JWT (se logado), Idempotency-Key e Correlation-Id auto.

import * as store from '../store.js';
import { resolveRef } from '../parser.js';
import { uuid, authHeader } from '../auth.js';
import { escapeHtml } from './markdown.js';
import { icon } from './icons.js';
import { buildUrl, generateCurl, generateFetch, copyToClipboard, highlightJson } from './code.js';

// Paths cobertos pelo IdempotencyMiddleware do EasyStock.Api/Program.cs.
const IDEM_PATHS = ['/api/itensestoque', '/api/vendas', '/api/movimentacoes', '/api/mobile/vendas'];

export function requiresIdempotency(ep) {
    if (ep.method !== 'POST') return false;
    return IDEM_PATHS.some(p => ep.path.startsWith(p));
}

const AUTO_HEADERS = new Set(['x-idempotency-key', 'x-correlation-id']);

function isAutoHeader(name, ep) {
    const n = (name || '').toLowerCase();
    if (n === 'x-correlation-id') return true;
    if (n === 'x-idempotency-key') return requiresIdempotency(ep);
    return false;
}

export function defaultBody(spec, ep) {
    const media = ep.requestBody?.content?.['application/json'];
    if (!media) return '';
    if (media.example !== undefined) return JSON.stringify(media.example, null, 2);
    if (media.examples) {
        const first = Object.values(media.examples)[0];
        if (first?.value !== undefined) return JSON.stringify(first.value, null, 2);
    }
    let sch = media.schema;
    if (sch?.$ref) {
        const r = resolveRef(spec, sch, new Set());
        sch = r.__resolved || null;
    }
    if (sch?.example !== undefined) return JSON.stringify(sch.example, null, 2);
    return '';
}

export function renderTryIt(spec, ep, state) {
    const params = ep.parameters || [];
    const pathParams = params.filter(p => p.in === 'path');
    const queryParams = params.filter(p => p.in === 'query');
    const declaredHeaders = params
        .filter(p => p.in === 'header' && !['authorization', 'accept', 'content-type'].includes((p.name || '').toLowerCase()))
        .map(p => ({ ...p, _auto: isAutoHeader(p.name, ep) }));

    // Injeta auto-headers que não foram declarados no swagger
    const extraHeaders = [];
    if (requiresIdempotency(ep) && !declaredHeaders.find(p => (p.name || '').toLowerCase() === 'x-idempotency-key')) {
        extraHeaders.push({ name: 'X-Idempotency-Key', in: 'header', schema: { type: 'string', format: 'uuid' }, description: 'Idempotência (gerada auto se vazio)', _auto: true });
    }
    if (!declaredHeaders.find(p => (p.name || '').toLowerCase() === 'x-correlation-id')) {
        extraHeaders.push({ name: 'X-Correlation-Id', in: 'header', schema: { type: 'string', format: 'uuid' }, description: 'Correlation ID (gerado auto)', _auto: true });
    }
    const headerParams = declaredHeaders;

    const initialBody = state.route.body || defaultBody(spec, ep);
    const requiresAuth = (ep.security && ep.security.length > 0);
    const showAuthWarn = requiresAuth && !state.auth?.token;

    return `
        <div class="es-tryit" data-ep-id="${escapeHtml(ep.id)}">
            ${showAuthWarn ? authBanner() : ''}
            <form class="es-tryit-form" id="es-tryit-form" novalidate>
                ${pathParams.length ? renderParamsBlock('Path parameters', pathParams) : ''}
                ${queryParams.length ? renderParamsBlock('Query parameters', queryParams) : ''}
                ${(headerParams.length || extraHeaders.length) ? renderParamsBlock('Headers', [...headerParams, ...extraHeaders]) : ''}
                ${ep.requestBody ? renderBodyBlock(initialBody) : ''}
                <div class="es-tryit-actions">
                    <button type="submit" class="es-btn es-btn-fire" id="es-tryit-fire">
                        ${icon('play', 14)} <span>FIRE</span>
                    </button>
                    <span class="es-tryit-method es-method es-method-${ep.method.toLowerCase()}">${ep.method}</span>
                    <code class="es-tryit-url" id="es-tryit-url">${escapeHtml(ep.path)}</code>
                    <span class="es-tryit-hint">enter pra disparar</span>
                </div>
            </form>
            <section class="es-tryit-result" id="es-tryit-result" aria-live="polite" hidden></section>
        </div>
    `;
}

function authBanner() {
    return `
        <div class="es-tryit-banner es-tryit-banner-auth">
            <span class="es-tryit-banner-icon">${icon('lock', 18)}</span>
            <div>
                <strong>Autenticação JWT obrigatória.</strong>
                <span>Esse endpoint requer token bearer.</span>
            </div>
            <button type="button" class="es-btn es-btn-primary es-btn-sm" data-action="open-login">${icon('lock', 14)} <span>Login</span></button>
        </div>
    `;
}

function renderBodyBlock(initialBody) {
    return `
        <fieldset class="es-tryit-block">
            <legend>Body <small>JSON</small></legend>
            <textarea class="es-tryit-body" name="__body" spellcheck="false" autocomplete="off" data-default="${escapeHtml(initialBody)}">${escapeHtml(initialBody)}</textarea>
            <div class="es-tryit-body-actions">
                <button type="button" class="es-btn es-btn-ghost es-btn-sm" data-action="format-body">Formatar</button>
                <button type="button" class="es-btn es-btn-ghost es-btn-sm" data-action="reset-body">Restaurar exemplo</button>
                <span class="es-tryit-bytes" id="es-tryit-bytes">${initialBody ? new Blob([initialBody]).size + ' bytes' : ''}</span>
            </div>
        </fieldset>
    `;
}

function renderParamsBlock(title, params) {
    if (!params.length) return '';
    return `
        <fieldset class="es-tryit-block">
            <legend>${escapeHtml(title)}</legend>
            ${params.map(p => {
                const sch = p.schema || {};
                const placeholder = p._auto ? '— auto —'
                    : (p.example != null ? String(p.example)
                    : (sch.example != null ? String(sch.example)
                    : (p.description || '')));
                const typeLabel = (sch.type || 'string') + (sch.format ? ':' + sch.format : '');
                return `
                    <label class="es-tryit-row">
                        <span class="es-tryit-row-name">
                            <code>${escapeHtml(p.name)}</code>
                            ${p.required ? '<span class="es-st-req-badge">req</span>' : ''}
                            <span class="es-st-type">${escapeHtml(typeLabel)}</span>
                            ${p._auto ? '<span class="es-tryit-auto">auto</span>' : ''}
                        </span>
                        <input
                            type="text"
                            name="${escapeHtml(p.in)}__${escapeHtml(p.name)}"
                            data-loc="${escapeHtml(p.in)}"
                            data-pname="${escapeHtml(p.name)}"
                            placeholder="${escapeHtml(placeholder)}"
                            ${p.required ? 'required' : ''}
                            spellcheck="false"
                            autocomplete="off"
                        >
                        ${p.description && !p._auto ? `<small class="es-tryit-row-desc">${escapeHtml(p.description)}</small>` : ''}
                    </label>
                `;
            }).join('')}
        </fieldset>
    `;
}

// Coleta valores do form
export function collectFormData(form) {
    const data = { path: {}, query: {}, headers: {}, body: null };
    const inputs = form.querySelectorAll('input[data-loc], textarea[name="__body"]');
    for (const inp of inputs) {
        if (inp.name === '__body') {
            data.body = inp.value;
            continue;
        }
        const loc = inp.dataset.loc;
        const name = inp.dataset.pname;
        const val = inp.value.trim();
        if (loc === 'path' || loc === 'query') {
            if (val) data[loc][name] = val;
        } else if (loc === 'header') {
            if (val) data.headers[name] = val;
        }
    }
    return data;
}

// Executa o request real. Retorna objeto com status/headers/body/elapsed.
export async function executeRequest(ep, state, formData) {
    const headers = { Accept: 'application/json', ...formData.headers };

    // Auth bearer se logado
    Object.assign(headers, authHeader());

    // Auto idempotency e correlation
    if (!headers['X-Idempotency-Key'] && requiresIdempotency(ep)) {
        headers['X-Idempotency-Key'] = uuid();
    }
    if (!Object.keys(headers).some(k => k.toLowerCase() === 'x-correlation-id')) {
        headers['X-Correlation-Id'] = uuid();
    }

    let bodyText = null;
    if (ep.requestBody && formData.body && formData.body.trim()) {
        try { JSON.parse(formData.body); }
        catch (e) { throw new Error('Body inválido — não é JSON: ' + e.message); }
        headers['Content-Type'] = 'application/json';
        bodyText = formData.body;
    }

    const url = buildUrl(ep, formData);
    const start = performance.now();
    let resp;
    try {
        resp = await fetch(url, { method: ep.method, headers, body: bodyText });
    } catch (e) {
        throw new Error(`Falha de rede: ${e.message || e}`);
    }
    const elapsed = Math.round(performance.now() - start);

    const respHeaders = {};
    resp.headers.forEach((v, k) => { respHeaders[k] = v; });
    const respText = await resp.text();
    let respJson = null;
    try { respJson = JSON.parse(respText); } catch {}

    return {
        url,
        method: ep.method,
        requestHeaders: headers,
        requestBody: bodyText,
        status: resp.status,
        statusText: resp.statusText,
        ok: resp.ok,
        responseHeaders: respHeaders,
        responseBody: respText,
        responseJson: respJson,
        elapsed
    };
}

// Truncate seguro pra evitar travar UI com responses gigantes
const MAX_BODY_RENDER = 200_000; // 200KB

// Renderiza painel de resposta. Se 401, mostra banner pra refazer login.
export function renderResult(target, result) {
    const isOk = result.ok;
    const is401 = result.status === 401;
    const sevClass = isOk ? 'es-tryit-result-ok'
        : is401 ? 'es-tryit-result-auth'
        : result.status >= 500 ? 'es-tryit-result-server'
        : 'es-tryit-result-client';

    const headersList = Object.entries(result.responseHeaders)
        .map(([k, v]) => `<dt>${escapeHtml(k)}</dt><dd>${escapeHtml(v)}</dd>`)
        .join('');

    let bodyText = result.responseJson != null
        ? JSON.stringify(result.responseJson, null, 2)
        : (result.responseBody || '');
    let truncatedNote = '';
    if (bodyText.length > MAX_BODY_RENDER) {
        truncatedNote = `<p class="es-mute" style="font-size:11px;padding:0 var(--sp-4)">⚠ truncado em ${MAX_BODY_RENDER} chars · response total ${bodyText.length}</p>`;
        bodyText = bodyText.slice(0, MAX_BODY_RENDER) + '\n…';
    }
    const bodyRendered = result.responseJson != null
        ? `<pre class="es-code es-code-json">${highlightJson(bodyText)}</pre>`
        : `<pre class="es-code">${escapeHtml(bodyText) || '<em class="es-mute">(vazio)</em>'}</pre>`;

    target.hidden = false;
    target.innerHTML = `
        <div class="es-tryit-result-card ${sevClass}">
            <header class="es-tryit-result-head">
                <span class="es-tryit-result-status">${result.status} ${escapeHtml(result.statusText || '')}</span>
                <span class="es-tryit-result-elapsed">${result.elapsed}ms</span>
                <code class="es-tryit-result-url">${escapeHtml(result.method)} ${escapeHtml(result.url)}</code>
                <button type="button" class="es-btn es-btn-ghost es-btn-sm" data-action="copy-response">${icon('copy', 12)} <span>copy</span></button>
            </header>
            ${is401 ? `
                <div class="es-tryit-banner es-tryit-banner-auth">
                    <span class="es-tryit-banner-icon">${icon('alert', 18)}</span>
                    <div><strong>401 — token expirado ou inválido.</strong> Refaça o login pra continuar.</div>
                    <button type="button" class="es-btn es-btn-primary es-btn-sm" data-action="open-login">${icon('lock', 14)} <span>Refazer login</span></button>
                </div>
            ` : ''}
            <details class="es-tryit-result-section" open>
                <summary>Response body</summary>
                ${truncatedNote}
                ${bodyRendered}
            </details>
            <details class="es-tryit-result-section">
                <summary>Response headers <small>(${Object.keys(result.responseHeaders).length})</small></summary>
                <dl class="es-headers-list">${headersList}</dl>
            </details>
            <details class="es-tryit-result-section">
                <summary>Request enviado</summary>
                <dl class="es-headers-list">
                    ${Object.entries(result.requestHeaders).map(([k, v]) => `<dt>${escapeHtml(k)}</dt><dd>${escapeHtml(v)}</dd>`).join('')}
                </dl>
                ${result.requestBody ? `<pre class="es-code es-code-json">${highlightJson(result.requestBody)}</pre>` : ''}
            </details>
        </div>
    `;
    target.dataset.payload = JSON.stringify({
        body: result.responseBody,
        status: result.status,
        elapsed: result.elapsed
    });
}
