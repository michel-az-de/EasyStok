# Recent Evolution

> Auto-gerável via `bash .knowledge/update.sh`. Atualizar manualmente se script não rodou.

## Snapshot 2026-04-30

### Últimos 10 commits
```
1ce968c fix(bugs): 5 bugs confirmados — segurança, tenant isolation, middleware
30b630f fix(admin): corrigir bypass de autenticação e URL hardcoded na listagem de tenants
45dcd8d fix(admin): segunda revisão — 4 defeitos corrigidos
e1b7ba8 fix(admin): corrigir bugs no painel admin - sessão, planos e impersonation
2d16c28 fix(admin): auditoria e pente fino do design system
340aff0 fix(99%): Produtos + Pedido→Estoque + Estoque(web)
```

### Decisões de arquitetura recentes
- **2026-04**: PedidoEstoqueIntegrationService extraído pra service dedicado com `PedidoEstoqueOptions` (PermiteEstoqueNegativo, RequerEstoqueExistente).
- **2026-04**: `AtualizarStatusPedidoUseCase` com state machine matrix explícita + `GetByIdWithDetailsAsync` (corrige bug de itens vazios).
- **2026-04**: xmin RowVersion adicionado a Produto, Pedido, ItemEstoque, AssinaturaEmpresa.
- **2026-04**: SubscriptionGateMiddleware retornando 402 para tenants suspensos.
- **2026-04**: Webhook Pix com HMAC-SHA256 + replay protection (mas SEM validação de valor — pendente).
- **2026-04**: Idempotência de movimentação de estoque por `{pedidoId}:{itemId}` (não só pedidoId).

### Direção atual
- Migração Azure → GCP em andamento (aguarda Project ID do usuário)
- Foco P0: corrigir `PedidoFornecedor.Itens [NotMapped]` e validação webhook Pix antes de cliente externo
- Knowledge base (`.knowledge/`) criada pra reduzir custo de contexto em sessões futuras
