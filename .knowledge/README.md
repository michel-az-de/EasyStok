# EasyStok — Knowledge Base

> **Para o próximo agente que abrir este repo:** este é o single source of truth técnico do projeto. O `CLAUDE.md` da raiz aponta pra cá. Leia este README, depois decida quais arquivos abrir conforme a tarefa. Não leia tudo cego.

## O que é o projeto (em 3 frases)

EasyStok é um SaaS multi-tenant de gestão de estoque/foodservice em **.NET 9 + Postgres** com Clean Architecture, PWA mobile (Casa da Babá em produção real) e MAUI Android (produto SaaS em maturação). Hoje em **2026-05-06** tem **714+ commits**, 17 projetos, ~50 controllers, 57 entidades. Posicionado como ERP para microempresas que NÃO precisam de NF-e ainda; auditoria honesta estima **30–35% de feature parity** vs Bling/Tiny/Omie (`audit-brutal.md`).

## Quem é o usuário

Dono é **Felipe Azevedo** (`@michel-az-de` no GitHub, Avanade Brasil, .NET sênior). Estilo: direto, português BR, sem floreio, sem vírgulas sobrando, sem travessões. Já se irritou com:
- Estimativas de custo otimistas (Azure estourou $474 em 30d quando foi estimado $50)
- Trabalho deixado pela metade ("e o que eu pedi do mobile?")
- Floreio em respostas — quer ação direta

**Sempre commit + push ao final de cada demanda, sem pedir confirmação** (`feedback_commit.md` na auto-memória).

## Estado atual da infra (atualizado 2026-06-25)

- **VM Azure = producao canonica** desde 2026-06-06. `easystok-vm` (RG `easystok-app_group`, IP estatico `20.230.185.203` via sslip.io) roda docker-compose com web+admin+api+postgres+caddy+storefront. Deploy via `scripts/docker/vm-deploy.sh` (manual; NAO ha cron/timer — medido 2026-06-25, 2 crontabs vazios + systemd sem timer). Hosts: `app.`/`admin.`/`api.`/`casa.`/`casadababa.` `20.230.185.203.sslip.io`.
- **Postgres** roda em container na VM (volume `easystok_postgres-data`). PG 17.10. Backup: baseline manual 2026-06-25 (`docs/runbook/vm-baseline-snapshot-2026-06-25.md`) + hook pre-deploy em `vm-deploy.sh` (snapshot antes de todo `up --build`). Backup recorrente automatizado = pendente (F1).
- **Render DESLIGADO.** `easystok-*.onrender.com` retorna 404/503 (medido 2026-06-25). Sem dump postumo (Dashboard inacessivel). Dados so-server-side do Render perdidos (a VM comecou vazia + resync, sem transferencia server->server). `render.yaml` + workflows Render = historico. Ver `docs/dev/incidentes/2026-06-24-render-cutover-sem-dump-postumo.md`.
- **Fly.io DESLIGADO** (descomissionado 2026-06-25). Postgres Fly nao existe mais (`fly postgres list` vazio); `deploy-fly.yml` falhava ha 3+ commits por timeout de conexao. Workflow removido.
- **Azure App Service / Static Web Apps descomissionados** em 2026-05-11. `azurewebsites.net` morto (timeout). Plano antigo GCP removido — nunca executado.
- **APK Casa da Baba**: OTA via `capacitor.config.json` `updateUrl` -> `api.20.230.185.203.sslip.io/api/mobile/apk/manifest`. Pareamento/sync do APK aponta pra VM via `build-release-wsl.sh` `CDB_API_BASE_URL`. Convencao: `apiBaseUrl` SEM `/api` (sync.js concatena `/api/mobile`). Gotcha: URL e IP via sslip.io — se o IP da VM mudar, OTA quebra (registrar dominio resolve).
- **Cloudflared** em `~/bin/cloudflared.exe` como backup pra túnel HTTPS público (dev local).

## Estado do código

Último commit relevante: `78fdbc1 fix(notifications): pente fino — 30 correcoes`. Testes verdes em todas as suites principais (Domain.Tests, Application.Tests, Api.UnitTests, IntegrationTests com Testcontainers).

P0 antigos (`tech-debt.md`) RESOLVIDOS:
- ✅ `PedidoFornecedorItem` persiste (entity criada)
- ✅ Webhook Pix valida valor pago vs cobrança
- ✅ `DiagnosticoController` `[Authorize(Policy="Admin")]`

