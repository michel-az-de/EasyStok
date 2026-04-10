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
        const r = await fetch('/categorias/listar');
        if (!r.ok) return;
        const categorias = await r.json();

        if (!Array.isArray(categorias) || categorias.length === 0) return;

        nav.innerHTML = '';
        categorias.forEach(cat => {
            const a = document.createElement('a');
            a.href = '/estoque?categoria=' + encodeURIComponent(cat.id);
            a.className = 'ni-sub';
            a.textContent = cat.nome;
            nav.appendChild(a);
        });
    } catch { /* silent: operação em background, falha não-crítica */ }
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
    } catch { /* silent: operação em background, falha não-crítica */ }
}
