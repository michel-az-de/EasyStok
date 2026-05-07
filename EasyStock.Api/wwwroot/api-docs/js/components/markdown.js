// Wrapper marked + DOMPurify — sanitiza markdown antes de injetar.
// `marked` e `DOMPurify` são carregados via CDN com defer; esperar `window.marked` e `window.DOMPurify`.

let configured = false;

function configure() {
    if (configured || typeof window.marked === 'undefined') return;
    if (typeof window.marked.use === 'function') {
        window.marked.use({
            gfm: true,
            breaks: false
        });
    }
    configured = true;
}

export function md(text) {
    if (text == null || text === '') return '';
    if (typeof window.marked === 'undefined' || typeof window.DOMPurify === 'undefined') {
        return escapeHtml(String(text));
    }
    configure();
    let html;
    try { html = window.marked.parse(String(text)); }
    catch { return escapeHtml(String(text)); }
    return window.DOMPurify.sanitize(html, {
        ADD_ATTR: ['target', 'rel'],
        FORBID_TAGS: ['style', 'script'],
        FORBID_ATTR: ['onerror', 'onload', 'onclick']
    });
}

function escapeHtml(s) {
    return s.replace(/[&<>"']/g, c => ({
        '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
    })[c]);
}

export { escapeHtml };
