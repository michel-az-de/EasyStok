/* form-submit.js — botão submit com loading state automático.
 *
 * Aplica data-loading=true no botão clicado/submit do form e re-habilita
 * em caso de validação client-side falhar. Também desabilita demais submits
 * pra impedir double-submit acidental.
 */
(function () {
    'use strict';

    document.addEventListener('submit', (e) => {
        const form = e.target;
        if (!(form instanceof HTMLFormElement)) return;
        if (form.hasAttribute('data-no-loading')) return;

        const submits = form.querySelectorAll('button[type=submit], input[type=submit]');
        submits.forEach((b) => {
            if (b.matches('button')) {
                b.dataset._origText = b.innerHTML;
                b.dataset.loading = 'true';
            }
            b.disabled = true;
        });

        // Se a validação HTML5 falhar, o submit não acontece — restauramos.
        if (!form.checkValidity()) {
            setTimeout(() => {
                submits.forEach((b) => { b.disabled = false; if (b.dataset.loading) b.dataset.loading = 'false'; });
            }, 50);
        }
    }, true);
})();
