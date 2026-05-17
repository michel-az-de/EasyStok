// Cmd+K search palette. Substring match em endpoints (path/summary/operationId/tag) + schemas.

import * as store from '../store.js';
import { moduleForTag } from '../modules-config.js';
import { escapeHtml } from './markdown.js';
import { icon } from './icons.js';

let activeIdx = 0;
let currentResults = [];

export function openSearch() {
    closeSearch();
    const modal = document.createElement('div');
    modal.className = 'es-modal es-search-modal';
    modal.id = 'es-search-modal';
    modal.innerHTML = `
        <div class="es-modal-backdrop" data-close></div>
        <div class="es-search-card" role="dialog" aria-modal="true" aria-labelledby="es-search-input">
            <header class="es-search-head">
                <span class="es-search-icon">${icon('search', 18)}</span>
                <input type="text" id="es-search-input" placeholder="Buscar endpoints, schemas e tags…" autocomplete="off" spellcheck="false" aria-label="Buscar">
                <kbd>esc</kbd>
            </header>
            <div class="es-search-results" id="es-search-results"></div>
            <footer class="es-search-foot">
                <span><kbd>↑</kbd><kbd>↓</kbd> navega</span>
                <span><kbd>enter</kbd> abre</span>
                <span><kbd>esc</kbd> fecha</span>
            </footer>
        </div>
    `;
    document.body.appendChild(modal);

    const input = modal.querySelector('#es-search-input');
    const resultsEl = modal.querySelector('#es-search-results');
    const state = store.get();

    function render(query) {
        const q = (query || '').toLowerCase().trim();
        if (!q) {
            resultsEl.innerHTML = `
                <p class="es-search-hint">
                    ${state.spec ? state.spec.endpoints.length : 0} endpoints · ${state.spec ? Object.keys(state.spec.schemas).length : 0} schemas indexados.
                </p>
            `;
            currentResults = [];
            activeIdx = 0;
            return;
        }
        if (!state.spec) {
            resultsEl.innerHTML = `<p class="es-search-hint">Spec não carregada ainda.</p>`;
            return;
        }
        const epMatches = state.spec.endpoints
            .filter(ep =>
                ep.path.toLowerCase().includes(q)
                || (ep.summary || '').toLowerCase().includes(q)
                || (ep.operationId || '').toLowerCase().includes(q)
                || ep.tag.toLowerCase().includes(q)
            )
            .slice(0, 18)
            .map(ep => ({ kind: 'ep', ep, label: ep.path, sub: ep.summary || ep.tag, method: ep.method }));

        const schemaMatches = Object.keys(state.spec.schemas)
            .filter(n => n.toLowerCase().includes(q))
            .slice(0, 8)
            .map(name => ({ kind: 'sch', name, label: name, sub: 'schema' }));

        currentResults = [...epMatches, ...schemaMatches];
        activeIdx = 0;

        if (currentResults.length === 0) {
            resultsEl.innerHTML = `<p class="es-search-hint">Sem resultados para <strong>${escapeHtml(q)}</strong></p>`;
            return;
        }

        resultsEl.innerHTML = currentResults.map((r, i) => {
            if (r.kind === 'ep') {
                const mod = moduleForTag(r.ep.tag);
                const href = `#/m/${encodeURIComponent(mod.id)}/${r.method}/${encodeURIComponent(r.ep.id)}`;
                return `
                    <a class="es-search-row ${i === activeIdx ? 'es-search-row-active' : ''}" data-idx="${i}" href="${href}">
                        <span class="es-method es-method-${r.method.toLowerCase()}">${r.method}</span>
                        <code class="es-search-row-path">${escapeHtml(r.label)}</code>
                        <span class="es-search-row-sub">${escapeHtml(r.sub || '')}</span>
                        <span class="es-search-row-mod">${escapeHtml(mod.name)}</span>
                    </a>
                `;
            }
            const href = `#/schemas/${encodeURIComponent(r.name)}`;
            return `
                <a class="es-search-row ${i === activeIdx ? 'es-search-row-active' : ''}" data-idx="${i}" href="${href}">
                    <span class="es-method es-method-head">SCH</span>
                    <code class="es-search-row-path">${escapeHtml(r.label)}</code>
                    <span class="es-search-row-sub">schema</span>
                </a>
            `;
        }).join('');
    }

    function setActive(idx) {
        if (currentResults.length === 0) return;
        activeIdx = (idx + currentResults.length) % currentResults.length;
        resultsEl.querySelectorAll('.es-search-row').forEach((r, i) => {
            r.classList.toggle('es-search-row-active', i === activeIdx);
            if (i === activeIdx) r.scrollIntoView({ block: 'nearest' });
        });
    }

    function go(target) {
        if (!target) return;
        if (target.kind === 'ep') {
            const mod = moduleForTag(target.ep.tag);
            location.hash = `#/m/${encodeURIComponent(mod.id)}/${target.method}/${encodeURIComponent(target.ep.id)}`;
        } else {
            location.hash = `#/schemas/${encodeURIComponent(target.name)}`;
        }
        closeSearch();
    }

    input.addEventListener('input', (e) => render(e.target.value));
    input.addEventListener('keydown', (e) => {
        if (e.key === 'ArrowDown') { e.preventDefault(); setActive(activeIdx + 1); }
        else if (e.key === 'ArrowUp') { e.preventDefault(); setActive(activeIdx - 1); }
        else if (e.key === 'Enter') { e.preventDefault(); go(currentResults[activeIdx]); }
        else if (e.key === 'Escape') { e.preventDefault(); closeSearch(); }
    });

    modal.addEventListener('click', (e) => {
        const navLink = e.target.closest('a.es-search-row');
        if (navLink) {
            e.preventDefault();
            location.hash = navLink.getAttribute('href');
            closeSearch();
            return;
        }
        if (e.target.closest('[data-close]')) closeSearch();
    });

    modal.addEventListener('mousemove', (e) => {
        const row = e.target.closest('.es-search-row');
        if (row && row.dataset.idx) {
            setActive(Number(row.dataset.idx));
        }
    });

    document.body.style.overflow = 'hidden';
    setTimeout(() => input.focus(), 50);
    render('');
}

export function closeSearch() {
    const m = document.getElementById('es-search-modal');
    if (m) m.remove();
    document.body.style.overflow = '';
}
