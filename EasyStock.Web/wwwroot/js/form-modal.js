/**
 * Reusable factory for create/edit modals (Fornecedor, Cliente, etc.).
 * Handles open/close state, body scroll lock, focus trap, autofocus,
 * AJAX submit with friendly error mapping, and Enter-to-submit.
 *
 * Usage in Alpine:
 *   x-data="formModal({ id: 'novo-fornecedor', endpoint: '/fornecedores/json',
 *                       redirect: '/fornecedores/{id}', initialForm: { ... },
 *                       requireField: 'nome', requireMessage: 'Informe o nome.' })"
 */
(function () {
    function friendlyError(status, data) {
        var err = data && data.error;
        var code = err && err.code;
        var apiMsg = (err && err.message) || (data && data.errorMessage);

        var byCode = {
            VALIDATION_ERROR:        apiMsg || 'Revise os campos destacados e tente de novo.',
            CLIENTE_DUPLICADO:       'Já existe um cliente com esse documento. Busque pelo CPF/CNPJ na lista.',
            FORNECEDOR_DUPLICADO:    'Já existe um fornecedor com esse documento.',
            CPF_DUPLICADO:           'Já existe um cadastro com esse CPF.',
            CNPJ_DUPLICADO:          'Já existe um cadastro com esse CNPJ.',
            EMAIL_DUPLICADO:         'Esse e-mail já está em uso.',
            DOCUMENTO_INVALIDO:      'CPF ou CNPJ inválido. Confira os dígitos.',
            EMAIL_INVALIDO:          'E-mail inválido. Confira o endereço.',
            SKU_DUPLICADO:           'Já existe um produto com esse SKU.',
            CATEGORIA_DUPLICADA:     'Já existe uma categoria com esse nome.',
            LOJA_DUPLICADA:          'Já existe uma loja com esse nome.',
            EMPRESA_INVALIDA:        'Loja não identificada. Selecione uma loja no topo e tente novamente.',
            LOJA_NAO_SELECIONADA:    'Selecione uma loja antes de continuar.',
            AUTH_TOKEN_EXPIRED:      'Sua sessão expirou. Vamos te levar ao login.',
            PERMISSAO_INSUFICIENTE:  'Você não tem permissão para essa ação. Fale com um admin do seu time.',
            NOT_FOUND:               'Não encontramos esse registro. Talvez tenha sido removido.',
            CONCURRENCY_CONFLICT:    'Outra pessoa acabou de atualizar esse registro. Recarregue para ver as mudanças.',
            BUSINESS_RULE_VIOLATION: apiMsg || 'Essa operação não é permitida agora.',
            TIMEOUT:                 'O servidor demorou para responder. Tente de novo.',
            NETWORK_ERROR:           'Sem conexão com o servidor. Verifique sua internet.'
        };

        if (code && byCode[code]) return byCode[code];
        if (code && /^LIMITE_PLANO/.test(code)) {
            return 'Você atingiu o limite do seu plano. Veja opções em Configurações › Plano.';
        }

        if (status === 401) return byCode.AUTH_TOKEN_EXPIRED;
        if (status === 403) return byCode.PERMISSAO_INSUFICIENTE;
        if (status === 404) return byCode.NOT_FOUND;
        if (status === 409) return apiMsg || 'Esse registro conflita com um existente.';
        if (status === 422) return byCode.VALIDATION_ERROR;
        if (status === 429) return 'Muitas tentativas em pouco tempo. Aguarde alguns segundos.';
        if (status >= 500) return apiMsg ? ('Tivemos um problema. ' + apiMsg) : 'Tivemos um problema do nosso lado. Tente de novo em instantes.';
        return apiMsg || ('Não foi possível concluir (erro ' + status + ').');
    }

    function _ensureFieldErrorEl(field) {
        if (!field.parentElement) return null;
        var err = field.parentElement.querySelector(':scope > .field-error');
        if (err) return err;
        err = document.createElement('p');
        err.className = 'field-error text-xs text-red-600 mt-1';
        err.setAttribute('role', 'alert');
        field.parentElement.appendChild(err);
        return err;
    }

    function _setFieldError(field, message) {
        field.classList.add('input-invalid', 'border-red-400');
        field.setAttribute('aria-invalid', 'true');
        var err = _ensureFieldErrorEl(field);
        if (err) {
            err.textContent = message || 'Valor inválido.';
            err.style.display = '';
        }
    }

    function _clearFieldError(field) {
        field.classList.remove('input-invalid', 'border-red-400');
        field.removeAttribute('aria-invalid');
        if (!field.parentElement) return;
        var err = field.parentElement.querySelector(':scope > .field-error');
        if (err) {
            err.textContent = '';
            err.style.display = 'none';
        }
    }

    function trapFocus(panel, event) {
        if (event.key !== 'Tab' || !panel) return;
        var focusable = panel.querySelectorAll(
            'a[href], button:not([disabled]), input:not([disabled]):not([type=hidden]),' +
            'select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'
        );
        if (!focusable.length) return;
        var first = focusable[0];
        var last = focusable[focusable.length - 1];
        if (event.shiftKey && document.activeElement === first) {
            event.preventDefault();
            last.focus();
        } else if (!event.shiftKey && document.activeElement === last) {
            event.preventDefault();
            first.focus();
        }
    }

    window.formModal = function (config) {
        return {
            open: false,
            loading: false,
            erroMsg: '',
            form: Object.assign({}, config.initialForm || {}),
            advancedOpen: false,
            _previousFocus: null,

            init() {
                window.addEventListener('open-modal', (e) => {
                    if (e.detail === config.id) {
                        this._previousFocus = document.activeElement;
                        this.open = true;
                        this.$nextTick(() => this._afterOpen());
                    }
                });
                this.$watch('open', (isOpen) => {
                    document.body.style.overflow = isOpen ? 'hidden' : '';
                    if (!isOpen) {
                        this.erroMsg = '';
                        if (this._previousFocus && typeof this._previousFocus.focus === 'function') {
                            this._previousFocus.focus();
                        }
                    }
                });
            },

            close() {
                this.open = false;
            },

            handleKeydown(event) {
                if (event.key === 'Escape') {
                    this.close();
                    return;
                }
                trapFocus(this.$refs.panel, event);
            },

            _afterOpen() {
                var panel = this.$refs.panel;
                if (!panel) return;
                var first = panel.querySelector(
                    'input:not([type=hidden]):not([disabled]), select:not([disabled]), textarea:not([disabled])'
                );
                if (first) first.focus();
                this._attachInlineValidation(panel);
            },

            _attachInlineValidation(panel) {
                // Re-roda em cada abertura — se o conteudo do modal mudou, captura novos campos.
                // Usa EasyValidate.checkField se disponivel (CPF/CNPJ/phone/email/required).
                if (!window.EasyValidate || !window.EasyValidate.checkField) return;
                var fields = panel.querySelectorAll('input, textarea, select');
                fields.forEach(function (field) {
                    if (field.dataset._fmInlineBound === '1') return;
                    field.dataset._fmInlineBound = '1';

                    field.addEventListener('blur', function () {
                        // So valida se o usuario interagiu com o campo, evita "obrigatorio"
                        // disparar antes de o usuario sequer ter digitado.
                        if (!field.dataset.touched) return;
                        var result = window.EasyValidate.checkField(field);
                        if (result.valid) _clearFieldError(field);
                        else _setFieldError(field, result.message);
                    });

                    field.addEventListener('input', function () {
                        field.dataset.touched = '1';
                        // Revalida em real-time so quando ja esta mostrando erro,
                        // pra nao incomodar quem ainda esta digitando.
                        if (field.classList.contains('input-invalid')) {
                            var result = window.EasyValidate.checkField(field);
                            if (result.valid) _clearFieldError(field);
                        }
                    });
                });
            },

            isInvalid() {
                if (!config.requireField) return false;
                var v = this.form[config.requireField];
                return !v || !String(v).trim();
            },

            async submit() {
                if (this.loading) return;
                if (this.isInvalid()) {
                    this.erroMsg = config.requireMessage || 'Preencha os campos obrigatórios.';
                    return;
                }
                this.loading = true;
                this.erroMsg = '';
                try {
                    var token = document.querySelector('[name=__RequestVerificationToken]')?.value;
                    var res = await esFetch(config.endpoint, {
                        method: 'POST',
                        headers: {
                            'Content-Type': 'application/json',
                            'RequestVerificationToken': token
                        },
                        body: JSON.stringify(this.form)
                    });
                    var raw = await res.text();
                    var data = null;
                    try { data = raw ? JSON.parse(raw) : null; } catch { /* não-JSON */ }

                    if (res.ok && data && data.success) {
                        if (typeof config.onSuccess === 'function') {
                            config.onSuccess(data);
                            return;
                        }
                        if (config.redirect) {
                            window.location.href = config.redirect.replace('{id}', data.id);
                            return;
                        }
                        this.close();
                        return;
                    }
                    this.erroMsg = friendlyError(res.status, data);
                } catch {
                    this.erroMsg = 'Sem conexão com o servidor. Verifique sua internet e tente de novo.';
                } finally {
                    this.loading = false;
                }
            }
        };
    };
})();
