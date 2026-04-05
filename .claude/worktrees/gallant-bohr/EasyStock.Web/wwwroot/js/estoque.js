/**
 * EasyStock — Estoque JS
 * - Carrega categorias dinâmicas no sidebar
 * - O modal de saída rápida é gerenciado pelo Alpine.js inline na view
 */

document.addEventListener('DOMContentLoaded', async function () {
    await fetchCategorias();
    await fetchNotifBadge();
});

async function fetchCategorias() {
    const nav = document.getElementById('cat-nav');
    if (!nav) return;

    try {
        const r = await fetch('/produtos/buscar?q=&limit=100');
        if (!r.ok) return;
        const produtos = await r.json();

        const categorias = [...new Set(produtos.map(p => p.categoria).filter(Boolean))].sort();
        if (categorias.length === 0) return;

        const html = categorias.map(cat =>
            `<a href="/estoque?categoria=${encodeURIComponent(cat)}" class="ni-sub">${cat}</a>`
        ).join('');
        nav.innerHTML = html;
    } catch { /* silently ignore */ }
}

async function fetchNotifBadge() {
    const badges = [
        document.getElementById('notif-badge'),
        document.getElementById('topbar-notif-badge')
    ];
    if (!badges.some(Boolean)) return;

    try {
        const r = await fetch('/notificacoes/badge');
        if (!r.ok) return;
        const data = await r.json();
        const count = data.count || data.naoLidas || 0;
        if (count > 0) {
            badges.forEach(b => {
                if (!b) return;
                b.textContent = count > 99 ? '99+' : count;
                b.classList.remove('hidden');
            });
        }
    } catch { /* endpoint can be absent in dev */ }
}
