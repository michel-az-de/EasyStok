/**
 * EasyStock — Entradas JS
 * Helpers complementares ao Alpine.js inline das views de entradas
 */

/**
 * Debounce helper
 * @param {Function} fn
 * @param {number} ms
 * @returns {Function}
 */
function debounce(fn, ms) {
    let timer;
    return function (...args) {
        clearTimeout(timer);
        timer = setTimeout(() => fn.apply(this, args), ms);
    };
}

/**
 * Busca produtos via API
 * @param {string} q — termo de busca
 * @returns {Promise<Array>}
 */
async function buscarProduto(q) {
    if (!q || q.length < 2) return [];
    try {
        const r = await esFetch(`/produtos/buscar?q=${encodeURIComponent(q)}`);
        if (!r.ok) return [];
        return await r.json();
    } catch {
        window.showToast('Erro ao buscar produtos.', 'error');
        return [];
    }
}

/**
 * Calcula total da entrada
 * @param {number} qty
 * @param {number} custo
 * @returns {number}
 */
function calcularTotal(qty, custo) {
    return (qty || 0) * (custo || 0);
}
