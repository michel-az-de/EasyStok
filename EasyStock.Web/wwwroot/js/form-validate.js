/* form-validate.js — validação client-side declarativa antes do POST.
 *
 * Aplica-se automaticamente em <form data-validate>. Bloqueia submit quando
 * algum campo inválido for encontrado, foca o primeiro erro, mostra mensagens
 * inline (criadas dinamicamente) e dispara um toast de orientação.
 *
 * Regras suportadas via atributos do campo:
 *   - required, type="email", type="tel", type="number"
 *   - minlength, maxlength, min, max, pattern, step (HTML5 nativo)
 *   - data-validate-cpf      → 11 dígitos com cálculo de DV
 *   - data-validate-cnpj     → 14 dígitos com cálculo de DV
 *   - data-validate-doc      → CPF ou CNPJ (auto pelo length)
 *   - data-validate-phone    → 10 ou 11 dígitos
 *   - data-validate-money    → > 0 (após parsing BR)
 *   - data-validate-min-num="X"  → número >= X (aceita BR "1.234,56")
 *   - data-validate-max-num="X"  → número <= X
 *   - data-error-message="..."   → sobrescreve a mensagem padrão
 *
 * O server permanece como source of truth: client-side só evita ida-volta para
 * 400. Em modais Alpine.js, a validação é compatível com submits programáticos
 * desde que o form tenha data-validate.
 */
