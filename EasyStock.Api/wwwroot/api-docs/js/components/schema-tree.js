// Renderiza um schema OpenAPI recursivo com cycle detection.
// Cada $ref resolvido entra em uma stack; se já estiver, vira marker `↻ Nome` clicável.

import { resolveRef } from '../parser.js';
import { escapeHtml } from './markdown.js';

export function schemaTree(spec, schema, opts = {}) {
    const stack = new Set();
    const html = renderNode(spec, schema, stack, 0, opts);
    return `<div class="es-st">${html || '<span class="es-st-empty">(vazio)</span>'}</div>`;
}

function renderNode(spec, node, stack, depth, opts) {
    if (node == null) return '<span class="es-st-empty">null</span>';
    if (typeof node !== 'object') {
        return `<span class="es-st-literal">${escapeHtml(JSON.stringify(node))}</span>`;
    }

    if (typeof node.$ref === 'string') {
        const resolved = resolveRef(spec, node, stack);
        if (resolved.__circular) {
            return `<span class="es-st-circular" title="Referência circular já aberta acima"><a href="#/schemas/${encodeURIComponent(resolved.__ref)}">↻ ${escapeHtml(resolved.__ref)}</a></span>`;
        }
        if (resolved.__unresolved) {
            return `<span class="es-st-unresolved">⚠ $ref ${escapeHtml(resolved.__ref)} não resolvido</span>`;
        }
        const newStack = new Set(stack);
        newStack.add(resolved.__ref);
        return `
            <details class="es-st-ref" ${depth < 2 ? 'open' : ''}>
                <summary>
                    <span class="es-st-ref-icon">$</span>
                    <a href="#/schemas/${encodeURIComponent(resolved.__ref)}" class="es-st-ref-name">${escapeHtml(resolved.__ref)}</a>
                </summary>
                <div class="es-st-ref-body">${renderNode(spec, resolved.__resolved, newStack, depth + 1, opts)}</div>
            </details>
        `;
    }

    if (node.allOf || node.oneOf || node.anyOf) {
        return renderVariants(spec, node, stack, depth, opts);
    }

    if (node.type === 'array' || node.items) {
        return renderArray(spec, node, stack, depth, opts);
    }

    if (node.type === 'object' || node.properties || node.additionalProperties) {
        return renderObject(spec, node, stack, depth, opts);
    }

    return renderPrimitive(node);
}

function renderObject(spec, node, stack, depth, opts) {
    const props = node.properties || {};
    const keys = Object.keys(props);
    const required = new Set(node.required || []);
    if (keys.length === 0) {
        if (node.additionalProperties) {
            return `<div class="es-st-object es-st-object-loose">
                <span class="es-st-type">object</span>
                <span class="es-st-additional">+ propriedades adicionais</span>
            </div>`;
        }
        return '<span class="es-st-empty">{ }</span>';
    }

    const items = keys.map(key => {
        const val = props[key] || {};
        const isReq = required.has(key);
        const typeChip = renderTypeChip(val);
        const desc = val.description ? `<p class="es-st-desc">${escapeHtml(val.description)}</p>` : '';
        const example = val.example !== undefined
            ? `<pre class="es-st-example" title="Exemplo">${escapeHtml(JSON.stringify(val.example))}</pre>`
            : '';
        const enumPart = Array.isArray(val.enum)
            ? `<div class="es-st-enum">${val.enum.map(e => `<code>${escapeHtml(String(e))}</code>`).join('')}</div>`
            : '';
        const constraints = renderConstraints(val);
        const child = (val.$ref || val.properties || val.items || val.allOf || val.oneOf || val.anyOf)
            ? `<div class="es-st-children">${renderNode(spec, val, stack, depth + 1, opts)}</div>`
            : '';
        return `
            <li class="es-st-prop ${isReq ? 'es-st-required' : ''}">
                <div class="es-st-prop-head">
                    <code class="es-st-key">${escapeHtml(key)}</code>
                    ${typeChip}
                    ${isReq ? '<span class="es-st-req-badge">required</span>' : ''}
                    ${val.deprecated ? '<span class="es-st-dep-badge">deprecated</span>' : ''}
                    ${val.readOnly ? '<span class="es-st-meta">readOnly</span>' : ''}
                    ${val.writeOnly ? '<span class="es-st-meta">writeOnly</span>' : ''}
                </div>
                ${desc}
                ${constraints}
                ${enumPart}
                ${example}
                ${child}
            </li>
        `;
    }).join('');

    return `<ul class="es-st-object">${items}</ul>`;
}

function renderArray(spec, node, stack, depth, opts) {
    const items = node.items || {};
    return `
        <div class="es-st-array">
            <span class="es-st-array-label">array of</span>
            <div class="es-st-array-items">${renderNode(spec, items, stack, depth + 1, opts)}</div>
        </div>
    `;
}

function renderVariants(spec, node, stack, depth, opts) {
    const variants = node.allOf || node.oneOf || node.anyOf;
    const label = node.allOf ? 'allOf' : node.oneOf ? 'oneOf' : 'anyOf';
    return `
        <div class="es-st-variants">
            <span class="es-st-variant-label">${label}</span>
            <div class="es-st-variants-body">
                ${variants.map((v, i) => `
                    <details class="es-st-variant" ${i === 0 ? 'open' : ''}>
                        <summary>variante ${i + 1}</summary>
                        <div>${renderNode(spec, v, stack, depth + 1, opts)}</div>
                    </details>
                `).join('')}
            </div>
        </div>
    `;
}

function renderPrimitive(node) {
    const enumPart = Array.isArray(node.enum)
        ? `<div class="es-st-enum">${node.enum.map(e => `<code>${escapeHtml(String(e))}</code>`).join('')}</div>`
        : '';
    const desc = node.description
        ? `<p class="es-st-desc">${escapeHtml(node.description)}</p>`
        : '';
    const example = node.example !== undefined
        ? `<pre class="es-st-example">${escapeHtml(JSON.stringify(node.example))}</pre>`
        : '';
    const constraints = renderConstraints(node);
    return `
        <div class="es-st-primitive">
            ${renderTypeChip(node)}
            ${desc}
            ${constraints}
            ${enumPart}
            ${example}
        </div>
    `;
}

function renderConstraints(node) {
    const parts = [];
    if (node.minLength != null || node.maxLength != null) {
        parts.push(`len: ${node.minLength ?? 0}–${node.maxLength ?? '∞'}`);
    }
    if (node.minimum != null || node.maximum != null) {
        parts.push(`range: ${node.minimum ?? '−∞'}–${node.maximum ?? '∞'}`);
    }
    if (node.pattern) {
        parts.push(`pattern: <code>${escapeHtml(node.pattern)}</code>`);
    }
    if (node.minItems != null || node.maxItems != null) {
        parts.push(`items: ${node.minItems ?? 0}–${node.maxItems ?? '∞'}`);
    }
    if (parts.length === 0) return '';
    return `<div class="es-st-constraints">${parts.join(' · ')}</div>`;
}

function renderTypeChip(node) {
    let label;
    if (node.$ref) {
        label = '$ref';
    } else if (Array.isArray(node.type)) {
        label = node.type.join('|');
    } else if (node.type) {
        label = node.type;
    } else if (node.enum) {
        label = 'enum';
    } else {
        label = 'any';
    }
    if (node.format) label += `:${node.format}`;
    if (node.nullable) label += '?';
    const cls = `es-st-type es-st-type-${(label.split(':')[0] || 'any').replace(/[^a-z0-9]/gi, '_')}`;
    return `<span class="${cls}">${escapeHtml(label)}</span>`;
}
