// Geradores de cURL e fetch JS, syntax highlight de JSON e clipboard copy.

import { escapeHtml } from './markdown.js';

export function buildUrl(ep, params) {
    let path = ep.path;
    for (const [k, v] of Object.entries(params.path || {})) {
        path = path.replace(`{${k}}`, encodeURIComponent(v));
    }
    const qs = new URLSearchParams();
    for (const [k, v] of Object.entries(params.query || {})) {
        if (v != null && v !== '') qs.set(k, v);
    }
    const q = qs.toString();
    return q ? `${path}?${q}` : path;
}

export function generateCurl(ep, params, headers, bodyText) {
    const url = buildUrl(ep, params);
    const lines = [`curl -X ${ep.method} '${url}'`];
    const all = { Accept: 'application/json', ...headers };
    if (bodyText) all['Content-Type'] = 'application/json';
    for (const [k, v] of Object.entries(all)) {
        if (v == null || v === '') continue;
        lines.push(`  -H '${k}: ${String(v).replace(/'/g, "'\\''")}'`);
    }
    if (bodyText) {
        const escaped = bodyText.replace(/'/g, "'\\''");
        lines.push(`  -d '${escaped}'`);
    }
    return lines.join(' \\\n');
}

export function generateFetch(ep, params, headers, bodyText) {
    const url = buildUrl(ep, params);
    const all = { Accept: 'application/json', ...headers };
    if (bodyText) all['Content-Type'] = 'application/json';
    const opts = { method: ep.method, headers: all };
    if (bodyText) opts.body = bodyText;
    const optsJson = JSON.stringify(opts, null, 2);
    return `const resp = await fetch(${JSON.stringify(url)}, ${optsJson});\nconst data = await resp.json();\nconsole.log(resp.status, data);`;
}

export async function copyToClipboard(text) {
    try {
        await navigator.clipboard.writeText(text);
        return true;
    } catch {
        const ta = document.createElement('textarea');
        ta.value = text;
        ta.setAttribute('readonly', '');
        ta.style.position = 'fixed';
        ta.style.opacity = '0';
        document.body.appendChild(ta);
        ta.select();
        let ok = false;
        try { ok = document.execCommand('copy'); } catch {}
        document.body.removeChild(ta);
        return ok;
    }
}

// JSON syntax highlight — escapa primeiro, depois aplica spans.
// Heurística simples mas robusta o suficiente pra responses típicas.
export function highlightJson(text) {
    if (text == null) return '';
    const safe = escapeHtml(String(text));
    return safe
        .replace(/(&quot;(?:\\.|[^&\\])*?&quot;)(\s*:)/g, '<span class="es-j-key">$1</span>$2')
        .replace(/(:\s*)(&quot;(?:\\.|[^&\\])*?&quot;)/g, '$1<span class="es-j-str">$2</span>')
        .replace(/(:\s*)(true|false)\b/g, '$1<span class="es-j-bool">$2</span>')
        .replace(/(:\s*)(null)\b/g, '$1<span class="es-j-null">$2</span>')
        .replace(/(:\s*)(-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?)\b/g, '$1<span class="es-j-num">$2</span>')
        .replace(/(\[\s*)(&quot;(?:\\.|[^&\\])*?&quot;)/g, '$1<span class="es-j-str">$2</span>')
        .replace(/,(\s*)(&quot;(?:\\.|[^&\\])*?&quot;)(?=\s*[,\]])/g, ',$1<span class="es-j-str">$2</span>');
}
