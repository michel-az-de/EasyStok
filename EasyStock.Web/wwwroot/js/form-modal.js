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
    function friendlyError(status) {
        if (status === 400 || status === 403) {
            return 'Sua sessão expirou. Recarregue a página e tente novamente.';
        }
        if (status === 401) {
            return 'Você precisa entrar de novo. Te levamos para o login em instantes.';
        }
        if (status === 409) {
            return 'Já existe um registro com esses dados. Verifique e tente novamente.';
        }
        if (status >= 500) {
            return 'Tivemos um problema no servidor. Tente novamente em alguns instantes.';
        }
        return 'Não foi possível concluir o cadastro (erro ' + status + '). Tente novamente em instantes.';
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
                    var res = await fetch(config.endpoint, {
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
                    this.erroMsg = (data && data.errorMessage) || friendlyError(res.status);
                } catch {
                    this.erroMsg = 'Sem conexão com o servidor. Verifique sua internet e tente de novo.';
                } finally {
                    this.loading = false;
                }
            }
        };
    };
})();