P0 atuais: NF-e, rate limiting em `/api/webhooks/pix` (auth/* já coberto — B-015 fechado em 2026-05-07), CI gate de qualidade.

Qualidade por área (auditoria `audit-brutal.md` 2026-04-30 — sem mudança estrutural desde):

| Domínio | Q% | Prod-Ready |
|---|---|---|
| Identity/Auth/Multi-tenant | 49% | Parcial |
| Estoque/Catálogo | 49% | Parcial |
| Vendas/Pedidos/Compras/Caixa | 56% | OK (PedidoFornecedor fixado) |
| Subscription/Billing/Pix | 47% | OK (vuln R$0,01 fixada) |
| Analytics/IA/Notifs | 47% → 60% (após PR1–PR7) | OK |
| Admin/Diagnostics | 57% → 75% (após Gestão Cliente P0–P3) | OK |
| Mobile/PWA + MAUI | 48% → 65% (após F0–F4c) | OK |
| Infra/Jobs/Email | 60% | Parcial |

## Convenções ativas (siga)

Veja `conventions.md`. TL;DR:
- Clean Architecture estrita (Domain → Application → Infra → Api)
- Multi-tenant via filtros manuais por `EmpresaId` (sem EF Global Query Filter — bug-prone)
- xmin RowVersion em entidades-chave (Postgres system column, sem migration)
- Use cases injetam interfaces de repos via primary constructor
- Idempotência via `MovimentacaoEstoque.DocumentoReferencia = "{pedidoId}:{itemId}"`
- Status de pedido como string (`"aguardando" | "preparando" | "pronto" | "entregue" | "cancelado"`) com matriz de transições explícita
- Testes: xUnit + NSubstitute + FluentAssertions
- Comentar APENAS o "porquê" não-óbvio
- Outbox pattern para notificações (nunca despachar inline)
- Auditoria E2E: mudanças em entidade chave gravam `XxxxAlteracao`

## Não-faça (mistakes registrados)

Veja `do-not-do.md`. Top 5:
1. **Não estimar custos cloud sem auditar TUDO da subscription** ($474 vs estimativa $50)
2. **Não recomendar `[AllowAnonymous]` em endpoints destrutivos**
3. **Não criar dependência nova sem checar se pacote existe** na camada
4. **Não usar `Math.Ceiling` em qty fracionária** (vira saldo negativo silencioso)
5. **Não fazer migration EF que duplique tabelas já criadas em SQL raw**

## Arquivos desta pasta

- `README.md` — este arquivo (sempre comece aqui)
- `architecture.md` — Clean Arch, projetos, fluxo de dependências
- `domain-glossary.md` — Pedido vs Venda, MovimentacaoEstoque, ItemEstoque, Caixa, Assinatura, Plano, IA, PWA
- `conventions.md` — naming, padrões de código, DI, testes, multi-tenant, xmin
- `current-state.md` — estado por área + infra + features (atualizar conforme avança)
- `tech-debt.md` — P0/P1/P2 + resolvidos como histórico
- `do-not-do.md` — erros já cometidos e por quê
- `recent-evolution.md` — ondas de feature recentes + decisões de arquitetura
- `quick-reference.md` — comandos Bash/dotnet/git mais usados
- `audit-brutal.md` — auditoria sênior pessimista de 2026-04-30 (não regenere)
- `dual-frontend-policy.md` — **POLÍTICA OBRIGATÓRIA**: PWA + MAUI coexistem; merge unidirecional `PWA → MAUI`
- `stability-roadmap.md` — **checklist vivo** do que falta pra estabilizar (deploy, CI, observabilidade, testes integração). Atualizar marcando `[x]` ao resolver.

## Como usar isso pra economizar tokens

**Cenário 1 — começar conversa nova:** lê este README. Pergunta o que o usuário quer. Aí abre 1-2 arquivos específicos.

**Cenário 2 — usuário pediu pedido/estoque:** abre `domain-glossary.md` + `current-state.md` + arquivo do código relevante.

**Cenário 3 — usuário pediu deploy:** abre `current-state.md` (seção Infra) + `render.yaml` + `.github/workflows/deploy-render.yml`.

**Cenário 4 — usuário pediu auditoria:** abre `audit-brutal.md` (não roda novos agentes — já tem dados).

**Cenário 5 — tarefa toca PWA ou MAUI:** abre `dual-frontend-policy.md` SEMPRE.

## Manutenção

- Resolveu P0 do `tech-debt.md`? Move pra "Resolvidos" com data + commit hash.
- Marcou `[x]` em `stability-roadmap.md` ao concluir item.
- Atualizou `current-state.md` quando deploy/auditoria/teste mudou.
- `recent-evolution.md` recebe ondas grandes de feature (não cada commit).
