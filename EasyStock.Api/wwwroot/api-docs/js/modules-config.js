// Mapeamento de tags OpenAPI → módulos do console.
// Tags não listadas explicitamente caem em "sistema" (catch-all) ou "admin" via tagPrefix.

export const MODULES = [
    {
        id: 'vendas',
        name: 'Vendas',
        icon: 'sword',
        accent: 'accent',
        tags: ['Venda', 'Vendas', 'Pedido', 'Pedidos', 'WebhookPix'],
        desc: 'Pedidos, vendas e cobrança Pix.'
    },
    {
        id: 'estoque',
        name: 'Estoque',
        icon: 'package',
        accent: 'basil',
        tags: ['Produto', 'Produtos', 'ItemEstoque', 'ItensEstoque', 'Movimentacao', 'Movimentacoes', 'Categoria', 'Categorias', 'Lote', 'Lotes'],
        desc: 'Catálogo, itens, movimentações e lotes.'
    },
    {
        id: 'caixa',
        name: 'Caixa',
        icon: 'card',
        accent: 'gold',
        tags: ['Caixa'],
        desc: 'Movimentações de caixa, abertura e fechamento.'
    },
    {
        id: 'pessoas',
        name: 'Pessoas',
        icon: 'users',
        accent: 'cyan',
        tags: ['Usuario', 'Usuarios', 'Empresa', 'Empresas', 'Cliente', 'Clientes', 'Fornecedor', 'Fornecedores'],
        desc: 'Usuários, empresas, clientes e fornecedores.'
    },
    {
        id: 'inteligencia',
        name: 'Inteligência',
        icon: 'brain',
        accent: 'magenta',
        tags: ['Analytics', 'Inteligencia', 'InteligenciaLojas', 'IaAnuncio', 'ListasCompras'],
        desc: 'Dashboard, projeções, IA e listas de compras.'
    },
    {
        id: 'auth',
        name: 'Auth',
        icon: 'lock',
        accent: 'gold',
        tags: ['Auth'],
        desc: 'Login, refresh e perfil do usuário.'
    },
    {
        id: 'admin',
        name: 'Admin',
        icon: 'wrench',
        accent: 'danger',
        tags: ['Diagnostico', 'DiagnosticoInfra', 'DiagnosticoLogs'],
        tagPrefix: ['Admin'],
        desc: 'Diagnóstico operacional e administração de tenants.'
    },
    {
        id: 'sistema',
        name: 'Sistema',
        icon: 'gear',
        accent: 'ink-dim',
        tags: ['Configuracoes', 'Loja', 'Plano', 'Notificacao', 'Notificacoes', 'NotificacoesJobs', 'Uploads', 'Tickets', 'Consentimentos', 'AssinaturaCliente', 'Outros'],
        catchAll: true,
        desc: 'Configurações, planos, notificações e suporte.'
    }
];

const tagToModule = new Map();
for (const m of MODULES) {
    for (const t of (m.tags || [])) {
        tagToModule.set(t.toLowerCase(), m);
    }
}

export function moduleForTag(tag) {
    if (!tag) return catchAllModule();
    const exact = tagToModule.get(String(tag).toLowerCase());
    if (exact) return exact;
    for (const m of MODULES) {
        if (m.tagPrefix) {
            for (const p of m.tagPrefix) {
                if (String(tag).toLowerCase().startsWith(p.toLowerCase())) return m;
            }
        }
    }
    return catchAllModule();
}

function catchAllModule() {
    return MODULES.find(m => m.catchAll) || MODULES[MODULES.length - 1];
}

export function moduleById(id) {
    return MODULES.find(m => m.id === id) || null;
}

export function endpointsForModule(spec, moduleId) {
    if (!spec) return [];
    const eps = [];
    for (const [tag, list] of spec.byTag.entries()) {
        if (moduleForTag(tag).id === moduleId) {
            eps.push(...list);
        }
    }
    return eps.sort((a, b) => a.path.localeCompare(b.path) || a.method.localeCompare(b.method));
}

export function modulesWithCounts(spec) {
    if (!spec) return MODULES.map(m => ({ ...m, count: 0, requiresAuth: false }));
    return MODULES.map(m => {
        const eps = endpointsForModule(spec, m.id);
        const requiresAuth = eps.some(e => e.security && e.security.length > 0);
        return { ...m, count: eps.length, requiresAuth };
    });
}
