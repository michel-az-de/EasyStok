# EasyStok — Roadmap

> Visao publica do que vem pela frente. Para detalhes tecnicos internos, veja
> [`.knowledge/`](.knowledge/README.md) e [`CLAUDE.md`](CLAUDE.md).

**Versao:** 1.0  
**Data:** 2026-05-24  
**Mantenedor:** [@michel-az-de](https://github.com/michel-az-de) (Felipe Azevedo)

## O que e o EasyStok

SaaS multi-tenant de gestao de estoque e foodservice em **.NET 9 + PostgreSQL**,
com PWA mobile (Casa da Baba em producao real) e MAUI Android. Posicionado como
ERP para microempresas e foodservice que ainda nao precisam emitir NF-e e que
nao sao bem servidas pelos ERPs incumbentes (Bling, Tiny, Omie).

## Estado atual (snapshot 2026-05-24)

- **Repo:** 1500+ commits em `master`, 24 projetos .NET, build verde, 1544 testes
  logicos passando.
- **Deploy:** 3 apps no Fly.io (`easystok-api`, `easystok-web`, `easystok-admin`)
  + `easystok-worker`. Postgres gerenciado.
- **Cliente em producao:** Casa da Baba (foodservice — massas artesanais) com
  caixa NFC-e, etiquetagem termica e fluxo de venda real diario.
- **Feature parity vs concorrentes** (auditoria 2026-04-30): ~30-35% — ver
  [`.knowledge/audit-brutal.md`](.knowledge/audit-brutal.md).
- **Cobertura tecnica por dominio:** ver [`docs/FEATURES.md`](docs/FEATURES.md).
- **Decisoes arquiteturais:** 21 ADRs em [`docs/adr/`](docs/adr/).
- **Historico de mudancas:** [`docs/CHANGELOG.md`](docs/CHANGELOG.md).

## Como o trabalho e organizado

Toda mudanca substantiva vira uma **task formal numerada ETK-NNNN** (yaml em
[`docs/tasks/`](docs/tasks/)), com:

- TDD obrigatorio (red -> green -> refactor) para tasks que tocam dominio
- Worktree dedicado por task (`.claude/worktrees/wt-etk-NNNN/`)
- Branch `feat/etk-NNNN-<slug>` + PR + squash + delete branch
- Quality gates: `dotnet build` verde, arch tests passando, format limpo
- Handoff escrito em [`docs/dev/sessoes/`](docs/dev/sessoes/)

Decisao formal em [ADR-0020](docs/adr/0020-tdd-tasks-numeradas-multitarefa.md).
Trabalho informal pequeno (typo, doc menor, < 1h e < 5 arquivos) ainda vai
direto em `master`.

## Proximas entregas

Ordem refletida no [CLAUDE.md secao 6](CLAUDE.md) (protocolo operacional
interno). Estimativas sao otimistas (sem buffer); cada item vira uma ou mais
tasks ETK no [`backlog`](docs/tasks/backlog/).

### Etapa 1 — Marco zero: deploy + tag v1.0 [ETK-0001, P0]
- Promover o build atual para tag `v1.0.0` (primeira release formal).
- Validar deploy fly em prod sem regressao apos tag.
- Esforco estimado: 4h.
- Por que agora: nao existe ainda nenhuma tag/release no repo; sem marco
  inicial, o changelog cresce sem ancora.

### Etapa 2 — Defesas estruturais [ETK-0002, P0]
- Branch protection no `master` (exige PR + status verde).
- Husky pre-commit local (build + arch tests rapidos).
- CI no GitHub Actions destravando billing (atualmente bloqueado desde 2026-05-11).
- Esforco estimado: 6h.
- Tasks correlatas: [ETK-0019 (Husky)](docs/tasks/backlog/ETK-0019-husky-pre-commit-hook.yaml),
  [ETK-0020 (CI)](docs/tasks/backlog/ETK-0020-ci-github-actions-billing.yaml).

### Etapa 3 — Triagem de PRs abertas [ETK-0003, P1]
- Hoje 0 PRs abertas no GitHub (triagem concluida em 2026-05-22) — vigilancia
  continua.
- Esforco estimado: 3h por triagem.

### Etapa 4 — ROADMAP publico [ETK-0004, P1]
- **Em execucao** (este arquivo materializa a entrega).
- Esforco estimado: 2h. Inclui tambem [`docs/CHANGELOG.md`](docs/CHANGELOG.md)
  e [`docs/FEATURES.md`](docs/FEATURES.md) como entrega combinada.

### Etapa 5 — Modulo novo: Rotulagem Nutricional P-02 [ETK-0016, ETK-0017, P1]
**Decisao formalizada em [ADR-0021](docs/adr/0021-rotulagem-p02-etapa5-do-roadmap.md)
(2026-05-24):** Rotulagem P-02 vence Caixa Conciliado V2 como proxima frente.
Justificativa: dor regulatoria ativa (multa Anvisa R$6k-1,5M/unidade), diferencial
competitivo defensavel (IA extrai ficha + Anvisa compliance), bloqueadores
externos contornaveis em paralelo.

Tasks atuais:
- [ETK-0016 — Entity RotuloNutricional](docs/tasks/backlog/ETK-0016-entity-rotulo-nutricional.yaml)
- [ETK-0017 — UseCase GerarRotuloNutricional](docs/tasks/backlog/ETK-0017-usecase-gerar-rotulo.yaml)
- Plano completo: [`docs/plan/p-02-rotulagem-nutricional.md`](docs/plan/p-02-rotulagem-nutricional.md)
- ADRs relacionadas:
  [ADR-0011 (nomenclatura PT-BR)](docs/adr/0011-nomenclatura-pt-br-rotulagem.md),
  [ADR-0012 (backup rotulos storage)](docs/adr/0012-backup-rotulos-storage.md),
  [ADR-0017 (comprovante aprovacao interna RT)](docs/adr/0017-comprovante-aprovacao-interna-rt.md).

### Etapa 6+ — Caixa Conciliado V2 (diferido)
Plano completo continua valido em [`docs/plan/00-reconhecimento.md`](docs/plan/00-reconhecimento.md)
(8 documentos). Volta ao topo do roadmap apos Rotulagem P-02 atingir M3
(go-live em producao). Tasks reservadas:
[ETK-0006](docs/tasks/backlog/ETK-0006-entity-sessao-caixa.yaml) ..
[ETK-0012](docs/tasks/backlog/ETK-0012-migration-add-caixa-module.yaml).

## Estabilizacao continua (paralela ao roadmap)

Itens de [`stability-roadmap`](.knowledge/stability-roadmap.md) com pri P0/P1
abertos hoje. Sao endereçados conforme aparece janela; nao bloqueiam Etapa 5.

| ID | Item | Prioridade | Status |
|---|---|---|---|
| [ETK-0018](docs/tasks/backlog/ETK-0018-fix-sqlite-dev-fallback.yaml) | SQLite dev-fallback completo (paridade DI Postgre) | P1 | Backlog |
| [ETK-0021](docs/tasks/backlog/ETK-0021-api-mongo-integration-tests-triagem.yaml) | Integration tests Mongo (triagem) | P1 | Backlog |
| [ETK-0022](docs/tasks/backlog/ETK-0022-flaky-tests-catalog-cleanup.yaml) | Flaky tests catalog cleanup | P2 | Backlog |
| [ETK-0023](docs/tasks/backlog/ETK-0023-architecture-tests-caixa.yaml) | Arch tests modulo Caixa | P2 | Backlog |
| [ETK-0024](docs/tasks/backlog/ETK-0024-openapi-swagger-completo.yaml) | OpenAPI/Swagger completo | P2 | Backlog |
| [ETK-0025](docs/tasks/backlog/ETK-0025-telemetria-otel-fly.yaml) | Telemetria OTel + dashboards Fly | P2 | Backlog |
| — | Alertas Cloud Monitoring (5xx, p95, Pix pendente) | P0 | Nao iniciado |
| — | Rate limiting `/api/webhooks/pix` | P0 | Nao iniciado |
| — | Sentry/error tracking | P1 | Nao iniciado |
| — | Multi-tenant leak test automatizado | P1 | Nao iniciado |

## Marcos de longo prazo (sem data ainda)

Sao prioridades reconhecidas mas nao agendadas para o trimestre. Entram em
ROADMAP formal quando viram task ETK com data.

- **NF-e/NFC-e emissao real** (via Focus NFe ou eNotas) — entidades stub ja
  desenhadas em [ETK-0013](docs/tasks/backlog/ETK-0013-nfe-event-outbox-entity.yaml),
  [ETK-0014](docs/tasks/backlog/ETK-0014-usecase-emitir-nfe-modelo-55.yaml),
  [ETK-0015](docs/tasks/backlog/ETK-0015-background-emitir-nfe.yaml). Decisao
  formal de nomenclatura em [ADR-0018](docs/adr/0018-nomenclatura-nfe-prefixo-curto.md).
- **Marketplaces** (Mercado Livre, Shopee, Magalu) — zero hoje. Demanda
  validada com clientes pequenos foodservice.
- **Variantes/grades de produto** (cor/tamanho) — estrutura existe mas UI
  nao exposta.
- **Multi-empresa por usuario** — parcial, falta seletor global.

## Como acompanhar

- **Status real-time:** `dashboard.html` (local; servidor casa-da-baba em
  `localhost:4321`).
- **Tasks em andamento:** [`docs/tasks/in-progress/`](docs/tasks/in-progress/).
- **Backlog atual:** [`docs/tasks/_index.yaml`](docs/tasks/_index.yaml)
  (regenerado por `scripts/tasks/regen-index.sh`).
- **Historico de releases:** [`docs/CHANGELOG.md`](docs/CHANGELOG.md).
- **Decisoes:** [`docs/adr/`](docs/adr/) (21 ADRs).
- **Handoffs de sessao:** [`docs/dev/sessoes/`](docs/dev/sessoes/).
- **Incidentes:** [`docs/dev/incidentes/`](docs/dev/incidentes/).

## Mudancas neste roadmap

Atualizar este arquivo quando:
- Uma etapa for concluida (marcar com data + commit hash).
- Uma nova etapa entrar no top-5.
- Uma ADR mudar prioridade ou ordem.

Nao atualizar para refletir trabalho diario — isso vai no `docs/CHANGELOG.md`
via Conventional Commits.
