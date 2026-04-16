// Alpine.js component for the announcement generation form
function anuncioForm() {
    return {
        busca: '',
        resultados: [],
        showResults: false,
        produtoId: '',
        canal: 'ML',
        tom: 'profissional',
        foco: 'beneficios',
        contexto: '',
        gerando: false,
        _debounce: null,

        init() {
            window.addEventListener('anuncio-finalizado', () => {
                this.gerando = false;
            });
        },

        buscarProduto() {
            clearTimeout(this._debounce);
            this._debounce = setTimeout(async () => {
                if (this.busca.length < 2) { this.resultados = []; return; }
                try {
                    const res = await fetch(`/produtos/buscar?q=${encodeURIComponent(this.busca)}`);
                    this.resultados = res.ok ? await res.json() : [];
                } catch (e) {
                    this.resultados = [];
                }
            }, 300);
        },

        selecionarProduto(p) {
            this.produtoId = p.id;
            this.busca = p.nome;
            this.showResults = false;
            this.resultados = [];
            window.dispatchEvent(new CustomEvent('produto-selecionado', { detail: { produtoId: p.id } }));
        },

        gerarAnuncio() {
            if (!this.produtoId) return;
            this.gerando = true;

            window.dispatchEvent(new CustomEvent('anuncio-gerar', {
                detail: {
                    produtoId: this.produtoId,
                    canal: this.canal,
                    tom: this.tom,
                    foco: this.foco,
                    contexto: this.contexto
                }
            }));
        }
    };
}

// Alpine.js component for the result panel
function anuncioResultado() {
    return {
        resultado: '',
        gerando: false,
        copiado: false,
        salvando: false,
        _produtoId: '',
        _instrucoes: '',
        _source: null,

        init() {
            window.addEventListener('anuncio-gerar', (e) => {
                this._produtoId = e.detail.produtoId;
                this._instrucoes = [e.detail.canal, e.detail.tom, e.detail.foco, e.detail.contexto].filter(Boolean).join('. ');
                this.iniciarStream(e.detail);
            });
        },

        iniciarStream({ produtoId, canal, tom, foco, contexto }) {
            if (this._source) {
                this._source.close();
                this._source = null;
            }

            this.resultado = '';
            this.gerando = true;

            const params = new URLSearchParams({
                produtoId,
                canal,
                tom,
                foco,
                ...(contexto ? { contexto } : {})
            });

            fetch(`/anuncios/gerar?${params}`)
                .then(res => {
                    if (!res.ok) {
                        this.resultado = 'Erro ao gerar anúncio. Tente novamente.';
                        this.gerando = false;
                        window.dispatchEvent(new CustomEvent('anuncio-concluido'));
                        return;
                    }

                    const reader = res.body.getReader();
                    const decoder = new TextDecoder();

                    const read = () => {
                        reader.read().then(({ done, value }) => {
                            if (done) {
                                this.gerando = false;
                                window.dispatchEvent(new CustomEvent('anuncio-concluido'));
                                return;
                            }

                            const text = decoder.decode(value, { stream: true });
                            const lines = text.split('\n');
                            for (const line of lines) {
                                if (line.startsWith('data: ')) {
                                    const data = line.slice(6);
                                    if (data === '[DONE]') {
                                        this.gerando = false;
                                        window.dispatchEvent(new CustomEvent('anuncio-concluido'));
                                        return;
                                    }
                                    this.resultado += data;
                                } else if (line.trim() && !line.startsWith('event:') && !line.startsWith(':')) {
                                    this.resultado += line;
                                }
                            }
                            read();
                        }).catch(() => {
                            this.resultado = 'Erro ao ler resposta do servidor. Tente novamente.';
                            this.gerando = false;
                            window.dispatchEvent(new CustomEvent('anuncio-concluido'));
                        });
                    };

                    read();
                })
                .catch(() => {
                    this.resultado = 'Erro de conexão. Verifique sua internet e tente novamente.';
                    this.gerando = false;
                    window.dispatchEvent(new CustomEvent('anuncio-concluido'));
                });

            window.addEventListener('anuncio-concluido', () => {
                window.dispatchEvent(new CustomEvent('anuncio-finalizado'));
            }, { once: true });
        },

        copiar() {
            if (!this.resultado) return;
            navigator.clipboard.writeText(this.resultado).then(() => {
                this.copiado = true;
                setTimeout(() => { this.copiado = false; }, 2000);
            });
        },

        async salvarRascunho() {
            if (!this.resultado || !this._produtoId) return;
            this.salvando = true;
            const token = document.querySelector('[name=__RequestVerificationToken]')?.value ?? '';
            const titulo = this.resultado.substring(0, 80).replace(/\n/g, ' ').trim();
            try {
                const res = await fetch('/anuncios/salvar', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/x-www-form-urlencoded',
                        'RequestVerificationToken': token
                    },
                    body: new URLSearchParams({
                        produtoId: this._produtoId,
                        titulo,
                        conteudo: this.resultado,
                        instrucoes: this._instrucoes
                    })
                });
                const data = await res.json();
                if (data.ok) {
                    window.showToast('Anúncio salvo!', 'success');
                    window.dispatchEvent(new CustomEvent('anuncio-salvo', { detail: { produtoId: this._produtoId } }));
                } else {
                    window.showToast(data.erro || 'Erro ao salvar.', 'error');
                }
            } catch {
                window.showToast('Erro ao salvar.', 'error');
            } finally {
                this.salvando = false;
            }
        }
    };
}

// Alpine.js component for saved ads panel
function anunciosSalvos() {
    return {
        produtoId: '',
        itens: [],
        carregando: false,

        init() {
            window.addEventListener('produto-selecionado', (e) => {
                this.produtoId = e.detail.produtoId;
                this.carregar();
            });
            window.addEventListener('anuncio-salvo', (e) => {
                if (e.detail.produtoId === this.produtoId) this.carregar();
            });
        },

        async carregar() {
            if (!this.produtoId) return;
            this.carregando = true;
            try {
                const res = await fetch(`/anuncios/salvos?produtoId=${this.produtoId}`);
                this.itens = res.ok ? await res.json() : [];
            } catch {
                this.itens = [];
            } finally {
                this.carregando = false;
            }
        },

        copiarItem(conteudo) {
            navigator.clipboard.writeText(conteudo).then(() => {
                window.showToast('Copiado!', 'success');
            });
        },

        async deletar(id) {
            if (!confirm('Tem certeza que deseja excluir este anúncio?')) return;
            const token = document.querySelector('[name=__RequestVerificationToken]')?.value ?? '';
            try {
                const res = await fetch(`/anuncios/${id}/deletar`, {
                    method: 'POST',
                    headers: { 'RequestVerificationToken': token }
                });
                const data = await res.json();
                if (data.ok) {
                    this.itens = this.itens.filter(i => i.id !== id);
                    window.showToast('Anúncio removido.', 'success');
                } else {
                    window.showToast(data.erro || 'Erro ao remover.', 'error');
                }
            } catch {
                window.showToast('Erro ao remover.', 'error');
            }
        }
    };
}
