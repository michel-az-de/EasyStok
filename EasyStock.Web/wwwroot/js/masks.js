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

    /* --- Par display/canonico para campos de moeda (issue 986) ---
     *
     * O input VISIVEL e mascarado ("R$ 1.234,56") e NAO tem `name` — nunca e
     * submetido. O hidden apontado por `data-money-target` carrega o decimal
     * invariante (\d+\.\d{2}) e e ele que tem o `name`, que o FormData le e que o
     * x-model do Alpine observa.
     *
     * O par existe porque `formatters.money` produz "R$ 1.234,56", e o
     * InvariantDecimalModelBinder parseia SEM AllowCurrencySymbol (NormalizarDecimalPtBr
     * so remove '.' e ','). Mascarar o campo que faz POST invalidaria o ModelState —
     * e por isso que `x-mask:money` tinha zero usos: nunca foi integrado a um campo
     * que submete.
     */
    function moneyTargetOf(el) {
        const id = el.dataset.moneyTarget;
        return id ? document.getElementById(id) : null;
    }

    function syncMoneyTarget(el) {
        const hidden = moneyTargetOf(el);
        if (!hidden) return;
        const digits = onlyDigits(el.value);
        const canonical = digits ? (parseInt(digits, 10) / 100).toFixed(2) : '';
        if (hidden.value === canonical) return;
        hidden.value = canonical;
        // Mesmo motivo do re-dispatch logo abaixo: sem isto o x-model do Alpine nao
        // enxerga a escrita programatica e o estado reativo diverge do que foi digitado.
        hidden.dispatchEvent(new Event('input', { bubbles: true }));
        hidden.dispatchEvent(new Event('change', { bubbles: true }));
    }

    /* Hidratacao: o desenho cobre digitar, colar e atribuir programaticamente, mas nao
     * a ENTRADA EM TELA com valor ja existente — edicao de registro, re-render com
     * ModelState invalido, e reset de modal. Sem isto o usuario veria o campo visivel
     * vazio enquanto o hidden esta preenchido. */
    function hydrateMoneyDisplay(el) {
        const hidden = moneyTargetOf(el);
        if (!hidden || !hidden.value) return;
        const n = parseFloat(hidden.value);
        if (!Number.isFinite(n) || n <= 0) return;
        el.value = formatters.money(String(Math.round(n * 100)));
    }

    function applyMask(el, type) {
        if (!el || el.dataset.maskApplied === '1') return;
        const fmt = formatters[type];
        if (!fmt) return;
        if (type === 'money') hydrateMoneyDisplay(el);
        const handler = () => {
            // Guarda de reentrada: o dispatch de 'input' abaixo re-chama este handler
            // (esta funcao E o listener de input); sem a guarda, recursao infinita.
            if (el._masking) return;
            const start = el.selectionStart;
            const before = el.value;
            const formatted = fmt(before);
            if (formatted === before) {
                el.dataset.raw = onlyDigits(formatted);
                syncMoneyTarget(el);
                return;
            }
            el._masking = true;
            el.value = formatted;
            // raw value for submit / programmatic access
            el.dataset.raw = onlyDigits(formatted);
            syncMoneyTarget(el);
            // try to keep cursor near the end while typing
            try {
                if (type === 'money') {
                    // Caixa registradora: os digitos entram pela direita e o separador anda
                    // sozinho, entao preservar a posicao (correto p/ CPF/telefone) faria o
                    // cursor cair no meio do numero a cada tecla.
                    el.setSelectionRange(formatted.length, formatted.length);
                } else {
                    const delta = formatted.length - before.length;
                    el.setSelectionRange(start + Math.max(delta, 0), start + Math.max(delta, 0));
                }
            } catch (_) { /* selection not supported on some inputs */ }
            // Issue 812/#497: re-dispatcha 'input' para o x-model do Alpine capturar o
            // valor JA mascarado (antes o x-model guardava o valor pre-mascara e divergia).
            el.dispatchEvent(new Event('input', { bubbles: true }));
            el._masking = false;
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

    /* Reflete um valor numerico no display mascarado. Para uso em x-effect, quando o
     * estado do Alpine muda por FORA da digitacao (resposta assincrona que traz o
     * preco-base, reset de modal, edicao). Sem isto o hidden mudaria e o visivel
     * ficaria congelado.
     *
     * A guarda de activeElement e o "nunca sobrescrever a entrada do usuario" que o QA
     * pediu — mesma regra que syncMoneyInput (Produtos/Form) resolveu primeiro. */
    function setMoney(el, numericValue) {
        if (!el || el === document.activeElement) return;
        const n = Number(numericValue);
        const formatted = Number.isFinite(n) && n > 0
            ? formatters.money(String(Math.round(n * 100)))
            : '';
        if (el.value === formatted) return;
        el.value = formatted;
        el.dataset.raw = onlyDigits(formatted);
    }

    // Public API
    window.EasyMasks = { apply: applyMask, format: formatters, onlyDigits, setMoney };
})();
