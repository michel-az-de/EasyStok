/**
 * EasyStock — Estoque JS
 * - Carrega categorias dinâmicas no sidebar (com cache sessionStorage)
 * - Badge de notificações é gerenciado pelo polling global em _Layout.cshtml
 */

document.addEventListener('DOMContentLoaded', async function () {
    await fetchCategorias();
});

const CAT_CACHE_TTL = 5 * 60 * 1000; // 5 minutos

// Chave com escopo empresa:loja (issue 806): a chave global fazia o sidebar mostrar
// as categorias da loja ANTERIOR por ate 5 min apos trocar de loja na mesma aba.
function catCacheKey() {
    const scope = document.querySelector('meta[name="es-scope"]')?.content || '';
    return 'easystock_categorias:' + scope;
}

// Invalidacao p/ fluxos que criam categoria sem recarregar (Produtos/Form, modal de
// Pedidos) ou antes do reload (tela de Categorias). Exclusao fica so com o TTL:
// um link de categoria removida por ate 5 min leva a um filtro vazio, inocuo.
window.esInvalidateCategorias = function () {
    try { sessionStorage.removeItem(catCacheKey()); } catch { /* nao-critico */ }
};

async function fetchCategorias() {
    const nav = document.getElementById('cat-nav');
    if (!nav) return;

    // Tentar cache primeiro
    try {
        const cached = sessionStorage.getItem(catCacheKey());
        if (cached) {
            const { data, ts } = JSON.parse(cached);
            if (Date.now() - ts < CAT_CACHE_TTL && Array.isArray(data) && data.length > 0) {
                renderCategorias(nav, data);
                return;
            }
        }
    } catch { /* cache corrompido, buscar do servidor */ }

    try {
        const r = await esFetch('/categorias/listar');
        if (!r.ok) return;
        const categorias = await r.json();

        if (!Array.isArray(categorias) || categorias.length === 0) return;

        // Salvar no cache
        try {
            sessionStorage.setItem(catCacheKey(), JSON.stringify({ data: categorias, ts: Date.now() }));
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
