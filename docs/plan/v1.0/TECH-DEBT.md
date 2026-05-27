# TECH-DEBT.md — Débito técnico documentado para v1.1+

Itens **conhecidos** que não bloqueiam v1.0 mas precisam ser tratados depois. Cada item declara: o que é, por que adia, e qual o gatilho/condição para tratar.

**Última atualização:** 2026-05-26.

---

## D-001 — CancellationToken ausente em `IUseCase`

**O que:** A interface `IUseCase<TCommand,TResult>` em `EasyStock.Application/UseCases/Common/` não recebe `CancellationToken`. ~80 implementações ignoram cancelamento de request.

**Adiado por:** ADR-0013 (Deferred, 2026-05-16). Sem incidente real. Mudança ampla.

**Risco em produção:** long-running queries (Analytics tem 46 UC) não cancelam quando cliente abandona request — pool de conexão sofre sob carga.

**Gatilho para tratar:**
- Métricas APM mostrando >1% requests long-running, OU
- Primeiro incidente real de pool esgotado.

**Esforço estimado:** alto (refactor de ~80 UCs + call-sites + testes).

---

## D-002 — Duas superfícies HTTP coexistindo

**O que:** API Web usa `ApiResponse<T>` (envelope com status/errors), API Mobile usa `ActionResult<T>` direto. Documentado em ADR-0019 (Accepted).

**Adiado por:** decisão consciente — mobile precisa de payload menor.

**Risco:** custo de manutenção dobra para endpoints expostos nas duas superfícies; clientes externos podem confundir.

**Gatilho para tratar:**
- Decidir unificar quando WhatsApp / outros canais externos passarem a consumir API regularmente, OU
- Reescrever uma das duas se um cliente parceiro reclamar de inconsistência.

---

## D-003 — `EasyStock.Admin` sem `ProjectReferences` explícitas

**O que:** O projeto Admin não referencia `EasyStock.Application` ou `EasyStock.Api` diretamente; faz HTTP contra a API. Pode divergir silenciosamente.

**Adiado por:** funciona hoje; refactor exige decidir entre "Admin direto na Application" ou "Admin como cliente da API com SDK compartilhado".

**Risco:** breaking change na API quebra Admin sem o compilador avisar.

**Gatilho para tratar:**
- Primeiro incidente em produção causado por incompatibilidade Admin↔Api, OU
- Decisão estratégica de unificar painéis (Admin + Web).

---

## D-004 — 168 migrations não squashadas em `Infra.Postgre`

**O que:** Pasta `EasyStock.Infra.Postgre/Migrations/` tem 168 arquivos `.cs`. Replay-from-zero recém-corrigido (`c5dbb555`). Tooling `dotnet ef list-migrations` lento.

**Adiado por:** squash é destrutivo (precisa autorização R9) e quer-se fazer após v1.0 estável.

**Risco:** próxima migration vai degradar mais; onboarding de dev sofre.

**Gatilho para tratar:**
- Fase 5 do plano v1.0 inclui ETK-FREEZE-001 (squash em snapshot v1.0).
- Política: squash a cada release major (v1.0, v2.0, ...).

---

## D-005 — SQLite dev-fallback incompleto (`AddEasyStockSqliteInfrastructure`)

**O que:** Registra ~25 repos a menos que Postgres. App **não sobe local sem Postgres em Development**.

**Adiado por:** funcional sob Postgres em todos os ambientes.

**Risco:** trava onboarding de dev novo + bloqueia testes ad-hoc.

**Gatilho para tratar:**
- ETK-DEV-001 (proposto no plano v1.0, Fase 2) — decide entre completar SQLite OU docker-compose Postgres dev.
- Se ETK-DEV-001 entrar em v1.0, este débito sai daqui.

---

## D-006 — OTel collector inativo

**O que:** `OtlpEndpoint: "http://localhost:4317"` em todos os ambientes; nenhum collector consome.

**Adiado por:** ETK-0025 (no backlog ETK existente) cobre. Será resolvido na Fase 2.

**Risco:** primeiro incidente em saga assíncrona (Notificações ou Pagamentos) em produção = debug via grep de log.

**Gatilho para tratar:**
- ETK-0025 obrigatório antes da tag v1.0. Se ETK-0025 não couber, este débito vira blocker.

---

## D-007 — Stub de WhatsApp / SMS em produção

**O que:** `if (!builder.Environment.IsProduction())` registra stub; em prod, nada funciona.

**Adiado por:** TASK-EZ-WA-001 (Meta WhatsApp Cloud) ainda em curso fora do escopo v1.0.

