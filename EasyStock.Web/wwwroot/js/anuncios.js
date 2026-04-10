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

        buscarProduto() {
            clearTimeout(this._debounce);
            this._debounce = setTimeout(async () => {
                if (this.busca.length < 2) { this.resultados = []; return; }
                try {
                    const res = await fetch(`/produtos/buscar?q=${encodeURIComponent(this.busca)}`);
                    this.resultados = await res.json();
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
        },

        gerarAnuncio() {
            if (!this.produtoId) return;
            this.gerando = true;

            // Dispatch event to resultado component
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
        _source: null,

        init() {
            window.addEventListener('anuncio-gerar', (e) => {
                this.iniciarStream(e.detail);
            });
        },

        iniciarStream({ produtoId, canal, tom, foco, contexto }) {
            // Close previous stream if any
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

            const url = `/anuncios/gerar?${params}`;

            // Use fetch with ReadableStream for SSE
            fetch(url)
                .then(res => {
                    if (!res.ok) {
                        this.resultado = 'Erro ao gerar anúncio. Tente novamente.';
                        this.gerando = false;

                        // Notify parent form
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

            // Listen for completion to reset the form's gerando state
            window.addEventListener('anuncio-concluido', () => {
                // Notify parent form component
                const event = new CustomEvent('anuncio-finalizado');
                window.dispatchEvent(event);
            }, { once: true });
        },

        copiar() {
            if (!this.resultado) return;
            navigator.clipboard.writeText(this.resultado).then(() => {
                this.copiado = true;
                setTimeout(() => { this.copiado = false; }, 2000);
            });
        },

        salvarRascunho() {
            if (!this.resultado) return;
            const key = `anuncio_rascunho_${Date.now()}`;
            localStorage.setItem(key, JSON.stringify({
                texto: this.resultado,
                savedAt: new Date().toISOString()
            }));
            window.showToast('Rascunho salvo localmente!', 'success');
        }
    };
}

// Reset gerando state when anuncio finalizado
window.addEventListener('anuncio-finalizado', () => {
    // Alpine components handle their own state via events
});
