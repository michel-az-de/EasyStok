# Changelog — EasyStok

Todas as mudancas relevantes vao aqui. Formato baseado em
[Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/), adaptado para
nosso ciclo de PRs squashed (Conventional Commits).

Cada entrada e um PR squashed em `master`. O numero entre `(#NNN)` aponta
para o PR no GitHub. Antes de 2026-05, alguns commits sao diretos sem PR
(periodo pre-protocolo formal — ver
[ADR-0020](adr/0020-tdd-tasks-numeradas-multitarefa.md)).

**Convencao de tipos:** `feat`, `fix`, `chore`, `docs`, `refactor`, `perf`,
`test`, `ci`, `style`, `arch`.

**Releases formais:** ainda nao ha tags. Tag `v1.0.0` planejada em
[ETK-0001](tasks/backlog/ETK-0001-marco-zero-deploy-v1.yaml).

---

## [Unreleased]

### 2026-05 — Maio

**Highlights:**
- **Sistema ETK-NNNN de tasks formais** ([ADR-0020](adr/0020-tdd-tasks-numeradas-multitarefa.md), [#226](https://github.com/michel-az-de/EasyStok/pull/226)): 25 tasks bootstrap, TDD obrigatorio com red/green/refactor, worktree por task, locks atomicos, scripts shell.
- **Decisao Etapa 5: Rotulagem P-02 vence Caixa V2** ([ADR-0021](adr/0021-rotulagem-p02-etapa5-do-roadmap.md), [#227](https://github.com/michel-az-de/EasyStok/pull/227)). Justificativa em [docs/plan/p-02-rotulagem-nutricional.md](plan/p-02-rotulagem-nutricional.md).
- **RLS Postgres como defesa em profundidade do multi-tenant** ([ADR-0010](adr/0010-rls-postgres-defesa-em-profundidade.md), [#129](https://github.com/michel-az-de/EasyStok/pull/129)) + correcao de gap pos-migration em 3 tabelas ([#211](https://github.com/michel-az-de/EasyStok/pull/211)).
- **Modulo Compras E2E (3 fases)**: lista inteligente ([#187](https://github.com/michel-az-de/EasyStok/pull/187)), virar lista em pedido fornecedor ([#189](https://github.com/michel-az-de/EasyStok/pull/189)), recebimento da entrada no estoque ([#190](https://github.com/michel-az-de/EasyStok/pull/190)), UX da lista ([#192](https://github.com/michel-az-de/EasyStok/pull/192)).
- **Modulo Financeiro CAP/CAR completo** (5 ondas, [#88](https://github.com/michel-az-de/EasyStok/pull/88)) + bootstrap Lancamento + LancamentoBaixa ([#127](https://github.com/michel-az-de/EasyStok/pull/127)) + combobox com criacao inline ([#186](https://github.com/michel-az-de/EasyStok/pull/186)).
- **NF-e fundacao**: Domain NFC-e Corte 1 ([#85](https://github.com/michel-az-de/EasyStok/pull/85)), UX configuracao fiscal Admin + listagem NFC-e Web ERP ([#169](https://github.com/michel-az-de/EasyStok/pull/169)), operacao NF visual end-to-end ([#177](https://github.com/michel-az-de/EasyStok/pull/177)).
- **Estoque hardening**: FIFO/FEFO determinismo real ([#221](https://github.com/michel-az-de/EasyStok/pull/221)), IgnoreQueryFilters em FOR UPDATE preserva ORDER BY ([#199](https://github.com/michel-az-de/EasyStok/pull/199), [#209](https://github.com/michel-az-de/EasyStok/pull/209)), GetByIdComLockAsync recebe empresaId ([#212](https://github.com/michel-az-de/EasyStok/pull/212)).
- **Security**: elimina 6 CVEs em dependencias ([#210](https://github.com/michel-az-de/EasyStok/pull/210)), suprime NU1902/NU1903 SharpCompress ([#219](https://github.com/michel-az-de/EasyStok/pull/219), [#223](https://github.com/michel-az-de/EasyStok/pull/223)), Scriban 7.2.0 ([#203](https://github.com/michel-az-de/EasyStok/pull/203)), rate limit auth/* B-015 ([#80](https://github.com/michel-az-de/EasyStok/pull/80)).
- **Deploy Fly.io estabilizado**: auto-deploy API/Web/Admin em push master ([#167](https://github.com/michel-az-de/EasyStok/pull/167)), migrations no deploy via release_command ([#191](https://github.com/michel-az-de/EasyStok/pull/191)).
- **PWA Casa da Baba ondas**: calculadora de producao + receitas ([#135](https://github.com/michel-az-de/EasyStok/pull/135)), 4 ondas pente fino UX (rotas, KDS, filtros, badges, validacoes), Onda 1 Clientes — score/recencia/WhatsApp/Maps ([#137](https://github.com/michel-az-de/EasyStok/pull/137)).
- **Mobile MAUI F0-F4c completo**: setup + estrutura + auth E2E + multi-tenant + produc SQLite + sync engine + popup foto/peso/validade (commits diretos 2026-05-04 a 05-09).
- **Helpdesk E2E**: fluxo cliente + dashboard + CSAT + relatorio ([#82](https://github.com/michel-az-de/EasyStok/pull/82)), FAQ E2E + CanalOrigem ([#101](https://github.com/michel-az-de/EasyStok/pull/101)).
- **Admin redesign UX/UI completo** com biblioteca de componentes ([#171](https://github.com/michel-az-de/EasyStok/pull/171), [#184](https://github.com/michel-az-de/EasyStok/pull/184)), upgrade UX Notificacoes + 16 templates ([#126](https://github.com/michel-az-de/EasyStok/pull/126)).
- **Dashboard v2**: KPIs priorizados, alertas em lista, sidebar agrupada ([#122](https://github.com/michel-az-de/EasyStok/pull/122)).
- **Onboarding wizard E2E** + rate limit + Pix self-service ([#78](https://github.com/michel-az-de/EasyStok/pull/78)).
- **Payment Orchestration P0**: smart routing + PaymentAttempt + audit ([#95](https://github.com/michel-az-de/EasyStok/pull/95)).
- **Seed bootstrap R6 mitigado** ([#87](https://github.com/michel-az-de/EasyStok/pull/87)).
- **Infra estabilidade**: UTC sistemico + sync RLS + lifetime fiscal ([#193](https://github.com/michel-az-de/EasyStok/pull/193), [#194](https://github.com/michel-az-de/EasyStok/pull/194)), Dockerfile API faltava COPY source ([#179](https://github.com/michel-az-de/EasyStok/pull/179)), dockerignore quebrava deploy ([#178](https://github.com/michel-az-de/EasyStok/pull/178)).

#### feat (mes)

- [#227](https://github.com/michel-az-de/EasyStok/pull/227) `docs(ETK-0005)` ADR-0021 Rotulagem P-02 vence Caixa V2 (Etapa 5)
- [#226](https://github.com/michel-az-de/EasyStok/pull/226) `feat(meta)` sistema de tasks ETK-NNNN + ADR-0020 + 25 tasks bootstrap
- [#192](https://github.com/michel-az-de/EasyStok/pull/192) `feat(compras)` UX da lista — marcar instantaneo, busca de produto, formatacao
- [#191](https://github.com/michel-az-de/EasyStok/pull/191) `feat(deploy)` aplica migrations no deploy (release_command Fly)
- [#190](https://github.com/michel-az-de/EasyStok/pull/190) `feat(compras)` recebimento da entrada no estoque + fix DI (Fase 3)
- [#189](https://github.com/michel-az-de/EasyStok/pull/189) `feat(compras)` virar lista em pedido de fornecedor (Fase 2)
- [#187](https://github.com/michel-az-de/EasyStok/pull/187) `feat(web)` lista de compras inteligente (Fase 1)
- [#186](https://github.com/michel-az-de/EasyStok/pull/186) `feat(financeiro)` combobox com criacao inline para Categoria e Centro de Custo
- [#184](https://github.com/michel-az-de/EasyStok/pull/184) `feat(admin)` redesign UX/UI completo com biblioteca de componentes
- [#182](https://github.com/michel-az-de/EasyStok/pull/182) `feat(ds)` filter-tabs e btn-outline-dark nas views principais
- [#181](https://github.com/michel-az-de/EasyStok/pull/181) `feat(ds)` consistency layer dark mode — 12 itens de padronizacao
- [#177](https://github.com/michel-az-de/EasyStok/pull/177) `feat(fiscal)` operacao NF visual end-to-end no Web admin
- [#176](https://github.com/michel-az-de/EasyStok/pull/176) `feat(web)` pente fino de UX copy no DS showcase
- [#171](https://github.com/michel-az-de/EasyStok/pull/171) `feat(admin)` redesign UX/UI completo + fix build BCrypt + security fixes
- [#169](https://github.com/michel-az-de/EasyStok/pull/169) `feat(fiscal)` UX configuracao fiscal (Admin) + listagem NFC-e (Web ERP)
- [#165](https://github.com/michel-az-de/EasyStok/pull/165) `feat(agendamento)` estabilizar F5 — templates, UI Web, testes
- [#158](https://github.com/michel-az-de/EasyStok/pull/158) `feat(web)` UI ficha tecnica nutricional + fix bug AtributosJson zerado
- [#156](https://github.com/michel-az-de/EasyStok/pull/156) `feat(domain)` VO Gtin com checksum mod10 e suporte a codigo interno
- [#142](https://github.com/michel-az-de/EasyStok/pull/142) `feat(lotes)` TipoEmbalagem em Produto + AtualizarPesoLoteItem + inline edit peso
- [#137](https://github.com/michel-az-de/EasyStok/pull/137) `feat(pwa)` Onda 1 Clientes — score, recencia, WhatsApp, Maps MVP
- [#135](https://github.com/michel-az-de/EasyStok/pull/135) `feat` calculadora de producao + receitas (Casa da Baba)
- [#129](https://github.com/michel-az-de/EasyStok/pull/129) `feat(security)` RLS Postgres como defesa em profundidade do multi-tenant
- [#127](https://github.com/michel-az-de/EasyStok/pull/127) `feat(financeiro)` bootstrap Lancamento + LancamentoBaixa
- [#126](https://github.com/michel-az-de/EasyStok/pull/126) `feat(admin)` upgrade UX modulo Notificacoes + seed 16 templates faltantes
- [#123](https://github.com/michel-az-de/EasyStok/pull/123) `feat(mobile)` tooling pra primeiro pareamento da Casa da Baba
- [#122](https://github.com/michel-az-de/EasyStok/pull/122) `feat(web)` dashboard v2 — KPIs priorizados, alertas em lista, sidebar agrupada
- [#110](https://github.com/michel-az-de/EasyStok/pull/110) `feat(web)` pente fino UX no portal — Clientes revamp + consistencia cross-modulos
- [#103](https://github.com/michel-az-de/EasyStok/pull/103) `EasyStock.Web` refatoracao de design system (fases 1 a 6)
- [#101](https://github.com/michel-az-de/EasyStok/pull/101) `feat(helpdesk)` FAQ E2E completo + CanalOrigem
- [#95](https://github.com/michel-az-de/EasyStok/pull/95) `feat(payment-orchestration)` Onda P0 — smart routing + PaymentAttempt + audit
- [#88](https://github.com/michel-az-de/EasyStok/pull/88) `feat(financeiro)` modulo CAP/CAR completo (5 ondas)
- [#85](https://github.com/michel-az-de/EasyStok/pull/85) `feat(fiscal)` fundacao Domain NFC-e Corte 1 (M-01 pavimentacao)
- [#84](https://github.com/michel-az-de/EasyStok/pull/84) `feat(kds)` tela alpha de Kitchen Display System pra cozinha
- [#82](https://github.com/michel-az-de/EasyStok/pull/82) `feat(helpdesk)` fluxo cliente E2E + dashboard + CSAT + relatorio
- [#78](https://github.com/michel-az-de/EasyStok/pull/78) `feat(onboarding)` wizard E2E + rate limit + Pix self-service
- [#72](https://github.com/michel-az-de/EasyStok/pull/72) `feat(notifications)` /health/dispatcher e heartbeat dos loops para mitigar cascata API+Worker

#### fix (mes)

- [#225](https://github.com/michel-az-de/EasyStok/pull/225) `fix(infra/sqlite)` paridade DI com Postgre (22 repos)
- [#224](https://github.com/michel-az-de/EasyStok/pull/224) `fix(web/admin)` null guards em 4 CS8602
- [#223](https://github.com/michel-az-de/EasyStok/pull/223) `fix(security)` NU1902 SharpCompress
- [#222](https://github.com/michel-az-de/EasyStok/pull/222) `fix(infra/mongodb)` VO serializers null-safe (bug NRE em prod)
- [#221](https://github.com/michel-az-de/EasyStok/pull/221) `fix(estoque)` FIFO/FEFO determinismo real
- [#220](https://github.com/michel-az-de/EasyStok/pull/220) `fix(mobile)` 3 bindings inertes XC0045
- [#219](https://github.com/michel-az-de/EasyStok/pull/219) `fix(security)` suprime NU1903 SharpCompress (sem patch upstream)
- [#218](https://github.com/michel-az-de/EasyStok/pull/218) `fix(api/mobile)` null guard dto.Items em SyncMutationDispatcher
- [#217](https://github.com/michel-az-de/EasyStok/pull/217) `fix(test)` ItemEstoque VO test usa UTC DateTime kind
- [#216](https://github.com/michel-az-de/EasyStok/pull/216) `fix(worker)` IFileStorage realocado p/ Infra.Async + resolve DI Worker
- [#212](https://github.com/michel-az-de/EasyStok/pull/212) `fix(estoque/movimentacao)` GetByIdComLockAsync recebe empresaId + IgnoreQueryFilters
- [#211](https://github.com/michel-az-de/EasyStok/pull/211) `fix(security)` RLS policy para 3 tabelas pos-migration (gap do #129)
- [#210](https://github.com/michel-az-de/EasyStok/pull/210) `fix(security)` elimina 6 CVEs em dependencias (HIGH x5, MODERATE x1)
- [#209](https://github.com/michel-az-de/EasyStok/pull/209) `fix(estoque)` IgnoreQueryFilters em FromSqlRaw FOR UPDATE preserva ORDER BY
- [#207](https://github.com/michel-az-de/EasyStok/pull/207) `fix(api/seed)` seed de notificacoes globais agora idempotente
- [#206](https://github.com/michel-az-de/EasyStok/pull/206) `fix(mobile)` habilita compiled bindings com x:DataType
- [#205](https://github.com/michel-az-de/EasyStok/pull/205) `fix(infra/async)` remover Microsoft.AspNetCore.Http.Abstractions 2.3.0 deprecated
- [#204](https://github.com/michel-az-de/EasyStok/pull/204) `fix(infra/async)` elimina CS9113 + CS8602
- [#203](https://github.com/michel-az-de/EasyStok/pull/203) `fix(security)` Scriban 7.1.0 -> 7.2.0 (GHSA-24c8-4792-22hx)
- [#202](https://github.com/michel-az-de/EasyStok/pull/202) `fix(build)` alinhar Microsoft.EntityFrameworkCore.Relational em 9.0.4
- [#199](https://github.com/michel-az-de/EasyStok/pull/199) `fix(estoque/fiscal/cobranca)` IgnoreQueryFilters em FromSqlRaw FOR UPDATE
- [#194](https://github.com/michel-az-de/EasyStok/pull/194) `fix(worker)` IGatewayFiscalFactory Scoped (lifetime mismatch)
- [#193](https://github.com/michel-az-de/EasyStok/pull/193) `fix(estabilidade)` UTC sistemico + sync RLS + lifetime fiscal + testes web→API
- [#185](https://github.com/michel-az-de/EasyStok/pull/185) `fix(migrations)` PascalCase em AddPedidoFornecedorItemTable
- [#183](https://github.com/michel-az-de/EasyStok/pull/183) `fix(web)` pacote 1 — 8 bugs de exibicao e UX
- [#182](https://github.com/michel-az-de/EasyStok/pull/182) `fix(pwa)+refactor(arch)+style(web)` bugs PWA, desacoplamento EF, tokens CSS
- [#180](https://github.com/michel-az-de/EasyStok/pull/180) `fix(sync)` JWT empresaId apos refresh + logs estruturados auto-link
- [#179](https://github.com/michel-az-de/EasyStok/pull/179) `fix(infra)` Dockerfile API faltava COPY source de Notifications/Integrations/Contracts
- [#178](https://github.com/michel-az-de/EasyStok/pull/178) `fix(infra)` dockerignore excluia MongoDb/Sqlite/scripts quebrando deploy API
- [#175](https://github.com/michel-az-de/EasyStok/pull/175) `fix(estoque)` deep-link /estoque?status=critico ativa pill e filtra
- [#174](https://github.com/michel-az-de/EasyStok/pull/174) `fix(diagnostico)` endpoints auth-protegidos nao marcam verde
- [#173](https://github.com/michel-az-de/EasyStok/pull/173) `fix(worker)` SlaMonitor usa UseRowLevelSecurityBypass() conforme ADR-0010
- [#170](https://github.com/michel-az-de/EasyStok/pull/170) `fix(pwa)` pente fino onda A — 10 bugs HTML5/CSS/labels
- [#168](https://github.com/michel-az-de/EasyStok/pull/168) `fix(web)` corrige build dashboard v2 + restaura dark mode funcional
- [#166](https://github.com/michel-az-de/EasyStok/pull/166) `fix(pwa)` onda 4 — 11 bugs criticos + CRM + LGPD + utils minimos
- [#164](https://github.com/michel-az-de/EasyStok/pull/164) `fix(web)` onda 3 pos-auditoria - 10 bugs comportamentais
- [#163](https://github.com/michel-az-de/EasyStok/pull/163) `fix(web)` onda 2 pos-auditoria - filtro Critico, datas cliente, badge Pedidos
- [#162](https://github.com/michel-az-de/EasyStok/pull/162) `fix(web)` onda 1 pos-auditoria - rotas /lotes /dispositivos /analises + KDS abandonado
- [#160](https://github.com/michel-az-de/EasyStok/pull/160) `fix(web)` viewport guard + typo EasyStok no title de Error
- [#159](https://github.com/michel-az-de/EasyStok/pull/159) `fix(web)` pente-fino dashboard onda 2 — legendas, vocabulario, timezone
- [#154](https://github.com/michel-az-de/EasyStok/pull/154) `fix(web)` topbar a11y badge notif + nowrap nos botoes header
- [#153](https://github.com/michel-az-de/EasyStok/pull/153) `fix(web)` pente-fino p0 dashboard + sidebar + diagnostico
- [#152](https://github.com/michel-az-de/EasyStok/pull/152) `fix(web)` double-escape HTML em ProdutoNome/VariacaoNome da Reposicao
- [#151](https://github.com/michel-az-de/EasyStok/pull/151) `fix(web)` mobile-nav vazando + acentos PT-BR + ajustes layout landing
- [#149](https://github.com/michel-az-de/EasyStok/pull/149) `fix(web)` remove rota / duplicada do DashboardController
- [#148](https://github.com/michel-az-de/EasyStok/pull/148) `fix(tests)` atualiza assercoes desatualizadas em ItemEstoque e CriarLote
- [#145](https://github.com/michel-az-de/EasyStok/pull/145) `fix(mobile-sync)` SyncController + EntityDtos para TipoEmbalagem
- [#143](https://github.com/michel-az-de/EasyStok/pull/143) `fix(etiqueta)` render PayloadHelpers + JS helpers
- [#140](https://github.com/michel-az-de/EasyStok/pull/140) `fix` Onda D — UX/a11y (drawer Esc, contraste sidebar, label cliente)
- [#139](https://github.com/michel-az-de/EasyStok/pull/139) `fix` Onda C — validacoes e regras de negocio
- [#138](https://github.com/michel-az-de/EasyStok/pull/138) `fix` Onda B — metricas confiaveis
- [#136](https://github.com/michel-az-de/EasyStok/pull/136) `fix` Onda A — sangra dinheiro
- [#134](https://github.com/michel-az-de/EasyStok/pull/134) `fix(web+api)` 5 bugs — consentimentos 404, Console 404, Swagger producao
- [#125](https://github.com/michel-az-de/EasyStok/pull/125) `fix(web)` humaniza toasts de erro da API (G-04)
- [#121](https://github.com/michel-az-de/EasyStok/pull/121) `fix(web)` destrava dropdowns do topbar + pente fino UX/a11y
- [#116](https://github.com/michel-az-de/EasyStok/pull/116) `fix(security)` quebra literal da dev key vazada (gitleaks)
- [#112](https://github.com/michel-az-de/EasyStok/pull/112) `fix(ci)` destrava gitleaks (permission) + CodeQL (MAUI workload)
- [#111](https://github.com/michel-az-de/EasyStok/pull/111) `fix(web)` nav progress bar travada em forms AJAX e bfcache
- [#109](https://github.com/michel-az-de/EasyStok/pull/109) `fix(auth)` preserva returnUrl em POST + limpeza Login.cshtml
- [#107](https://github.com/michel-az-de/EasyStok/pull/107) `chore(pedidos)` remove guarda morta + CSS nao usado no combobox
- [#96](https://github.com/michel-az-de/EasyStok/pull/96) `fix(helpdesk)` normaliza Guid.Empty em CriadoPorId/AutorId (F14 webhook anonimo)
- [#91](https://github.com/michel-az-de/EasyStok/pull/91) `fix(faturas)` corrige multi-tenant, duplicacao na reconciliacao e vazamento PDF cross-tenant
- [#89](https://github.com/michel-az-de/EasyStok/pull/89) `fix(notifications)` pente fino Scriban — sandbox real, timeout efetivo, cache, auto-escape HTML
- [#87](https://github.com/michel-az-de/EasyStok/pull/87) `fix(seed)` R6 Seed fragil mitigado em profundidade (Onda 1)
- [#80](https://github.com/michel-az-de/EasyStok/pull/80) `fix(auth)` fecha B-015 — rate limit em /auth/* com teste de regressao
- [#79](https://github.com/michel-az-de/EasyStok/pull/79) `fix(billing)` MercadoPago webhook valida janela de replay (paridade Stripe)
- [#77](https://github.com/michel-az-de/EasyStok/pull/77) `fix(web)` 4 correcoes UX em diagnostico/loja/produtos
- [#71](https://github.com/michel-az-de/EasyStok/pull/71) `fix(billing)` expoe forcarRefresh no endpoint /api/admin/faturas/metricas

#### perf

- [#75](https://github.com/michel-az-de/EasyStok/pull/75) `perf(billing)` cache de status de assinatura no SubscriptionGateMiddleware (TTL 60s)

#### test / arch / refactor

- [#198](https://github.com/michel-az-de/EasyStok/pull/198) `test(integration)` EstoqueWorkflows/Concurrency 12/12 verde
- [#197](https://github.com/michel-az-de/EasyStok/pull/197) `test(rls)` RowLevelSecurityTests como role NOSUPERUSER
- [#214](https://github.com/michel-az-de/EasyStok/pull/214) `test(api/integration)` cachear seed demo check + handoff otimizacao 12 skips lentos
- [#181](https://github.com/michel-az-de/EasyStok/pull/181) `refactor(web)` helpers BaseController + IntegrityScore para ViewModel + UX
- [#97](https://github.com/michel-az-de/EasyStok/pull/97) `arch(audit)` IPasswordHasher, ICacheService, FakeUnitOfWork + 4 ArchTests
- [#83](https://github.com/michel-az-de/EasyStok/pull/83) `test+ci` cobertura agora e medida com gate por modulo (Domain 73 / App 47 / Api 9)
- [#161](https://github.com/michel-az-de/EasyStok/pull/161) `style(web)` pente fino onda 1 - tokens, soft alerts, validity semaforo

#### chore / ci / docs

- [#208](https://github.com/michel-az-de/EasyStok/pull/208) `docs(flaky-tests)` remove ENTRADA STALE de Exceptions_De_Domain (arch 16/16 verde)
- [#213](https://github.com/michel-az-de/EasyStok/pull/213) `docs(sessao)` handoff do diagnostico RLS prod (#200)
- [#200](https://github.com/michel-az-de/EasyStok/pull/200) `docs(rls)` diagnostico role prod easystok_user - RLS ativo + gap pos-migration
- [#196](https://github.com/michel-az-de/EasyStok/pull/196) `docs(sessao)` handoff da higiene docs/protocolo
- [#195](https://github.com/michel-az-de/EasyStok/pull/195) `docs(protocolo)` corrige path §0 e atualiza estado conhecido
- [#188](https://github.com/michel-az-de/EasyStok/pull/188) `docs(sessao)` handoff review+merge+deploy PRs #185, #186, #187
- [#167](https://github.com/michel-az-de/EasyStok/pull/167) `ci(fly)` auto-deploy de API/Web/Admin em push para master
- [#157](https://github.com/michel-az-de/EasyStok/pull/157) `docs(sessoes)` handoff 2026-05-16-1728 codigo de barras pausa pos-PR1
- [#155](https://github.com/michel-az-de/EasyStok/pull/155) `docs(sessoes)` handoff 2026-05-16-1554 pente-fino 29 itens
- [#150](https://github.com/michel-az-de/EasyStok/pull/150) `docs(sessoes)` handoff 2026-05-16-1700 deploy fly 3 apps
- [#147](https://github.com/michel-az-de/EasyStok/pull/147) `docs(policy)` CLAUDE.md v2.0 - protocolo reforcado
- [#146](https://github.com/michel-az-de/EasyStok/pull/146) `chore(cleanup)` higiene pos-incidente Fase 3
- [#144](https://github.com/michel-az-de/EasyStok/pull/144) `polish(ui)` residual UI tweaks + Dockerfile fiscal
- [#131](https://github.com/michel-az-de/EasyStok/pull/131) `ci` workflow manual de build APK Casa da Baba
- [#130](https://github.com/michel-az-de/EasyStok/pull/130) `chore(mobile)` pente fino - rename pedidoStatusEntries + dead comment
- [#124](https://github.com/michel-az-de/EasyStok/pull/124) `chore(claude)` regra branch isolada por demanda + PR para revisor
- [#102](https://github.com/michel-az-de/EasyStok/pull/102) `chore(cleanup)` usings BCL + file-scoped + Json fully-qualified em Ports/Adapters
- [#100](https://github.com/michel-az-de/EasyStok/pull/100) `chore(cleanup)` cref XML invalido + comentario stale Mongo (sweep 2026-05-08)
- [#98](https://github.com/michel-az-de/EasyStok/pull/98) `chore(cleanup)` corrige doc HTTP Pix + remove overload morta + log rodada
- [#94](https://github.com/michel-az-de/EasyStok/pull/94) `chore(cleanup)` padroniza FQN e record sealed em billing
- [#93](https://github.com/michel-az-de/EasyStok/pull/93) `docs(cleanup-log)` registra segunda passagem billing F12 + Mongo
- [#92](https://github.com/michel-az-de/EasyStok/pull/92) `docs(knowledge)` Snapshot 2026-05-07 (Onda Billing F1-F14)
- [#81](https://github.com/michel-az-de/EasyStok/pull/81) `chore(cleanup)` remove using desnecessario e corrige dispose JsonDocument
- [#76](https://github.com/michel-az-de/EasyStok/pull/76) `ci` workflow CI obrigatorio (build + dotnet test) em todo PR
- [#73](https://github.com/michel-az-de/EasyStok/pull/73) `chore(cleanup)` limpeza Admin/Program, Faturas, Tickets e DiagnosticoController
- [#69](https://github.com/michel-az-de/EasyStok/pull/69) `chore(infra)` politica PR + Dockerfiles otimizados pra economia Render

### 2026-04 — Abril

**Highlights:**
- **Auth completo**: registro, login JWT 8h + refresh 30d, logout, reset senha, email confirmation, biometria mobile, revogacao tokens (commits pre-PR).
- **Catalogo + Estoque** com VOs (Sku, Validade, Dinheiro, Cnpj, Telefone, EmailAddress, PasswordPolicy), FEFO real na saida, idempotencia em POSTs.
- **Pedido state machine** + idempotencia `{pedidoId}:{itemId}` em movimentacao.
- **`PedidoFornecedorItem` entity criada** — Compras agora persiste itens (P0 antigo resolvido).
- **Webhook Pix valida valor pago vs cobranca** (vuln R$0,01 fechada em commit `37fb7d9`).
- **DiagnosticoController `[Authorize(Policy="Admin")]`** (21 endpoints AllowAnonymous resolvido em `c5d2ad6`).
- **MongoDB descartado como provedor transacional** ([ADR-0001](adr/0001-mongo-discarded.md), commit `820843c`).
- **Notifications PR1-PR7**: Outbox + adapters Email/SMS/WhatsApp/InApp + Worker + painel Admin + LGPD + metricas OTel.
- **Mobile MAUI F0-F4c** (commits `ac49b498`, `970393e1`, `30ee28f4`, etc.): setup → estrutura → auth E2E → multi-tenant → producao SQLite → mutations otimistas → SyncEngine → popup captura.
- **Admin design system migrado** para identidade visual da marca ([#admin commits 2026-04-30](https://github.com/michel-az-de/EasyStok/commit/02bfbe11)).
- **Mobile Onda 6**: multi-loja UI no app + conflict modal rico ([commit 0455f46c](https://github.com/michel-az-de/EasyStok/commit/0455f46c)).

#### feat / fix (Abril — PRs e commits diretos)

- [#137](https://github.com/michel-az-de/EasyStok/pull/137) `feat(pwa)` Onda 1 Clientes — score, recencia, WhatsApp, Maps MVP
- `a5c8b85d` `style(swagger)` aplica design system dark sci-fi + botao Console (2026-05-07)
- `6502b009` `feat(api)` tema Swagger UI navy + laranja + botoes Site/Repo (2026-05-07)
- `d66f81ff` `feat(billing)` completar reativacao do modulo Faturas (F1+F2) (2026-05-07)
- `30ee28f4` `feat(mobile)` v1.2 - hero design PWA + AppIdentity + refator nominal PT-BR (2026-05-05)
- `01231caa` `fix(web)` review visual e UX — correcoes criticas e maiores (2026-05-04)
- `970393e1` `feat(mobile)` refator design EasyStok + modo demo offline (2026-05-04)
- `ac49b498` `feat(mobile-maui)` F0 - setup do projeto MAUI Android (2026-05-04)
- `d4d6f1f7` `feat(mobile v6)` blindagem defensiva — health-check + diagnostico + auto-share (2026-05-03)
- `f3c2011c` `fix(audit)` corrige bugs criticos achados em auditoria pos-refactor (2026-05-02)
- `ddb4f65d` `refactor(ui-pages)` aplica brand DS nas paginas auth + limpa hex indigo (Fase 3) (2026-05-02)
- `e4de5be0` `refactor(ui-shell)` migra paleta indigo→navy + orange accent (Fase 2) (2026-05-02)
- `45dcd8dd` `fix(admin)` segunda revisao — 4 defeitos corrigidos (2026-04-30)
- `2d16c28c` `fix(admin)` auditoria e pente fino do design system (2026-04-30)
- `02bfbe11` `feat(admin)` migrar design system para identidade visual da marca (2026-04-30)
- `0455f46c` `feat(mobile)` Onda 6 — multi-loja UI no app + conflict modal rico (2026-04-29)
- `a67b9bad` `fix(mobile)` undo z-index, undo no fechamento de caixa, prints + tipografia (2026-04-29)
- `c961c75b` `feat(mobile)` UX onda 2.1 — tela Pedidos em 2 abas (Novo / Andamento) (2026-04-28)
- `114adcca` `feat(mobile)` conferencia hibrida — pedidos + producao via QR/barcode (2026-04-28)
- `ea60d581` `fix(mobile)` icones do nav aparecem em todos os WebViews Android (2026-04-23)

### 2026-04 e anteriores

Periodo pre-protocolo formal de PRs (`CLAUDE.md` v2.0 introduzido em [#147](https://github.com/michel-az-de/EasyStok/pull/147), 2026-05-16). Commits sao diretos em master sem squash. Ver `git log master --before=2026-05-01` para detalhes.

**Marcos do periodo (sem PR formal):**
- MVP inicial do EasyStok (Q1 2026): solucao 17 projetos, Clean Architecture, multi-tenant base.
- Auditoria brutal 2026-04-30: snapshot completo em [`.knowledge/audit-brutal.md`](../.knowledge/audit-brutal.md).
- Render/Azure descomissionamento parcial 2026-05-11 (Azure removido, Render mantido por billing CI).

---

## Eventos de infra e decisao (sem PR)

| Data | Evento | Referencia |
|---|---|---|
| 2026-05-24 | Sistema ETK-NNNN ativo | [ADR-0020](adr/0020-tdd-tasks-numeradas-multitarefa.md) |
| 2026-05-24 | Decisao Etapa 5: Rotulagem P-02 vence Caixa V2 | [ADR-0021](adr/0021-rotulagem-p02-etapa5-do-roadmap.md) |
| 2026-05-22 | Hardening dos protocolos (CLAUDE.md path corrigido) | PR [#195](https://github.com/michel-az-de/EasyStok/pull/195) |
| 2026-05-19 | Incidente: deploy fly + migrations | [docs/dev/incidentes/](dev/incidentes/) |
| 2026-05-17 | NF-e prefixo curto (Nfe* vs NotaFiscal*) | [ADR-0018](adr/0018-nomenclatura-nfe-prefixo-curto.md) |
| 2026-05-16 | CLAUDE.md v2.0 protocolo reforcado | PR [#147](https://github.com/michel-az-de/EasyStok/pull/147) |
| 2026-05-11 | CI GitHub Actions bloqueado por billing | [`.knowledge/current-state.md`](../.knowledge/current-state.md) |
| 2026-05-11 | Azure App Service descomissionado | [`.knowledge/current-state.md`](../.knowledge/current-state.md) |
| 2026-05-07 | Fly.io como producao unica | [`.knowledge/current-state.md`](../.knowledge/current-state.md) |
| 2026-05-01 | MongoDB descartado como provedor transacional | [ADR-0001](adr/0001-mongo-discarded.md) |

---

## Como contribuir com este changelog

Toda mudanca em `master` deve seguir [Conventional Commits](https://www.conventionalcommits.org/pt-br/):
`tipo(escopo): descricao imperativa`. Tipos validos: `feat`, `fix`, `chore`,
`docs`, `refactor`, `perf`, `test`, `ci`, `style`, `arch`.

PRs squashed devem ter mensagem final aderente ao formato — `gh pr merge`
usa o titulo do PR como mensagem do squash commit.

**Atualizacao deste arquivo:** apos cada mes fechado, novo bloco `### YYYY-MM`
com Highlights (3-5 marcos) + listagem agrupada por tipo. Tarefa formalizada
em [ETK-0004](tasks/in-progress/ETK-0004-roadmap-publicado.yaml).
