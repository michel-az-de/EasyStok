/* EasyStok admin components — Alpine factories compartilhadas.
   Carregado pelo _Layout antes do alpine.cdn.min.js (defer). */

(function () {
    'use strict';

    // ── DataTable: gerencia seleção em massa via DOM-driven sets ──
    window.esDataTable = function () {
        return {
            selected: new Set(),
            allSelected: false,
            someSelected: false,

            init() {
                this.recompute();
                // Sincroniza quando o consumidor altera linhas dinamicamente.
                this.$nextTick(() => this.recompute());
            },

            _rowInputs() {
                return this.$refs.tbody?.querySelectorAll('input[type=checkbox][data-bulk-row]') || [];
            },

            onItemChange(event) {
                const t = event.target;
                if (!t || t.tagName !== 'INPUT' || t.type !== 'checkbox') return;
                if (!t.hasAttribute('data-bulk-row')) return;
                const v = t.value;
                if (t.checked) this.selected.add(v); else this.selected.delete(v);
                this.recompute();
            },

            toggleAll(checked) {
                const inputs = this._rowInputs();
                inputs.forEach(i => {
                    i.checked = checked;
                    if (checked) this.selected.add(i.value);
                    else this.selected.delete(i.value);
                });
                this.recompute();
            },

            clearAll() {
                this.selected.clear();
                this._rowInputs().forEach(i => { i.checked = false; });
                this.recompute();
            },

            recompute() {
                const inputs = this._rowInputs();
                const total = inputs.length;
                const checked = Array.from(inputs).filter(i => i.checked).length;
                this.allSelected = total > 0 && checked === total;
                this.someSelected = checked > 0 && checked < total;
            },

            // Helper para ações em massa: serializa IDs selecionados.
            // Uso: <form @submit="$event.target.querySelector('[name=ids]').value = idsJson()">
            idsJson() { return JSON.stringify(Array.from(this.selected)); },
            idsCsv()  { return Array.from(this.selected).join(','); },
            ids()     { return Array.from(this.selected); }
        };
    };

    // ── Bulk: carimba os IDs selecionados em hidden inputs do form alvo ──
    // Evita o gotcha do FormTagHelper (descarta @submit.prevent): os ids vao no
    // @click do botao, ANTES do submit nativo do form.
    window.esStampIds = function (formId, ids) {
        const form = document.getElementById(formId);
        if (!form) return 0;
        const slot = form.querySelector('[data-ids-slot]');
        if (!slot) return 0;
        slot.innerHTML = '';
        const list = ids || [];
        list.forEach(function (id) {
            const inp = document.createElement('input');
            inp.type = 'hidden';
            inp.name = 'ids';
            inp.value = id;
            slot.appendChild(inp);
        });
        return list.length;
    };

    // Carimba e submete (Exportar selecionados: form GET que baixa o CSV).
    window.esStampAndSubmit = function (formId, ids) {
        if (window.esStampIds(formId, ids) > 0) {
            document.getElementById(formId).submit();
        }
    };

    // ── Tabs: gerencia activeTab via querystring (?tab=key) ──
    window.esTabs = function (initialTab) {
        return {
            active: initialTab || '',

            init() {
                // Escuta navegações via ?tab= (browser back/forward).
                window.addEventListener('popstate', () => this._readFromUrl());
                this._readFromUrl();
            },

            _readFromUrl() {
                const params = new URLSearchParams(window.location.search);
                const t = params.get('tab');
                if (t) this.active = t;
            },

            select(key) {
                this.active = key;
                const params = new URLSearchParams(window.location.search);
                params.set('tab', key);
                const url = window.location.pathname + '?' + params.toString();
                history.replaceState(null, '', url);
            },

            isActive(key) { return this.active === key; }
        };
    };

    // ── CountUp helper para números — usado pelo StatCard quando animated=true ──
    window.esCountUp = function (target, durationMs) {
        return {
            display: '0',
            init() {
                const t = Number(target) || 0;
                const d = Number(durationMs) || 800;
                const isInt = Number.isInteger(t);
                const fmt = (n) => isInt
                    ? Math.round(n).toLocaleString('pt-BR')
                    : n.toLocaleString('pt-BR', { maximumFractionDigits: 1 });
                const start = performance.now();
                const tick = (now) => {
                    const p = Math.min(1, (now - start) / d);
                    const e = 1 - Math.pow(1 - p, 3);
                    this.display = fmt(t * e);
                    if (p < 1) requestAnimationFrame(tick);
                };
                requestAnimationFrame(tick);
            }
        };
    };

})();
