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
        if (!el || el.dataset.maskApplied === '1') return;
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
        el.dataset.maskApplied = '1';
        // initial format
        if (el.value) handler();
    }

    function scanAndApply(root) {
        const target = root || document;
        target.querySelectorAll('[data-mask]').forEach((el) => {
            applyMask(el, el.dataset.mask);
        });
    }

    // Plain attribute fallback: <input data-mask="cpf">
    document.addEventListener('DOMContentLoaded', () => {
        scanAndApply();
        // Observa mudanças no DOM para captar inputs renderizados dinamicamente
        // (modais Alpine, partials carregados sob demanda, x-show com x-cloak, etc.).
        try {
            const mo = new MutationObserver((mutations) => {
                for (const m of mutations) {
                    m.addedNodes.forEach((node) => {
                        if (node.nodeType !== 1) return;
                        if (node.matches && node.matches('[data-mask]')) {
                            applyMask(node, node.dataset.mask);
                        }
                        if (node.querySelectorAll) {
                            scanAndApply(node);
                        }
                    });
                }
            });
            mo.observe(document.body, { childList: true, subtree: true });
        } catch (_) { /* IE/old browsers — fallback to initial scan */ }
    });

    // Alpine directive: x-mask:cpf, x-mask:cnpj, ...
    document.addEventListener('alpine:init', () => {
        if (!window.Alpine) return;
        window.Alpine.directive('mask', (el, { value }) => {
            if (value) applyMask(el, value);
        });
    });

    // ── Normalização no submit (#F4): envia o valor CRU (dataset.raw) ao servidor. ──
    // Captura + DOM nativo (respeita o gotcha do FormTagHelper: não usa @submit.prevent).
    // Seguro: o domínio (Cnpj.From) já normaliza tirando formatação, e campos sem data-raw
    // mantêm o value atual — então não há regressão para forms que hoje mandam mascarado/cru.
    document.addEventListener('submit', function (e) {
        try {
            var form = e.target;
            if (!form || !form.querySelectorAll) return;
            form.querySelectorAll('[data-mask-applied="1"]').forEach(function (el) {
                if (el.dataset && el.dataset.raw != null && el.dataset.raw !== '') {
                    el.value = el.dataset.raw;
                }
            });
        } catch (_) { /* máscara nunca deve bloquear o submit */ }
    }, true);

    // ── Validação de dígito verificador CPF/CNPJ (feedback inline, #F4). ──
    // Disponível via window.EasyMasks; o servidor (Cnpj.From) valida só o comprimento.
    function validarCpf(v) {
        var c = onlyDigits(v);
        if (c.length !== 11 || /^(\d)\1{10}$/.test(c)) return false;
        function dig(len) {
            var sum = 0;
            for (var i = 0; i < len; i++) sum += parseInt(c[i], 10) * (len + 1 - i);
            var r = (sum * 10) % 11;
            return r === 10 ? 0 : r;
        }
        return dig(9) === parseInt(c[9], 10) && dig(10) === parseInt(c[10], 10);
    }
    function validarCnpj(v) {
        var c = onlyDigits(v);
        if (c.length !== 14 || /^(\d)\1{13}$/.test(c)) return false;
        function dig(len) {
            var w = len === 12
                ? [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]
                : [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
            var sum = 0;
            for (var i = 0; i < len; i++) sum += parseInt(c[i], 10) * w[i];
            var r = sum % 11;
            return r < 2 ? 0 : 11 - r;
        }
        return dig(12) === parseInt(c[12], 10) && dig(13) === parseInt(c[13], 10);
    }
    // Campo único aceita CPF ou CNPJ (como o Cnpj.From do domínio): decide pelo tamanho.
    function validarDoc(v) {
        var c = onlyDigits(v);
        return c.length <= 11 ? validarCpf(c) : validarCnpj(c);
    }

    // Public API
    window.EasyMasks = {
        apply: applyMask,
        format: formatters,
        onlyDigits: onlyDigits,
        validarCpf: validarCpf,
        validarCnpj: validarCnpj,
        validarDoc: validarDoc
    };
})();
