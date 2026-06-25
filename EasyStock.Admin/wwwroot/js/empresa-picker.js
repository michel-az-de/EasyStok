// empresa-picker.js — factory Alpine do picker de empresa (tenant) reutilizavel (IMP-001).
// Carregado no _Layout ANTES do alpine.js (mesmo padrao de admin-components.js), pra que
// window.empresaPicker exista quando o Alpine processar x-data — inclusive quando o partial
// _EmpresaPicker e renderizado dentro de <template x-if> (script inline em template clonado
// nao executa). Typeahead sobre /api-proxy/buscar-global, derivado do modal Novo Ticket.
window.empresaPicker = function (opts) {
    opts = opts || {};
    return {
        mode: opts.mode || 'field',
        navBase: opts.navBase || '',
        sel: opts.id ? { id: opts.id, nome: opts.nome || opts.id, documento: opts.doc || '' } : null,
        busca: '',
        resultados: [],
        erro: '',
        _debounce: null,

        onInput() {
            clearTimeout(this._debounce);
            this.erro = '';
            const termo = this.busca.trim();
            if (termo.length < 2) { this.resultados = []; return; }
            this._debounce = setTimeout(() => this._buscar(termo), 200);
        },

        async _buscar(termo) {
            try {
                const r = await fetch('/api-proxy/buscar-global?q=' + encodeURIComponent(termo) + '&limit=8',
                    { credentials: 'same-origin', cache: 'no-store' });
                if (r.status === 401) { window.location.href = '/Auth/Login'; return; }
                if (!r.ok) { this.erro = 'Falha ao buscar empresas.'; this.resultados = []; return; }
                const d = await r.json();
                this.resultados = (d.data && d.data.clientes) || d.clientes || [];
            } catch (e) {
                this.erro = 'Falha de rede ao buscar empresas.';
                this.resultados = [];
            }
        },

        selecionar(emp) {
            this.resultados = [];
            if (this.mode === 'navigate') {
                window.location.href = this.navBase + '?empresaId=' + encodeURIComponent(emp.id);
                return;
            }
            this.sel = emp;
        },

        trocar() {
            this.sel = null;
            this.busca = '';
            this.resultados = [];
        }
    };
};
