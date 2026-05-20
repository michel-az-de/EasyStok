/* combobox.js — Alpine factory para searchable select acessível (ARIA combobox).
 *
 * Uso básico (retrocompatível):
 *   <div x-data="combobox({ items: [{value:1,label:'Foo'}], name: 'cat' })">...</div>
 *
 * Uso com criação inline + limpar (requer _CreatableCombobox.cshtml):
 *   combobox({ ...,
 *     createUrl:     '/financeiro/categorias/criar-rapido',
 *     createPayload: { tipo: 'Despesa' },
 *     createLabel:   'Criar',
 *     allowClear:    true
 *   })
 */
window.combobox = function (config) {
    return {
        // ── estado base ───────────────────────────────────────────────────────
        items:       config.items || [],
        filtered:    [],
        query:       '',
        selected:    config.value ?? null,
        open:        false,
        activeIdx:   0,
        name:        config.name || 'combobox',
        placeholder: config.placeholder || 'Selecionar...',

        // ── criação inline (opcionais) ────────────────────────────────────────
        createUrl:     config.createUrl    || null,
        createPayload: config.createPayload || {},
        createLabel:   config.createLabel  || 'Criar',
        allowClear:    config.allowClear   || false,
        creating:      false,
        createError:   null,

        // ── inicialização ─────────────────────────────────────────────────────
        init() { this.filtered = this.items.slice(); this._syncLabel(); },

        _syncLabel() {
            const sel = this.items.find(i => String(i.value) === String(this.selected));
            this.query = sel ? sel.label : '';
        },

        // ── eventos de input ──────────────────────────────────────────────────
        onInput() {
            const q = (this.query || '').toLowerCase();
            this.filtered = this.items.filter(i => i.label.toLowerCase().includes(q));
            this.open = true;
            this.activeIdx = 0;
            this.createError = null;
        },

        onFocus() { this.filtered = this.items.slice(); this.open = true; },

        onBlur() {
            setTimeout(() => { this.open = false; this._syncLabel(); }, 120);
        },

        // ── teclado ───────────────────────────────────────────────────────────
        onKey(e) {
            const maxIdx = this.filtered.length - 1 + (this.showCreate() ? 1 : 0);
            if (!this.open && (e.key === 'ArrowDown' || e.key === 'Enter')) {
                this.open = true; e.preventDefault(); return;
            }
            if (e.key === 'ArrowDown') {
                this.activeIdx = Math.min(this.activeIdx + 1, maxIdx); e.preventDefault();
            } else if (e.key === 'ArrowUp') {
                this.activeIdx = Math.max(this.activeIdx - 1, 0); e.preventDefault();
            } else if (e.key === 'Enter') {
                if (this.showCreate() && this.activeIdx === this.filtered.length) {
                    this.create(); e.preventDefault();
                } else if (this.filtered[this.activeIdx]) {
                    this.pick(this.filtered[this.activeIdx]); e.preventDefault();
                }
            } else if (e.key === 'Escape') {
                this.open = false;
            }
        },

        // ── seleção ───────────────────────────────────────────────────────────
        pick(item) {
            this.selected = item.value;
            this.query    = item.label;
            this.open     = false;
            this.$dispatch('change', { value: item.value });
        },

        // ── criação inline ────────────────────────────────────────────────────
        exactMatch() {
            const q = (this.query || '').trim().toLowerCase();
            return q.length > 0 && this.items.some(i => i.label.toLowerCase() === q);
        },

        showCreate() {
            return !!this.createUrl
                && (this.query || '').trim().length > 0
                && !this.exactMatch()
                && !this.creating;
        },

        async create() {
            const nome = (this.query || '').trim();
            if (!nome) return;
            this.creating = true;
            this.createError = null;
            try {
                const token = this.$root
                    .closest('form')
                    ?.querySelector('input[name=__RequestVerificationToken]')
                    ?.value;
                const resp = await fetch(this.createUrl, {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                        'RequestVerificationToken': token || ''
                    },
                    body: JSON.stringify({ nome, ...this.createPayload })
                });
                const json = await resp.json();
                if (!resp.ok) {
                    this.createError = json.message || 'Erro ao criar.';
                } else {
                    const item = { value: json.id, label: json.label };
                    this.items.push(item);
                    this.filtered = this.items.filter(i =>
                        i.label.toLowerCase().includes(nome.toLowerCase()));
                    this.pick(item);
                }
            } catch {
                this.createError = 'Erro de conexão.';
            } finally {
                this.creating = false;
            }
        },

        // ── limpar ────────────────────────────────────────────────────────────
        clear() {
            this.selected = null;
            this.query    = '';
            this.filtered = this.items.slice();
            this.$dispatch('change', { value: null });
        }
    };
};
