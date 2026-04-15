/**
 * EasyStock — Estoque JS
 * - Carrega categorias dinâmicas no sidebar (com cache sessionStorage)
 * - Badge de notificações é gerenciado pelo polling global em _Layout.cshtml
 */

document.addEventListener('DOMContentLoaded', async function () {
    await fetchCategorias();
});

const CAT_CACHE_KEY = 'easystock_categorias';
const CAT_CACHE_TTL = 5 * 60 * 1000; // 5 minutos

async function fetchCategorias() {
    const nav = document.getElementById('cat-nav');
    if (!nav) return;

    // Tentar cache primeiro
    try {
        const cached = sessionStorage.getItem(CAT_CACHE_KEY);
        if (cached) {
            const { data, ts } = JSON.parse(cached);
            if (Date.now() - ts < CAT_CACHE_TTL && Array.isArray(data) && data.length > 0) {
                renderCategorias(nav, data);
                return;
            }
        }
    } catch { /* cache corrompido, buscar do servidor */ }

    try {
        const r = await fetch('/categorias/listar');
        if (!r.ok) return;
        const categorias = await r.json();

        if (!Array.isArray(categorias) || categorias.length === 0) return;

        // Salvar no cache
        try {
            sessionStorage.setItem(CAT_CACHE_KEY, JSON.stringify({ data: categorias, ts: Date.now() }));
        } catch { /* sessionStorage cheio, não-crítico */ }

        renderCategorias(nav, categorias);
    } catch { /* silent: operação em background, falha não-crítica */ }
}

function renderCategorias(nav, categorias) {
    if (nav.children.length > 0) return; // já renderizado
    const fragment = document.createDocumentFragment();
    categorias.forEach(cat => {
        const a = document.createElement('a');
        a.href = '/estoque?categoria=' + encodeURIComponent(cat.id);
        a.className = 'ni-sub';
        a.textContent = cat.nome;
        fragment.appendChild(a);
    });
    nav.appendChild(fragment);
}
