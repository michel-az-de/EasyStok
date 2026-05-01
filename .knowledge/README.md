# EasyStock — Knowledge Base

> **Para o próximo Claude/agente que abrir esse repo:** leia este README primeiro. Depois decida quais arquivos abaixo abrir conforme a tarefa. Não leia tudo cego.

## O que é o projeto (em 3 frases)

EasyStock é um SaaS multi-tenant de gestão de estoque/foodservice em .NET 9 + Postgres + Clean Architecture, com PWA mobile e painel admin. Originado como white-label pra "Casa da Babá" (PWA mobile já em uso pelo dono Felipe + Thati). Hoje em **abril/2026** está sendo posicionado pra apresentar a clientes externos como ERP, mas está honestamente em **30-35% feature parity** vs Bling/Tiny/Omie.

## Quem é o usuário

Dono é **Felipe Azevedo** (`@michel-az-de` no GitHub, Avanade Brasil, .NET sênior). Estilo: direto, português BR, sem floreio, sem vírgulas sobrando, sem travessões. Já se irritou várias vezes com:
- Estimativas de custo otimistas (Azure estourou $474 em 30d quando estimei $50)
- Trabalho deixado pela metade ("e o que eu pedi do mobile?")
- Floreio em respostas — quer ação direta

**Sempre commit ao final de cada demanda, sem pedir confirmação** (memória persistente confirma isso).

## Estado atual da infra (atualize quando deploy mudar)

- **Azure subscription DESABILITADA** (spending limit hit, ~$474 estourados em 30d)
- **Recursos no Azure**: 3 App Services (api/web/admin) AdminDisabled, PG Flexible Server `pg-easystock` no RG `easy-stock`, FileStorage `easystockfiles` (StandardV2_LRS — caro)
- **Plano em andamento**: migrar pra **GCP Cloud Run + Cloud SQL** ($300 grátis 90d, free tier permanente). Script pronto em `scripts/gcp-deploy.sh`. Aguarda usuário criar conta GCP e mandar Project ID.
- **Alternativa de emergência**: cloudflared instalado em `~/bin/cloudflared.exe` pra rodar local + tunnel HTTPS público $0

## Estado do código (último commit relevante)

Último commit: `340aff0 fix(99%): Produtos + Pedido→Estoque + Estoque(web)`. **457/457 testes verdes**.

Qualidade por área (auditoria honesta — `audit-brutal.md` tem detalhes):

| Domínio | Q% | Prod-Ready |
|---|---|---|
| Identity/Auth/Multi-tenant | 49% | Parcial |
| Estoque/Catálogo | 49% | Parcial |
| Vendas/Pedidos/Compras/Caixa | 56% | ⚠️ Compras quebrada |
| Subscription/Billing/Pix | 47% | ⚠️ Vuln R$0,01 |
| Analytics/IA/Notifs | 47% | Parcial |
| Admin/Diagnostics | 57% | 🚨 Diagnóstico público |
| Mobile/PWA | 48% | Parcial (white-label OK) |
| Infra/Jobs/Email | 60% | Parcial |

## Convenções ativas (siga)

Veja `conventions.md`. TL;DR:
- Clean Architecture estrita (Domain → Application → Infra → Api)
- Multi-tenant via filtros manuais por `EmpresaId` (sem EF Global Query Filter — bug-prone)
- xmin RowVersion em entidades-chave (Postgres system column, sem migration)
- Use cases injetam interfaces de repos via primary constructor
- Idempotência via `MovimentacaoEstoque.DocumentoReferencia = "{pedidoId}:{itemId}"`
- Status de pedido como string ("aguardando", "preparando", "pronto", "entregue", "cancelado") com matriz de transições explícita
- Testes: xUnit + NSubstitute + FluentAssertions
- Sem comentários XML excessivos; comentar APENAS o "porquê" não-óbvio

## Não-faça (mistakes registrados)

Veja `do-not-do.md`. Top 5:
1. **Não estimar custos cloud sem auditar TUDO da subscription** (estourei $474 vs estimativa de $50)
2. **Não recomendar `[AllowAnonymous]` em endpoints destrutivos** (libei `/diagnostico` e expus `ProxyLimparLogs` etc)
3. **Não criar dependência nova sem checar se pacote existe** (.NET Application layer não tinha `Microsoft.Extensions.Configuration`)
4. **Não usar `Math.Ceiling` em qty fracionária pra desconto** (vira saldo negativo silencioso)
5. **Não fazer migration EF que duplique tabelas já criadas em SQL raw** (Mobile schema vs `AddAdminModule`)

## Arquivos desta pasta

- `README.md` — este arquivo (sempre comece aqui)
- `architecture.md` — Clean Arch, projetos, fluxo de dependências
- `domain-glossary.md` — Pedido vs Venda, MovimentacaoEstoque, etc
- `conventions.md` — naming, padrões de código, DI, testes
- `current-state.md` — features e probabilidades (atualizar conforme avança)
- `tech-debt.md` — `[NotMapped]`, stubs, hardcoded, TODOs reais
- `do-not-do.md` — erros já cometidos e por quê
- `recent-evolution.md` — últimos commits + decisões (auto-gerado por update.sh)
- `quick-reference.md` — comandos Bash/dotnet/git mais usados
- `audit-brutal.md` — última auditoria sênior pessimista completa
- `gcp-deploy.md` — passo-a-passo do deploy GCP
- `update.sh` — script que regenera as partes auto-discovery

## Como usar isso pra economizar tokens

**Cenário 1 — começar uma conversa nova:** Lê só este README. Pergunta o que o usuário quer. Aí abre 1-2 arquivos específicos.

**Cenário 2 — usuário pediu algo de pedido/estoque:** Abre `domain-glossary.md` + `current-state.md` (seção pedidos) + arquivo do código relevante.

**Cenário 3 — usuário pediu deploy:** Abre `gcp-deploy.md` + `current-state.md` (seção infra).

**Cenário 4 — usuário pediu auditoria:** Abre `audit-brutal.md` (não roda novos agentes — já tem dados de 2026-05-01).

## Manutenção

Rode `bash .knowledge/update.sh` ao final de sessões grandes pra atualizar `recent-evolution.md` automaticamente. Os outros arquivos são curados manualmente.
