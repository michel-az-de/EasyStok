// Alpine.js component pra cadastro de ficha tecnica nutricional do produto.
// Backend: PUT /api/produtos/{id}/ficha-tecnica (Operador), VO ProdutoFichaTecnica.
// Whitelist de alergenos espelha ProdutoFichaTecnicaValidator (9 itens).
function fichaTecnicaForm(produtoId, initialAtributosJson) {
    return {
        produtoId,
        porcaoG: null,
        kcal: null,
        carbsG: null,
        proteinaG: null,
        gorduraG: null,
        gorduraSaturadaG: null,
        fibrasG: null,
        sodioMg: null,
        modoPreparo: '',
        ingredientesInput: '',
        ingredientes: [],
        alergenos: [],
        alergenosOutros: '',
        attemptedSubmit: false,
        salvando: false,
        savedAt: null,
        expandido: false,

        ALERGENOS_DISPONIVEIS: [
            { codigo: 'gluten', label: 'Glúten' },
            { codigo: 'lactose', label: 'Lactose' },
            { codigo: 'ovo', label: 'Ovo' },
            { codigo: 'soja', label: 'Soja' },
            { codigo: 'amendoim', label: 'Amendoim' },
            { codigo: 'castanhas', label: 'Castanhas' },
            { codigo: 'peixe', label: 'Peixe' },
            { codigo: 'crustaceos', label: 'Crustáceos' },
            { codigo: 'outros', label: 'Outros' }
        ],

        init() {
            if (initialAtributosJson) {
                this._hidratar(initialAtributosJson);
                this.expandido = this._temFicha();
            }
        },

        _hidratar(json) {
            try {
                const d = typeof json === 'string' ? JSON.parse(json) : json;
                const n = d.nutricional || {};
                this.porcaoG = n.porcao_g ?? null;
                this.kcal = n.kcal ?? null;
                this.carbsG = n.carbs_g ?? null;
                this.proteinaG = n.proteina_g ?? null;
                this.gorduraG = n.gordura_g ?? null;
                this.gorduraSaturadaG = n.gordura_saturada_g ?? null;
                this.fibrasG = n.fibras_g ?? null;
                this.sodioMg = n.sodio_mg ?? null;
                this.modoPreparo = d.modo_preparo ?? '';
                this.ingredientes = Array.isArray(d.ingredientes) ? [...d.ingredientes] : [];
                this.alergenos = Array.isArray(d.alergenos) ? [...d.alergenos] : [];
                this.alergenosOutros = d.alergenos_outros ?? '';
            } catch {
                // JSON invalido — fica vazio. Nao bloqueia UI.
            }
        },

        _temFicha() {
            return this.porcaoG != null || this.kcal != null || this.carbsG != null ||
                   this.proteinaG != null || this.gorduraG != null ||
                   this.gorduraSaturadaG != null || this.fibrasG != null ||
                   this.sodioMg != null || (this.modoPreparo?.length > 0) ||
                   this.ingredientes.length > 0 || this.alergenos.length > 0;
        },

        toggleAlergeno(codigo) {
            const i = this.alergenos.indexOf(codigo);
            if (i >= 0) this.alergenos.splice(i, 1);
            else this.alergenos.push(codigo);
        },

        temAlergeno(codigo) {
            return this.alergenos.includes(codigo);
        },

        adicionarIngrediente() {
            const v = (this.ingredientesInput ?? '').trim();
            if (!v) return;
            if (v.length > 200) {
                window.showToast('Ingrediente deve ter no máximo 200 caracteres.', 'error');
                return;
            }
            if (this.ingredientes.length >= 50) {
                window.showToast('Máximo 50 ingredientes.', 'error');
                return;
            }
            this.ingredientes.push(v);
            this.ingredientesInput = '';
        },

        removerIngrediente(idx) {
            this.ingredientes.splice(idx, 1);
        },

        _validar() {
            this.attemptedSubmit = true;
            const numericos = [
                ['porcaoG', 'Porção'],
                ['kcal', 'Calorias'],
                ['carbsG', 'Carboidratos'],
                ['proteinaG', 'Proteínas'],
                ['gorduraG', 'Gorduras totais'],
                ['gorduraSaturadaG', 'Gorduras saturadas'],
                ['fibrasG', 'Fibras'],
                ['sodioMg', 'Sódio']
            ];
            for (const [campo, label] of numericos) {
                const v = this[campo];
                if (v == null || v === '') continue;
                const n = Number(v);
                if (!Number.isFinite(n) || n < 0 || n > 9999) {
                    window.showToast(`${label}: aceito entre 0 e 9999.`, 'error');
                    return false;
                }
            }
            if (this.modoPreparo && this.modoPreparo.length > 4000) {
                window.showToast('Modo de preparo deve ter no máximo 4000 caracteres.', 'error');
                return false;
            }
            if (this.alergenos.includes('outros') && !this.alergenosOutros.trim()) {
                window.showToast('Especifique os outros alérgenos no campo de texto.', 'error');
                return false;
            }
            if (this.alergenosOutros && this.alergenosOutros.length > 200) {
                window.showToast('Outros alérgenos: máximo 200 caracteres.', 'error');
                return false;
            }
            return true;
        },

        async salvar() {
            if (!this._validar()) return;
            this.salvando = true;
            const token = document.querySelector('[name=__RequestVerificationToken]')?.value ?? '';
            const body = {
                porcaoG: this._toDecimal(this.porcaoG),
                kcal: this._toDecimal(this.kcal),
                carbsG: this._toDecimal(this.carbsG),
                proteinaG: this._toDecimal(this.proteinaG),
                gorduraG: this._toDecimal(this.gorduraG),
                gorduraSaturadaG: this._toDecimal(this.gorduraSaturadaG),
                fibrasG: this._toDecimal(this.fibrasG),
                sodioMg: this._toDecimal(this.sodioMg),
                modoPreparo: this.modoPreparo?.trim() || null,
                ingredientes: this.ingredientes.length ? this.ingredientes : null,
                alergenos: this.alergenos.length ? this.alergenos : null,
                alergenosOutros: this.alergenosOutros?.trim() || null
            };
            try {
                const res = await fetch(`/produtos/${this.produtoId}/ficha-tecnica`, {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                        'RequestVerificationToken': token
                    },
                    body: JSON.stringify(body)
                });
                if (!res.ok) {
                    const data = await res.json().catch(() => ({}));
                    window.showToast(data.erro || 'Erro ao salvar ficha.', 'error');
                    return;
                }
                window.showToast('Ficha nutricional salva!', 'success');
                this.savedAt = new Date();
                this.attemptedSubmit = false;
            } catch {
                window.showToast('Erro de conexão.', 'error');
            } finally {
                this.salvando = false;
            }
        },

        _toDecimal(v) {
            if (v === '' || v == null) return null;
            const n = Number(v);
            return Number.isFinite(n) ? n : null;
        }
    };
}
