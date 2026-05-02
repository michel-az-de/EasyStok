/* masks.js — máscaras de input via Alpine directive `x-mask:<tipo>`.
 *
 * Implementação leve, sem dependência externa. Suporta:
 *   x-mask:cpf    → 000.000.000-00
 *   x-mask:cnpj   → 00.000.000/0000-00
 *   x-mask:doc    → CPF ou CNPJ (auto pelo length)
 *   x-mask:phone  → (00) 00000-0000  / (00) 0000-0000
 *   x-mask:cep    → 00000-000
 *   x-mask:money  → R$ 1.234,56  (BR)
 *   x-mask:date   → 00/00/0000
 *
 * Aplica-se em qualquer <input>; preserva o valor "limpo" em data-raw para submit.
 */
(function () {
    'use strict';

    const onlyDigits = (s) => (s || '').toString().replace(/\D+/g, '');

    const formatters = {
        cpf(v) {
            v = onlyDigits(v).slice(0, 11);
            return v
                .replace(/(\d{3})(\d)/, '$1.$2')
                .replace(/(\d{3})(\d)/, '$1.$2')
                .replace(/(\d{3})(\d{1,2})$/, '$1-$2');
        },
        cnpj(v) {
            v = onlyDigits(v).slice(0, 14);
            return v
                .replace(/^(\d{2})(\d)/, '$1.$2')
                .replace(/^(\d{2})\.(\d{3})(\d)/, '$1.$2.$3')
                .replace(/\.(\d{3})(\d)/, '.$1/$2')
                .replace(/(\d{4})(\d{1,2})$/, '$1-$2');
        },
        doc(v) {
            const d = onlyDigits(v);
            return d.length <= 11 ? formatters.cpf(d) : formatters.cnpj(d);
        },
        phone(v) {
            v = onlyDigits(v).slice(0, 11);
            if (v.length <= 10) {
                return v
                    .replace(/^(\d{2})(\d)/, '($1) $2')
                    .replace(/(\d{4})(\d{1,4})$/, '$1-$2');
            }
            return v
                .replace(/^(\d{2})(\d)/, '($1) $2')
                .replace(/(\d{5})(\d{1,4})$/, '$1-$2');
        },
        cep(v) {
            v = onlyDigits(v).slice(0, 8);
            return v.replace(/(\d{5})(\d{1,3})$/, '$1-$2');
        },
        money(v) {
            const n = onlyDigits(v);
            if (!n) return '';
            const cents = (parseInt(n, 10) / 100).toFixed(2);
            const [intPart, dec] = cents.split('.');
            return 'R$ ' + intPart.replace(/\B(?=(\d{3})+(?!\d))/g, '.') + ',' + dec;
        },
        date(v) {
            v = onlyDigits(v).slice(0, 8);
            return v
                .replace(/^(\d{2})(\d)/, '$1/$2')
                .replace(/^(\d{2}\/\d{2})(\d)/, '$1/$2');
        }
    };

    function applyMask(el, type) {
        const fmt = formatters[type];
        if (!fmt) return;
        const handler = () => {
            const start = el.selectionStart;
            const before = el.value;
            const formatted = fmt(before);
            el.value = formatted;
            // raw value for submit / programmatic access
            el.dataset.raw = onlyDigits(formatted);
            // try to keep cursor near the end while typing
            try {
                const delta = formatted.length - before.length;
                el.setSelectionRange(start + Math.max(delta, 0), start + Math.max(delta, 0));
            } catch (_) { /* selection not supported on some inputs */ }
        };
        el.addEventListener('input', handler);
        // initial format
        if (el.value) handler();
    }

    // Plain attribute fallback: <input data-mask="cpf">
    document.addEventListener('DOMContentLoaded', () => {
        document.querySelectorAll('[data-mask]').forEach((el) => {
            applyMask(el, el.dataset.mask);
        });
    });

    // Alpine directive: x-mask:cpf, x-mask:cnpj, ...
    document.addEventListener('alpine:init', () => {
        if (!window.Alpine) return;
        window.Alpine.directive('mask', (el, { value }) => {
            if (value) applyMask(el, value);
        });
    });

    // Public API
    window.EasyMasks = { apply: applyMask, format: formatters, onlyDigits };
})();
