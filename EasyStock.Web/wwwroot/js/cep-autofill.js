/* cep-autofill.js — auto-preenchimento de endereço a partir do CEP via ViaCEP.
 *
 * Marque o input de CEP com `data-cep-autofill` e diga o que preencher:
 *
 *   Modo campos (1 campo do ViaCEP → 1 input):
 *     <input data-cep-autofill
 *            data-cep-logradouro='[name="logradouro"]'
 *            data-cep-bairro='[name="bairro"]'
 *            data-cep-cidade='[name="cidade"]'
 *            data-cep-uf='[name="estado"]'
 *            data-cep-focus='[name="numero"]' />
 *
 *   Modo composição (todos os campos → 1 input de texto livre):
 *     <input data-cep-autofill data-cep-compose="#cli-end" data-cep-focus="#cli-end" />
 *
 * Seletores são resolvidos primeiro dentro do <form> mais próximo, senão no document.
 * Best-effort, igual ao ViaCepLookupClient do backend: timeout/4xx/5xx/CEP inexistente
 * não dão erro visível — o usuário segue preenchendo manualmente.
 *
 * Os valores são setados via native setter + dispatch de `input`/`change`, então o
 * Alpine x-model capta o preenchimento programático (gotcha conhecido do projeto).
 */
(function () {
    'use strict';

    const onlyDigits = (s) =>
        (window.EasyMasks && window.EasyMasks.onlyDigits)
            ? window.EasyMasks.onlyDigits(s)
            : (s || '').toString().replace(/\D+/g, '');

    function resolveTarget(cepEl, selector) {
        if (!selector) return null;
        const form = cepEl.closest('form');
        return (form && form.querySelector(selector)) || document.querySelector(selector);
    }

    function setNativeValue(el, value) {
        if (!el) return;
        const proto = el instanceof HTMLTextAreaElement
            ? HTMLTextAreaElement.prototype
            : HTMLInputElement.prototype;
        const setter = Object.getOwnPropertyDescriptor(proto, 'value').set;
        setter.call(el, value);
        el.dispatchEvent(new Event('input', { bubbles: true }));
        el.dispatchEvent(new Event('change', { bubbles: true }));
    }

    function compose(d) {
        const parts = [];
        if (d.logradouro) parts.push(d.logradouro);
        if (d.bairro) parts.push(d.bairro);
        let local = '';
        if (d.localidade) local = d.localidade;
        if (d.uf) local += (local ? '/' : '') + d.uf;
        if (local) parts.push(local);
        return parts.join(', ');
    }

    function fill(cepEl, d) {
        const composeSel = cepEl.dataset.cepCompose;
        if (composeSel) {
            const texto = compose(d);
            if (texto) setNativeValue(resolveTarget(cepEl, composeSel), texto);
        } else {
            const map = {
                cepLogradouro: d.logradouro,
                cepBairro: d.bairro,
                cepCidade: d.localidade,
                cepUf: d.uf
            };
            for (const key in map) {
                const sel = cepEl.dataset[key];
                if (sel && map[key]) setNativeValue(resolveTarget(cepEl, sel), map[key]);
            }
        }
        const focusSel = cepEl.dataset.cepFocus;
        if (focusSel) {
            const target = resolveTarget(cepEl, focusSel);
            if (target) try { target.focus(); } catch (_) { /* noop */ }
        }
    }

    async function lookup(cepEl) {
        const cep = onlyDigits(cepEl.value);
        if (cep.length !== 8) return;
        if (cepEl._cepLast === cep || cepEl._cepBusy) return;
        cepEl._cepBusy = true;

        const ctrl = new AbortController();
        const timer = setTimeout(() => ctrl.abort(), 4000);
        try {
            const resp = await fetch('https://viacep.com.br/ws/' + cep + '/json/', {
                signal: ctrl.signal,
                headers: { Accept: 'application/json' }
            });
            if (!resp.ok) return;
            const d = await resp.json();
            if (!d || d.erro) return;
            cepEl._cepLast = cep;
            fill(cepEl, d);
        } catch (_) {
            /* timeout/offline/JSON inválido — best-effort, segue manual */
        } finally {
            clearTimeout(timer);
            cepEl._cepBusy = false;
        }
    }

    function attach(cepEl) {
        if (!cepEl || cepEl.dataset.cepBound === '1') return;
        cepEl.dataset.cepBound = '1';
        cepEl.addEventListener('input', () => lookup(cepEl));
        cepEl.addEventListener('blur', () => lookup(cepEl));
    }

    function scan(root) {
        (root || document).querySelectorAll('[data-cep-autofill]').forEach(attach);
    }

    document.addEventListener('DOMContentLoaded', () => {
        scan();
        // Modais Alpine / partials carregados sob demanda.
        try {
            const mo = new MutationObserver((mutations) => {
                for (const m of mutations) {
                    m.addedNodes.forEach((node) => {
                        if (node.nodeType !== 1) return;
                        if (node.matches && node.matches('[data-cep-autofill]')) attach(node);
                        if (node.querySelectorAll) scan(node);
                    });
                }
            });
            mo.observe(document.body, { childList: true, subtree: true });
        } catch (_) { /* fallback: scan inicial */ }
    });
})();
