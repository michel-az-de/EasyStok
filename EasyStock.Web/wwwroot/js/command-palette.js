/**
 * Command Palette — Ctrl/Cmd+K abre busca global e acoes rapidas.
 *
 * Comandos estaticos sao sempre listados (navegacao + criar X).
 * Busca dinamica em /api/clientes/buscar quando o usuario digita.
 *
 * Navegacao: ↑ ↓ Enter Esc.
 */
(function () {
    'use strict';

    var STATIC_COMMANDS = [
        // Acoes (criar X)
        { group: 'Ações',    label: 'Novo cliente',      icon: 'user-plus',  href: '/clientes#novo-cliente',    keywords: 'criar cadastrar' },
        { group: 'Ações',    label: 'Novo pedido',       icon: 'plus',       href: '/pedidos#novo-pedido',      keywords: 'criar venda' },
        { group: 'Ações',    label: 'Novo produto',      icon: 'package',    href: '/produtos/novo',            keywords: 'criar cadastrar sku' },
        { group: 'Ações',    label: 'Novo fornecedor',   icon: 'truck',      href: '/fornecedores#novo-fornecedor', keywords: 'criar' },
        { group: 'Ações',    label: 'Registrar entrada', icon: 'arrow-down', href: '/entradas/nova',            keywords: 'estoque adicionar comprar' },
        { group: 'Ações',    label: 'Registrar saída',   icon: 'arrow-up',   href: '/saidas/nova',              keywords: 'estoque vender baixa' },

        // Navegacao
        { group: 'Ir para',  label: 'Dashboard',         icon: 'home',       href: '/dashboard',                keywords: 'inicio painel' },
        { group: 'Ir para',  label: 'Produtos',          icon: 'package',    href: '/produtos',                 keywords: 'catalogo sku' },
        { group: 'Ir para',  label: 'Estoque',           icon: 'box',        href: '/estoque',                  keywords: 'inventario' },
        { group: 'Ir para',  label: 'Clientes',          icon: 'users',      href: '/clientes',                 keywords: 'clientes' },
        { group: 'Ir para',  label: 'Pedidos',           icon: 'list',       href: '/pedidos',                  keywords: 'vendas' },
        { group: 'Ir para',  label: 'Fornecedores',      icon: 'truck',      href: '/fornecedores',             keywords: '' },
        { group: 'Ir para',  label: 'Caixa',             icon: 'cash',       href: '/caixa',                    keywords: 'financeiro dinheiro' },
        { group: 'Ir para',  label: 'Lotes',             icon: 'tag',        href: '/lotes',                    keywords: 'producao etiquetas' },
        { group: 'Ir para',  label: 'Análises & IA',     icon: 'chart',      href: '/analytics',                keywords: 'relatorios bi insights' },
        { group: 'Ir para',  label: 'Compras',           icon: 'cart',       href: '/listas-compras',           keywords: 'lista mercado' },
        { group: 'Ir para',  label: 'Configurações',     icon: 'cog',        href: '/configuracoes',            keywords: 'ajustes settings' },
        { group: 'Ir para',  label: 'Usuários',          icon: 'users',      href: '/usuarios',                 keywords: 'equipe time' }
    ];

    // SVG icons (24x24 stroke, currentColor). Set minimal — outros caem em fallback.
    var ICONS = {
        'user-plus':  'M16 21v-2a4 4 0 00-4-4H5a4 4 0 00-4 4v2 M12.5 7a4 4 0 11-8 0 4 4 0 018 0z M20 8v6 M23 11h-6',
        'plus':       'M12 4v16m8-8H4',
        'package':    'M20 7l-8-4-8 4m16 0l-8 4m8-4v10l-8 4m0-10L4 7m8 4v10M4 7v10l8 4',
        'truck':      'M9 17a2 2 0 100 4 2 2 0 000-4zm10 0a2 2 0 100 4 2 2 0 000-4zm-15-4h11V5H4v8zm11-3h3l3 3v3h-6V10z',
        'arrow-down': 'M19 14l-7 7m0 0l-7-7m7 7V3',
        'arrow-up':   'M5 10l7-7m0 0l7 7m-7-7v18',
        'home':       'M3 12l2-2m0 0l7-7 7 7M5 10v10a1 1 0 001 1h3m10-11l2 2m-2-2v10a1 1 0 01-1 1h-3m-6 0a1 1 0 001-1v-4a1 1 0 011-1h2a1 1 0 011 1v4a1 1 0 001 1m-6 0h6',
        'box':        'M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2',
        'users':      'M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14a7 7 0 00-7-7z',
        'list':       'M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2m-6 9l2 2 4-4',
        'cash':       'M3 10h18M7 15h.01M11 15h2M5 19h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v10a2 2 0 002 2z',
        'tag':        'M5 8h14M5 8a2 2 0 110-4h14a2 2 0 110 4M5 8v10a2 2 0 002 2h10a2 2 0 002-2V8m-9 4h4',
        'chart':      'M9 19v-6a2 2 0 00-2-2H5a2 2 0 00-2 2v6a2 2 0 002 2h2a2 2 0 002-2zm0 0V9a2 2 0 012-2h2a2 2 0 012 2v10m-6 0a2 2 0 002 2h2a2 2 0 002-2m0 0V5a2 2 0 012-2h2a2 2 0 012 2v14a2 2 0 01-2 2h-2a2 2 0 01-2-2z',
        'cart':       'M3 3h2l.4 2M7 13h10l4-8H5.4M7 13L5.4 5M7 13l-2.293 2.293c-.63.63-.184 1.707.707 1.707H17m0 0a2 2 0 100 4 2 2 0 000-4zm-8 2a2 2 0 11-4 0 2 2 0 014 0z',
        'cog':        'M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.065 2.572c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.572 1.065c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.065-2.572c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.572-1.065z'
    };

    function getIcon(name) {
        return ICONS[name] || 'M9 12h6m-6 4h6m-7-8h.01M5 21h14a2 2 0 002-2V5a2 2 0 00-2-2H5a2 2 0 00-2 2v14a2 2 0 002 2z';
    }

    function normalize(s) {
        return (s || '').toString().toLowerCase()
            .normalize('NFD').replace(/[̀-ͯ]/g, '');
    }

    function filterStatic(query) {
        var q = normalize(query);
        if (!q) return STATIC_COMMANDS;
        return STATIC_COMMANDS.filter(function (c) {
            return normalize(c.label).indexOf(q) >= 0 ||
                   normalize(c.keywords).indexOf(q) >= 0;
        });
    }

    var debounceTimer = null;
    function debounce(fn, ms) {
        return function () {
            var args = arguments, ctx = this;
            clearTimeout(debounceTimer);
            debounceTimer = setTimeout(function () { fn.apply(ctx, args); }, ms);
        };
    }

    window.commandPalette = function () {
        return {
            open: false,
            query: '',
            staticResults: STATIC_COMMANDS,
            clienteResults: [],
            selectedIdx: 0,
            loading: false,

            init() {
                var self = this;
                // Atalho global Ctrl+K (Win/Linux) ou Cmd+K (Mac)
                window.addEventListener('keydown', function (e) {
                    if ((e.ctrlKey || e.metaKey) && e.key === 'k') {
                        e.preventDefault();
                        self.toggle();
                    }
                });
            },

            toggle() {
                this.open = !this.open;
                if (this.open) {
                    this.query = '';
                    this.staticResults = STATIC_COMMANDS;
                    this.clienteResults = [];
                    this.selectedIdx = 0;
                    this.$nextTick(() => {
                        try { this.$refs.input && this.$refs.input.focus(); } catch (_) {}
                    });
                }
            },

            close() {
                this.open = false;
                this.query = '';
            },

            allResults() {
                return this.staticResults.concat(this.clienteResults);
            },

            onQuery() {
                this.staticResults = filterStatic(this.query);
                this.selectedIdx = 0;
                this.searchClientes();
            },

            searchClientes: debounce(function () {
                var self = this;
                var q = (self.query || '').trim();
                if (q.length < 2) {
                    self.clienteResults = [];
                    return;
                }
                self.loading = true;
                fetch('/api/clientes/buscar?termo=' + encodeURIComponent(q) + '&max=5', {
                    credentials: 'same-origin',
                    headers: { 'Accept': 'application/json' }
                })
                    .then(function (r) { return r.ok ? r.json() : null; })
                    .then(function (json) {
                        self.loading = false;
                        var list = (json && (json.data || json.items || json)) || [];
                        if (!Array.isArray(list)) { self.clienteResults = []; return; }
                        self.clienteResults = list.map(function (c) {
                            return {
                                group: 'Clientes',
                                icon: 'users',
                                label: c.nome || c.Nome || '—',
                                hint: c.documento || c.Documento || c.telefone || c.Telefone || '',
                                href: '/clientes/' + (c.id || c.Id)
                            };
                        });
                    })
                    .catch(function () { self.loading = false; self.clienteResults = []; });
            }, 220),

            moveSelection(delta) {
                var max = this.allResults().length;
                if (max === 0) return;
                this.selectedIdx = (this.selectedIdx + delta + max) % max;
                this.$nextTick(() => {
                    var el = document.querySelector('[data-cmdpal-item="' + this.selectedIdx + '"]');
                    if (el && el.scrollIntoView) el.scrollIntoView({ block: 'nearest' });
                });
            },

            activate(idx) {
                var item = this.allResults()[idx];
                if (item && item.href) {
                    this.close();
                    window.location.href = item.href;
                }
            },

            onKeydown(e) {
                if (e.key === 'Escape') { this.close(); return; }
                if (e.key === 'ArrowDown') { e.preventDefault(); this.moveSelection(1); return; }
                if (e.key === 'ArrowUp')   { e.preventDefault(); this.moveSelection(-1); return; }
                if (e.key === 'Enter')     { e.preventDefault(); this.activate(this.selectedIdx); return; }
            },

            getIconPath(name) { return getIcon(name); }
        };
    };
})();