(function () {
    'use strict';

    const onlyDigits = (s) => (s || '').toString().replace(/\D+/g, '');

    function parseDecimalBR(val) {
        if (val == null) return NaN;
        let s = String(val).trim();
        if (!s) return NaN;
        const lastComma = s.lastIndexOf(',');
        const lastDot = s.lastIndexOf('.');
        if (lastComma > -1 && lastComma > lastDot) {
            s = s.replace(/\./g, '').replace(',', '.');
        } else if (lastDot > -1 && lastComma > -1) {
            s = s.replace(/,/g, '');
        } else {
            s = s.replace(',', '.');
        }
        const n = parseFloat(s);
        return isFinite(n) ? n : NaN;
    }

    function isValidCpf(cpf) {
        const d = onlyDigits(cpf);
        if (d.length !== 11) return false;
        if (/^(\d)\1+$/.test(d)) return false;
        const calc = (slice) => {
            let sum = 0;
            for (let i = 0; i < slice.length; i++) sum += parseInt(slice[i], 10) * (slice.length + 1 - i);
            const r = (sum * 10) % 11;
            return r === 10 ? 0 : r;
        };
        return calc(d.slice(0, 9)) === parseInt(d[9], 10) && calc(d.slice(0, 10)) === parseInt(d[10], 10);
    }

    function isValidCnpj(cnpj) {
        const d = onlyDigits(cnpj);
        if (d.length !== 14) return false;
        if (/^(\d)\1+$/.test(d)) return false;
        const calc = (len) => {
            const w = len === 12 ? [5,4,3,2,9,8,7,6,5,4,3,2] : [6,5,4,3,2,9,8,7,6,5,4,3,2];
            let sum = 0;
            for (let i = 0; i < len; i++) sum += parseInt(d[i], 10) * w[i];
            const r = sum % 11;
            return r < 2 ? 0 : 11 - r;
        };
        return calc(12) === parseInt(d[12], 10) && calc(13) === parseInt(d[13], 10);
    }

    // Retorna {valid, message} para um campo. message vazio quando válido.
    function checkField(el) {
        if (el.disabled || el.type === 'submit' || el.type === 'button') {
            return { valid: true };
        }

        // BUG-07: required nativo OU custom (data-required). Componentes como o
        // _CreatableCombobox guardam o valor selecionado num input hidden e marcam
        // obrigatoriedade via data-required — o required HTML nativo não se aplica a hidden.
        const isRequired = el.required || el.dataset.required === 'true';

        // hidden é pulado, exceto quando obrigatório (ex.: o hidden do combobox de categoria).
        if (el.type === 'hidden' && !isRequired) {
            return { valid: true };
        }

        const val = (el.value ?? '').toString();
        const trimmed = val.trim();
        const customMsg = el.dataset.errorMessage;

        // Required
        if (isRequired && !trimmed) {
            return { valid: false, message: customMsg || 'Este campo é obrigatório.' };
        }

        // Se estiver vazio e não-required, considera válido — regras adicionais só
        // se aplicam quando há valor.
        if (!trimmed) return { valid: true };

        // HTML5 native (maxlength, min, max, pattern, type=email/url/number)
        if (typeof el.checkValidity === 'function' && !el.checkValidity()) {
            const msg = el.validationMessage || customMsg || 'Valor inválido.';
            return { valid: false, message: customMsg || msg };
        }

        // CPF / CNPJ / Documento
        if (el.dataset.validateCpf !== undefined && !isValidCpf(trimmed)) {
            return { valid: false, message: customMsg || 'CPF inválido.' };
        }
        if (el.dataset.validateCnpj !== undefined && !isValidCnpj(trimmed)) {
            return { valid: false, message: customMsg || 'CNPJ inválido.' };
        }
        if (el.dataset.validateDoc !== undefined) {
            const d = onlyDigits(trimmed);
            if (d.length === 11 && !isValidCpf(d)) return { valid: false, message: customMsg || 'CPF inválido.' };
            if (d.length === 14 && !isValidCnpj(d)) return { valid: false, message: customMsg || 'CNPJ inválido.' };
            if (d.length !== 11 && d.length !== 14) return { valid: false, message: customMsg || 'Informe um CPF (11 dígitos) ou CNPJ (14 dígitos).' };
        }

        // Telefone BR (10 ou 11 dígitos)
        if (el.dataset.validatePhone !== undefined) {
            const d = onlyDigits(trimmed);
            if (d.length < 10 || d.length > 11) {
                return { valid: false, message: customMsg || 'Telefone deve ter 10 ou 11 dígitos.' };
            }
        }

        // Money (> 0)
        if (el.dataset.validateMoney !== undefined) {
            const n = parseDecimalBR(trimmed);
            if (!isFinite(n) || n <= 0) {
                return { valid: false, message: customMsg || 'Informe um valor maior que zero.' };
            }
        }

        // Numérico mínimo / máximo (com parsing BR)
        if (el.dataset.validateMinNum !== undefined) {
            const min = parseFloat(el.dataset.validateMinNum);
            const n = parseDecimalBR(trimmed);
            if (!isFinite(n) || n < min) {
                return { valid: false, message: customMsg || `Valor mínimo: ${min}.` };
            }
        }
        if (el.dataset.validateMaxNum !== undefined) {
            const max = parseFloat(el.dataset.validateMaxNum);
            const n = parseDecimalBR(trimmed);
            if (!isFinite(n) || n > max) {
                return { valid: false, message: customMsg || `Valor máximo: ${max}.` };
            }
        }

        return { valid: true };
    }

    function ensureErrorEl(field) {
        let err = field.parentElement && field.parentElement.querySelector(':scope > .field-error');
        if (err) return err;
        err = document.createElement('p');
        err.className = 'field-error text-xs text-red-600 mt-1';
        err.setAttribute('role', 'alert');
        if (field.parentElement) field.parentElement.appendChild(err);
        return err;
    }

    function setFieldError(field, message) {
        field.classList.add('input-invalid', 'border-red-400');
        field.setAttribute('aria-invalid', 'true');
        const err = ensureErrorEl(field);
        err.textContent = message;
        err.style.display = '';
    }

    function clearFieldError(field) {
        field.classList.remove('input-invalid', 'border-red-400');
        field.removeAttribute('aria-invalid');
        const err = field.parentElement && field.parentElement.querySelector(':scope > .field-error');
        if (err) {
            err.textContent = '';
            err.style.display = 'none';
        }
    }

    function validateForm(form) {
        const fields = Array.from(form.querySelectorAll('input, textarea, select'));
        let firstInvalid = null;
        let invalidCount = 0;

        for (const field of fields) {
            const { valid, message } = checkField(field);
            if (valid) {
                clearFieldError(field);
            } else {
                setFieldError(field, message);
                invalidCount += 1;
                if (!firstInvalid) firstInvalid = field;
            }
        }

        if (invalidCount > 0) {
            if (firstInvalid) {
                try { firstInvalid.focus({ preventScroll: false }); } catch (_) { firstInvalid.focus(); }
                if (firstInvalid.scrollIntoView) firstInvalid.scrollIntoView({ behavior: 'smooth', block: 'center' });
            }
            if (window.showToast) {
                const msg = invalidCount === 1
                    ? 'Há 1 campo inválido — corrija antes de continuar.'
                    : `Há ${invalidCount} campos inválidos — corrija antes de continuar.`;
                window.showToast(msg, 'error');
            }
        }

        return invalidCount === 0;
    }

    function attach(form) {
        if (form.dataset._validateAttached === '1') return;
        form.dataset._validateAttached = '1';

        // novalidate sistemico: impede a constraint validation HTML5 nativa de cancelar o
        // submit antes deste handler (senao a mensagem custom + toast nao aparecem em campos
        // com min/required/pattern nativos — ex.: o abrir-caixa do #546). Generaliza o padrao
        // de Produtos/Pedidos a todo form[data-validate], sem depender de novalidate por view.
        form.setAttribute('novalidate', '');

        // Limpa erro do campo enquanto o usuário digita — feedback rápido sem revalidar tudo.
        form.addEventListener('input', (e) => {
            const t = e.target;
            if (t && t.classList && t.classList.contains('input-invalid')) {
                const { valid } = checkField(t);
                if (valid) clearFieldError(t);
            }
        }, true);

        form.addEventListener('submit', (e) => {
            if (!validateForm(form)) {
                e.preventDefault();
                e.stopImmediatePropagation();
                // Se o form-submit.js já travou os botões, libera de novo.
                form.querySelectorAll('button[type=submit], input[type=submit]').forEach((b) => {
                    b.disabled = false;
                    if (b.dataset && b.dataset.loading) b.dataset.loading = 'false';
                });
            }
        }, true);
    }

    document.addEventListener('DOMContentLoaded', () => {
        document.querySelectorAll('form[data-validate]').forEach(attach);
    });

    // Públiça API pra forms criados dinamicamente (modais, tabs, etc).
    window.EasyValidate = { attach, validateForm, checkField };
})();
