# Current State — EasyStock (abril/2026)

> Snapshot em 2026-04-30. Atualize quando deploy ou auditoria mudar.

## Numbers
- Branch: `master`
- Último commit relevante: `1ce968c fix(bugs): 5 bugs confirmados — segurança, tenant isolation, middleware`
- Testes: **457/457 verdes**
- Feature parity vs Bling/Tiny/Omie: **30–35%** (auditoria honesta)

## Infra
- ❌ Azure subscription **DESABILITADA** (spending limit hit, $474 em 30 dias)
- 🟡 Plano: migrar pra **GCP Cloud Run + Cloud SQL** ($300 grátis 90d). Script em `scripts/gcp-deploy.sh`.
- ⏳ Aguarda usuário: criar conta GCP + enviar Project ID
- 🆘 Backup de emergência: `cloudflared` em `~/bin/cloudflared.exe` pra tunnel local público

## Features por área

### ✅ OK pra produção real
- Auth básico (JWT + refresh + cookie web)
- Multi-tenant filtrado manual
- Pedido (criação + state machine + desconto estoque com idempotência)
- Produto CRUD + SKU
- Estoque entradas/saídas (web)

### 🟡 Parcial (usável mas com gap)
- Caixa (abre/fecha/lançamento — sem reconciliação rica)
- Vendas / Pedidos
- Subscription/Billing (Trial OK, Pix Efí integrado mas SEM validação de valor no webhook ⚠️)
- Analytics/Dashboard (números OK, sem drill-down)
- IA (geração funciona, contador mensal OK)
- Mobile PWA (Casa da Babá em uso real, white-label)
- Admin global (lista tenants, impersonate)

### ❌ Quebrado / não-prod
- **Compras (`PedidoFornecedor`)**: `Itens` é `[NotMapped]` — não persiste itens. Recebimento não dá entrada correta de estoque.
- **NF-e/NFC-e**: zero
- **Integração com marketplaces**: zero
- **Multi-empresa por usuário**: parcial

## Vulnerabilidades conhecidas
1. ⚠️ Webhook Pix não valida valor (paga R$0,01 → ativa plano)
2. ⚠️ `DiagnosticoController` — confirmar que rotas destrutivas estão atrás de `[Authorize(Roles="SuperAdmin")]`

## Commits recentes (últimos 5)
```
1ce968c fix(bugs): 5 bugs confirmados — segurança, tenant isolation, middleware
30b630f fix(admin): corrigir bypass de autenticação e URL hardcoded na listagem de tenants
45dcd8d fix(admin): segunda revisão — 4 defeitos corrigidos
e1b7ba8 fix(admin): corrigir bugs no painel admin - sessão, planos e impersonation
2d16c28 fix(admin): auditoria e pente fino do design system
```

## O que falta pro MVP-pago
1. Fix `PedidoFornecedor.Itens` (P0)
2. Validação valor webhook Pix (P0)
3. NF-e mínimo (emissor)
4. Deploy GCP estável
5. Onboarding de cliente externo testado (signup → trial → upgrade)
