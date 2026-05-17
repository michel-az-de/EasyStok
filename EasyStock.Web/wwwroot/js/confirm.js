/* confirm.js — substitui confirm() nativo por modal rico (_ConfirmModal).
 *
 * Uso programatico:
 *   window.easyConfirm({
 *     titulo: 'Estornar movimento?',
 *     mensagem: 'O valor sera revertido. Esta acao podera ser refeita.',
 *     textoBotao: 'Estornar',
 *     classBotao: 'bg-red-600 hover:bg-red-700'
 *   }, function () { form.submit(); });
 *
 * Uso declarativo em forms:
 *   <form ... data-confirm='{"titulo":"X", "mensagem":"Y?", "textoBotao":"Confirmar"}'>
 *     @Html.AntiForgeryToken()
 *     <button type="submit">Acao</button>
 *   </form>
 *
 *   O global submit handler intercepta, abre o modal e somente submete se confirmado.
 */
(function () {
    'use strict';

    if (window.easyConfirm) return;

    if (!window.__easyConfirmCallbacks) window.__easyConfirmCallbacks = {};

    function genId() {
        return 'cb_' + Date.now().toString(36) + '_' + Math.random().toString(36).slice(2, 8);
    }

    window.easyConfirm = function (opts, callback) {
        if (typeof callback !== 'function') {
            console.warn('[easyConfirm] callback ausente; abortando');
            return;
        }
        const id = genId();
        window.__easyConfirmCallbacks[id] = callback;
        window.dispatchEvent(new CustomEvent('confirm-action', {
            detail: Object.assign({}, opts || {}, { callbackId: id })
        }));
    };

    document.addEventListener('submit', function (ev) {
        const form = ev.target;
        if (!(form instanceof HTMLFormElement)) return;
        if (!form.hasAttribute('data-confirm')) return;
        if (form.dataset.confirmed === 'true') return;

        ev.preventDefault();
        let opts = {};
        try { opts = JSON.parse(form.dataset.confirm) || {}; }
        catch (_) { /* malformed JSON: usar defaults */ }

        window.easyConfirm(opts, function () {
            form.dataset.confirmed = 'true';
            try { form.submit(); }
            catch (e) { console.error('[easyConfirm] submit', e); }
        });
    }, true);
})();
