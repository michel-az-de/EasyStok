# Auditoria Brutal — EasyStock (2026-04-30)

> Última auditoria sênior pessimista. Não regenere — usa muitos tokens. Atualize manualmente quando houver mudança material.

## TL;DR
**Feature parity vs Bling/Tiny/Omie: 30–35%**. Vendável como "sistema de gestão simples" pra microempresas que NÃO precisam de NF-e. Não vendável como ERP pra negócio sério.

## Score por domínio

| Domínio | Q% | Usabilidade | Competitividade | Prod-Ready |
|---|---|---|---|---|
| Identity / Auth / Multi-tenant | 49% | 60% | 50% | Parcial |
| Estoque / Catálogo | 49% | 55% | 40% | Parcial |
| Vendas / Pedidos / Compras / Caixa | 56% | 60% | 35% | ⚠️ Compras quebrada |
| Subscription / Billing / Pix | 47% | 50% | 45% | ⚠️ Vuln R$0,01 |
| Analytics / IA / Notifs | 47% | 55% | 50% | Parcial |
| Admin / Diagnostics | 57% | 65% | n/a | 🚨 Diagnóstico |
| Mobile PWA | 48% | 70% (Casa da Babá) | 30% | Parcial |
| Infra / Jobs / Email | 60% | n/a | n/a | Parcial |

## Top bloqueadores P0

1. **`PedidoFornecedor.Itens [NotMapped]`** — compras simplesmente não persistem. Negócio não consegue rastrear entrada de mercadoria.
2. **Webhook Pix sem validação de valor** — vulnerabilidade R$0,01: cliente paga centavo e ativa plano de R$199.
3. **Sem NF-e/NFC-e** — qualquer cliente brasileiro que vende vai precisar. Bling/Tiny têm de fábrica.
4. **`DiagnosticoController` exposto** — endpoints destrutivos atrás de `[AllowAnonymous]` em algum ponto da história. Verificar estado atual.
5. **Compras → Estoque quebrado** — cascata de #1.

## Top gaps competitivos

- ❌ Sem integração com marketplaces (Mercado Livre, Shopee, Magalu)
- ❌ Sem emissão fiscal (NF-e, NFC-e, NFS-e, MDF-e)
- ❌ Sem Bling/Tiny export/import
- ❌ Sem app móvel nativo (PWA não conta pra muitos clientes)
- ❌ Sem multi-empresa por usuário (1 user = 1 tenant)
- ❌ Sem gestão financeira (contas a pagar/receber, fluxo de caixa real)
- ❌ Sem precificação por canal/marketplace
- ❌ Sem variantes/grades (cor/tamanho)
- ❌ Sem etiquetas/código de barras pra impressão

## Pontos fortes reais

- ✅ Multi-tenant funcionando de verdade (filtros manuais funcionam, testado)
- ✅ Idempotência de movimentação de estoque (raro até em concorrentes)
- ✅ State machine de pedido bem definida
- ✅ PWA white-label em produção real (Casa da Babá usa diariamente)
- ✅ 457 testes verdes (nem todo concorrente tem isso)
- ✅ Clean Architecture estrita (manutenção barata long-term)
- ✅ Subscription/billing infra pronta (Trial 14d, Pix Efí, gate middleware)

## Recomendação honesta

**Curto prazo (próximas 4 semanas):**
1. Fix P0 #1, #2, #4
2. Deploy GCP estável
3. Onboarding 1-2 clientes piloto **gratuitos** (não cobrar ainda)
4. Aceitar que primeiro cliente pagante real provavelmente vai exigir NF-e

**Médio prazo (3-6 meses):**
1. Integrar emissor NF-e third-party (Focus NFe, eNotas)
2. Sync básico Mercado Livre OU Shopee (escolher 1)
3. Variantes/grades de produto
4. Gestão financeira (contas a pagar/receber)

**Não focar em:**
- Reescrever PWA (funciona)
- Sair do Postgres (funciona)
- Adicionar Hangfire/Quartz (background services bastam)
- Microservices (Clean Arch monolith é certo pra esse tamanho)

## Verdade dolorosa
Se um cliente brasileiro real testar EasyStock vs Bling Free hoje, o Bling ganha em 5 minutos por causa de NF-e e marketplaces. EasyStock ganha em **simplicidade do PWA mobile** e **billing self-service rápido** — nichos pequenos, mas reais (foodservice/delivery sem nota).