**Risco:** comunicações com cliente storefront limitadas a email + push web.

**Gatilho para tratar:**
- TASK-EZ-WA-001 fechar → habilitar em prod.
- Avaliar SMS via Twilio/AWS SNS se WhatsApp não cobrir todos os casos.

---

## D-008 — AI (OpenAI / Anthropic) desabilitada

**O que:** `appsettings.json` tem chaves para OpenAI (gpt-4o-mini) e Anthropic, ambas `Enabled: false`.

**Adiado por:** sem caso de uso de produto definido.

**Risco:** nenhum agora. Apenas configuração que pode confundir devs.

**Gatilho para tratar:**
- Decisão de produto sobre feature AI (sugestão de produtos? assistente de catálogo?).
- Caso decisão seja "não usar", remover chaves do appsettings.

---

## D-009 — Dois diretórios de worktrees coexistindo

**O que:** Existem worktrees em `.claude/worktrees/` (5) e em `.worktrees/` (2). Inconsistente.

**Adiado por:** funcional, ninguém se confunde ainda.

**Risco:** comandos de cleanup (R9) podem perder um diretório.

**Gatilho para tratar:**
- Padronizar em `.claude/worktrees/` quando os 2 em `.worktrees/` forem fechados (PR mergeado ou abandonado).
- Adicionar regra no CLAUDE.md §1 (Worktrees) se for política explícita.

---

## D-010 — Validação OpenAPI/Swagger versus contratos reais

**O que:** Swagger gerado por reflexão, mas mobile usa `ActionResult<T>` direto (ADR-0019). Contratos reais podem divergir do que o Swagger declara para cada surface.

**Adiado por:** funcional para uso interno e debug.

**Risco:** clientes externos (parceiros) podem confiar no Swagger e tomar 500 silenciosos.

**Gatilho para tratar:**
- Primeiro parceiro externo consumindo API.
- Ou decisão de unificar surfaces (ver D-002).

---

## D-011 — `EasyStok.Mobile` (MAUI Android) embrionário

**O que:** 1.9K LoC, sem fluxo completo, sem CI/CD próprio.

**Adiado por:** PWA cobre mobile via webview.

**Risco:** mantê-lo no repo gera ruído de build e confusão de papel.

**Gatilho para tratar:**
- Decidir entre: (a) investir em paridade com PWA, (b) deletar projeto, (c) congelar com README "experimental".
- Decisão é v1.1+.

---

## D-012 — Spam de warnings de build (31 pré-existentes)

**O que:** Build tem 31 warnings: EF Core Relational 9.0.1 vs 9.0.4 (MSB3277), CS8602 nullable x4, CS9113, CS9107, XA0141 Android/Mobile.

**Adiado por:** nenhum é erro; alguns são do projeto Mobile (D-011).

**Risco:** warnings legítimos novos se afogam no ruído.

**Gatilho para tratar:**
- Após CI ativo (ETK-0020), promover regras de zero-warning gradualmente.
- Resolver D-011 elimina vários XA0141.

---

## D-013 — Health checks sem cobertura uniforme

**O que:** Health checks customizados existem (`ConfigurationHealthCheck`, `RedisHealthCheck`, `SqliteDatabaseHealthCheck`, `MongoMigrationHealthCheck`), mas **não há check para cada bounded context DENTRO** do v1.0.

**Adiado por:** ETK-SMOKE-001 (proposto no plano v1.0, Fase 2) cobre — vai criar 1 smoke por contexto.

**Risco:** smoke synthetic não detecta degradação parcial (ex: Pagamento ok mas Caixa não).

**Gatilho para tratar:**
- ETK-SMOKE-001 (Fase 2).

---

## Itens explicitamente NÃO-débito (decisões aceitas, não dor)

- **`Nfe*` em código + "Nota Fiscal" em UI** (ADR-0018): decisão consciente, não voltar atrás.
- **Sem MediatR**: padrão próprio `IUseCase` é decisão arquitetural, não débito.
- **Custom JWT + Cookie (sem ASP.NET Identity)**: funciona, decisão validada.
- **1 DbContext único**: simplifica, sem necessidade de bounded-context-per-context agora.

---

## Convenção de atualização

Cada item recebe novo ID `D-NNN` (sequencial). Quando tratado:
1. Atualizar item com `**Status:** Resolvido em <SHA>` no topo.
2. Mover seção para `## Resolvidos` ao fim do arquivo.
3. Não deletar — histórico de débito documenta evolução.
