# Plano endurecido: Refinamento UX do EasyStok Admin Panel (épico)

## Contexto

Relatório de QA/PO (JSON, 2026-06-28) com 31 itens de refinamento UX no Admin Panel
(backoffice multi-tenant, .NET 9), P0 a P3. As `sugestao_tecnica` assumem stack React;
o código real é Razor Pages + Alpine + Tailwind + tokens `--deck-*` + 23 TagHelpers +
sprite Lucide. Esta é a versão endurecida: toda marca de "investigar/verificar/se
derivável" foi resolvida no código. Nenhuma pendência aberta resta.

### Decisões travadas (Felipe)
1. Backend incluído nesta rodada (Onda 2 dedicada).
2. Épico completo (ondas item a item, incl. P2/P3).
3. Já-implementados: verificar e refinar. Regra aplicada: verificação que rebaixa um item
   recoloca ele na fila com nova prioridade (ver Achado A).

### Estado de sessão (§0)
Branch `master`, sync 0/0, working tree limpo, worktrees `dev/*` do harness (esperados).
Plan mode: nenhum arquivo alterado.

---

## Achado A: verificação dos 8 "já-feitos" (rebaixamentos incluídos)

| Item | Antes | Pós-verificação | Evidência | Ação |
|---|---|---|---|---|
| TEN-002 | ✅ | **◐ rebaixado** | `Tenants/Index.cshtml:151` barra existe, mas é estática na tabela, não snackbar fixo no bottom | re-slotar Onda 3 (FE, P) |
| TICK-001 | ✅ | ✅ confirmado | `Tickets/Index.cshtml:137-144` mostra `Xm`/`Xh` com cor | só countdown ao vivo fica opcional (Onda 5) |
| NAV-004 | ◐ | ✅ confirmado | `theme-toggle.js:6-12` persiste e aplica no load | polish `prefers-color-scheme` (Onda 4) |
| DASH-003 | ✅ | **◐ rebaixado** | `Index.cshtml`: só 3 de 8 cards são `<a>` (Ativos/Suspensos/Tickets); faltam Receita, Usuários ativos, Logins, Em atendimento, Tenants novos | re-slotar Onda 4 (FE, P) + definir destino de cada card |
| TEN-015 | ✅ | ✅ confirmado | `Detail.cshtml:1423-1437` avatar+email+badge tipo+data relativa | nada |
| TEN-016 | ◐ | **○ rebaixado e reclassificado FS** | `Detail.cshtml.cs:62-65` renderiza só o que a API devolve; catálogo `Features.cs` vive no Domain (Admin é BFF) | endpoint precisa expor catálogo completo: Onda 2 (BE) + Onda 4 (FE) |
| TICK-002 | ◐ | ◐ confirmado | `Tickets/Index.cshtml:163` badge de não-lidos existe; row não destacada | row highlight (Onda 4) |
| TEN-002+DASH-003 | - | caminho crítico alterado | dois ✅ viraram ◐ e voltam à fila | refletido nas Ondas 3 e 4 |

Saldo real: ✅2 confirmados (NAV-004, TEN-015, TICK-001 núcleo), ◐ rebaixados que voltam à
fila (TEN-002, DASH-003, TICK-002), ○ reclassificado FS (TEN-016).

---

## Tabela-índice endurecida (31 itens)

Tipo: FE/BE/FS. Onda: O0..O5. Mig?: exige migration (S/N). Tenant: escopo de isolamento
que a query nova deve respeitar. Esf: P/M/G.

| id | prio | tela | tipo | onda | Mig? | tenant | esf | status |
|---|---|---|---|---|---|---|---|---|
| NAV-001 | P1 | Sidebar badge | FE | O3 | N | n/a | P | segue (tooltip já; semântica reversível) |
| NAV-002 | P2 | Recentes | FE | O4 | N | n/a | P | ○ timestamp+status (localStorage) |
| NAV-003 | P1 | Topbar env | FE+cfg | O3 | N | n/a | P | ○ "ADMIN" fixo: tooltip+cor por env |
| NAV-004 | P2 | Tema | FE | O4 | N | n/a | P | ✅ persiste; +prefers |
| NAV-005 | P3 | Transição | FE | O5 | N | n/a | P | ○ fade-in |
| NAV-006 | P1 | Busca Ctrl+K | FE | O3 | N | n/a | M | ◐ headers de categoria |
| DASH-001 | P1 | MRR delta | FS | O2+O3 | **S** | global (IgnoreQF) | M | ○ precisa snapshot |
| DASH-002 | P1 | Status saúde | FE | O4 | N | n/a | P | ◐ formalizar estados/pulse |
| DASH-003 | P2 | Cards clicáveis | FE | O4 | N | n/a | P | ◐ 3/8; faltam 5 (definir destino) |
| DASH-004 | P2 | Recentes status | FE | O4 | N | n/a | P | ○ status inline |
| DASH-005 | P3 | Sparkline MRR | FS | O2(infra)+O5(FE) | **S** (compart.) | global (IgnoreQF) | M | ○ série acumula no tempo |
| TEN-001 | P1 | Última atividade | FS | O2+O3 | N | por-tenant `Where EmpresaId` | M | ○ MAX UltimoAcesso+sort |
| TEN-002 | P1 | Bulk snackbar | FE | O3 | N | n/a | P | ◐ barra existe; falta snackbar fixo |
| TEN-003 | P0 | Erro ERP/Sync | FE | O1 | N | 1 tenant | M | ○ granular 504/500/404+diagnóstico |
| TEN-004 | P1 | Banner alerta | FE | O3 | N | dado já vem | M | ◐ tipo/ícone/data |
| TEN-005 | P2 | Botões destrutivos | FE | O4 | N | n/a | P | ○ danger-outline |
| TEN-006 | P1 | Modal motivos | FE | O3 | N | n/a | P | segue (default reversível) |
| TEN-007 | P2 | Card mensalidade | FS | O2+O4 | N | join `EmpresaId` | M | ○ cobrança no DTO |
| TEN-008 | P2 | Último acesso rel | FE | O4 | N | n/a | P | ○ relativo |
| TEN-009 | P1 | Emoji→SVG usuários | FE | O3 | N | n/a | M | ○ +7 ícones no sprite |
| TEN-010 | P2 | Admin principal | FS | O2+O4 | **S** | por-tenant | M | ○ flag dono inexistente |
| TEN-011 | P3 | KPI loja | FS | O5(BE+FE) | N | 1 tenant (IgnoreQF) | G | ○ deferir inteiro |
| TEN-012 | P1 | Filtros Atividade | FE | O3 | N | n/a | M | ◐ responsivo |
| TEN-013 | P0 | Empty contextual | FS | O1(BE+FE) | N | usuariosDoTenant | M | ○ +`totalNaoFiltrado` |
| TEN-014 | P2 | Contador chars | FE | O4 | N | n/a | P | ○ condicional |
| TEN-015 | P1 | Autor notas | FE | feito | N | n/a | P | ✅ |
| TEN-016 | P2 | Features catálogo | FS | O2+O4 | N | 1 tenant | M | ○ endpoint catálogo+grid |
| TEN-017 | P3 | LGPD disabled | FE | O5 | N | server gating intacto | P | ○ +confirmar server |
| TICK-001 | P1 | SLA restante | FE | O5 opc. | N | n/a | P | ✅ núcleo; countdown opcional |
| TICK-002 | P2 | Não-lido row | FE | O4 | N | n/a | P | ◐ row highlight |
| TICK-003 | P1 | Filtros instant. | FE | O3 | N | n/a | M | ○ auto-submit no change |

Migrations no épico: 3 itens (DASH-001, DASH-005, TEN-010). Backend sem migration:
TEN-001, TEN-007, TEN-011, TEN-016 (endpoint), TEN-013 (campo extra). FE puro: o resto.

---

## ADRs esboçados (só itens com migration)

### ADR-A: tabela `MrrSnapshot` (cobre DASH-001 e DASH-005)
- **Contexto:** MRR é recalculado do estado atual (`MetricasFinanceirasUseCase.cs:92`). Não
  existe histórico; o passado não é reconstruível (preço de plano e status de assinatura
  mudam sem trilha temporal). `MrrArrChurnHandler` recalcula on-the-fly e é impreciso para
  meses passados.
- **Decisão:** criar tabela de snapshot mensal: `MrrSnapshot(Ano, Mes, MrrAtivo, MrrNovas,
  MrrCanceladas, MrrSuspensas, AtivasInicio, ReceitaRealizada, CapturadoEm)`. Job mensal
  popula. DASH-001 lê mês atual e anterior; DASH-005 lê os últimos 6.
- **Alternativas:** (a) reconstruir do estado atual: rejeitada (passado incorreto). (b) event
  sourcing de assinatura: rejeitada (overkill). (c) snapshot mensal: escolhida.
- **Ordem de aplicação (boot-verde antes de query):** 1) migration cria a tabela vazia;
  2) deploy do schema; 3) job de captura roda e popula o mês corrente; 4) só então o código
  de leitura referencia a tabela. Lição config-before-migration (ADR-0035 / issue #633):
  propriedade nova sem migration aplicada faz toda query 500. A leitura NÃO entra no mesmo
  deploy que a migration crua.
- **Consequência temporal:** série de 6 meses começa parcial e cresce 1 ponto por mês. Delta
  MoM (DASH-001) só tem base a partir do 2º snapshot; antes disso o FE mostra estado neutro
  ("sem base de comparação"), não seta.
- **Verificação:** boot da Api verde com a tabela vazia; 1 ciclo do job; query lê sem erro.

### ADR-B: flag de dono do tenant (cobre TEN-010)
- **Contexto:** não há flag de dono/criador em `UsuarioEmpresa` nem `Empresa`; só
  `NivelAcesso` (Admin/Gerente/Operador). Derivar "1º Admin por data" não é confiável
  (seed, duplicação).
- **Decisão:** adicionar `UsuarioEmpresa.IsOwner` (bool, default false) com backfill: marca
  como dono o Admin mais antigo por `CriadoEm` de cada tenant.
- **Alternativas:** (a) derivar em runtime: rejeitada (não confiável). (b) `Empresa.UsuarioDonoId`
  FK: equivalente, porém mais invasiva. (c) `IsOwner` em `UsuarioEmpresa`: escolhida.
- **Ordem (boot-verde):** 1) migration adiciona coluna com default; 2) deploy schema;
  3) script de backfill; 4) query/DTO passam a projetar `IsOwner`.
- **Verificação:** boot verde; backfill marca exatamente 1 dono por tenant; query lê sem erro.

---

## Isolamento multi-tenant (padrão + por item)

Padrão da base (`EasyStockDbContext`): `HasQueryFilter` global por `EmpresaId` +
bypass automático quando `IsSuperAdmin` (claim JWT). Queries admin cross-tenant usam
`.IgnoreQueryFilters()` com filtro explícito. Risco conhecido nesta base: filtro esquecido
+ contexto sem tenant vaza cross-tenant. Toda query nova abaixo carrega o filtro exigido.

| Query nova | Escopo | Filtro obrigatório |
|---|---|---|
| TEN-001 (lista, todos tenants) | cross-tenant (SuperAdmin) | projeção MAX dentro de `UsuariosEmpresas` já amarrada por `EmpresaId` do tenant da linha |
| TEN-007 (1 tenant) | single | join `CobrancaAssinatura` por `EmpresaId == id` |
| TEN-011 (1 tenant, por loja) | single | `Where EmpresaId == id` + `GroupBy LojaId`; `IgnoreQueryFilters` consciente |
| TEN-013 (1 tenant) | single | count não-filtrado restrito a `usuariosDoTenantIds` |
| TEN-016 (1 tenant) | single | merge catálogo x `TenantFeatureFlag Where EmpresaId == id` |
| DASH-001 / DASH-005 (global) | agregado global | sem dimensão tenant; `IgnoreQueryFilters` + agregação total, nunca por tenant arbitrário |
| TEN-010 (1 tenant) | single | `IsOwner` lido dentro do escopo do tenant |

Regra de revisão: nenhuma query nova com `.ToListAsync()` sem `Where EmpresaId` (ou
agregação global explícita). Entra no checklist de cada PR de backend.

---

## Contratos finais das 7 primitivas (Onda 0)

**1. Tempo relativo.** Fraseado pt-BR já existe inline (`Detail.cshtml:1383-1392`,
`format.js`); consolidar em duas faces:
- Server (Razor): `BrazilTime.FormatRelative(DateTime utc) -> string` em
  `Admin/Helpers/BrazilTime.cs`.
- Client (Alpine/fetch): função global `formatRelativo(iso) -> string` em
  `admin-components.js` (substitui as cópias inline).
- Thresholds de texto: `<60s` "agora há pouco"; `<1h` "há N min"; `<24h` "há N h";
  `<7d` "há N dia(s)"; `>=7d` data `dd/MM/yyyy`.
- Faixa de cor por uso (decidida no call-site, não na primitiva): TEN-001 âmbar `>15d`,
  vermelho `>30d`; TEN-008 âmbar `>7d`, vermelho `>30d`.

**2. StatusBadge.** Estender `BadgeTagHelper` com `bool Pulse` que injeta a classe
`deck-pulse` (keyframes já em `admin-premium.css:114`). Variantes mantidas: ok/crit/warn/
info/neutral. Mapa canônico status→(variant,label), extraído de `Tenants/Index.cshtml:100-105`
para helper único:

| status | variant | label |
|---|---|---|
| Ativa | ok | ATIVA |
| Suspensa | warn | SUSPENSA |
| Cancelada | crit | CANCELADA |
| Expirada | neutral | EXPIRADA |
| Trial expirado | crit | TRIAL EXPIRADO |
| Verificando (saúde) | warn + Pulse | VERIFICANDO |

**3. EmptyState.** `EmptyStateTagHelper` ganha `variant` (default/erro). `erro` aplica
`.es-empty-state-icon.is-error` (cor crit). Convenção dos 3 estados na view:

| estado | seleção | icon | variant |
|---|---|---|---|
| virgem | `!hasEverHadData` | inbox | default |
| filtrado | `hasEverHadData && total==0` | search | default |
| erro | `isError` | alert-triangle | erro |

**4. Tooltip.** Não criar `<es-tooltip>`. Regra: `title=""` nativo (já em `BadgeTagHelper.Title`)
para explicação curta de badge (NAV-001, NAV-003, TEN-008); `<es-help term=...>` para
glossário pedagógico. Decisão fechada: nenhum caso do relatório exige tooltip com HTML rico.

**5. Emoji→ícone.** 19 emojis mapeados. Adicionar ao sprite os 7 que faltam:
`key, eye-off, dollar-sign, package, lock, rotate-cw, upload`. Mapa de aplicação:

| emoji | lucide | no sprite? |
|---|---|---|
| 🔑 | key | adicionar |
| 🙈 | eye-off | adicionar |
| 💰 | dollar-sign | adicionar |
| 📦 | package | adicionar |
| 🔒 | lock | adicionar |
| ↻ | rotate-cw | adicionar |
| 📤 | upload | adicionar |
| 🚪👁✏️⚠️🛡️➕🗑▾ | log-out/eye/edit-2/alert-triangle/shield/plus/trash-2/chevron-down | já existem |
| 🚫 | x-circle | já existe |
| ↑ | chevron-up | já existe |

Para o ícone de "admin principal" (TEN-010): `star` e `crown` não existem; reusar `shield`
ou adicionar `star`. Decisão proposta: reusar `shield` (zero ícone novo).

**6. Tema prefers.** `theme-toggle.js`: quando não há valor salvo, ler
`window.matchMedia('(prefers-color-scheme: dark)')` como default inicial. Não altera o
mecanismo de persistência já existente.

**7. BulkActionBar.** Já existe em `DataTableTagHelper`. O delta de TEN-002 não é construir
a primitiva, é transformar a barra estática em snackbar fixo no bottom que aparece quando
`selectedCount > 0` (Alpine `esDataTable()` já expõe a contagem).

---

## Ondas + grafo de dependências BE→FE

### Sequência por onda
- **O0 Primitivas (FE):** os 7 contratos acima. Pronto: compila, 3 arch tests do Admin
  verdes, 1 uso real por primitiva.
- **O1 P0 (FS leve):** TEN-003 (FE puro, `ApiException.HttpStatus/ErrorCode`);
  TEN-013 (BE: campo `totalNaoFiltrado` no response de atividade; FE: 3 estados).
- **O2 Backend (FS, R5):** migrations ADR-A (MrrSnapshot) e ADR-B (TenantOwner) primeiro;
  depois queries sem migration (TEN-001 MAX+sort, TEN-007 cobrança no DTO, TEN-016 endpoint
  de catálogo). Pronto: boot verde, arch/unit verdes, schema deployado antes da leitura.
- **O3 P1 (FE, consome O2):** TEN-009 emoji→SVG; TEN-004 banner; NAV-003 env+tooltip;
  NAV-006 categorias; TEN-012 filtros responsivos; TICK-003 instantâneo; TEN-002 snackbar;
  TEN-006 motivos (default); NAV-001 tooltip+semântica; TEN-001 FE (coluna+sort).
- **O4 P2 (FE/FS):** DASH-002 pulse; DASH-003 5 cards; DASH-004 status; TEN-005 botões;
  TEN-007 FE (card mensalidade); TEN-008 relativo; TEN-010 FE (badge dono); TEN-014 contador;
  TEN-016 FE (grid catálogo); NAV-002 recentes; NAV-004 prefers; TICK-002 row.
- **O5 P3 (backlog):** NAV-005 fade-in; DASH-005 FE (sparkline, série já acumulando);
  TEN-011 (BE+FE KPI loja); TEN-017 LGPD (+confirmar gating server); TICK-001 countdown.

### Grafo BE→FE (caminho crítico em negrito)
```
O0 primitivas ─┬─> O1 TEN-003 (FE)
               ├─> O1 TEN-013 BE(totalNaoFiltrado) ─> O1 TEN-013 FE
               └─> **O2 backend**
**O2**: ADR-A MrrSnapshot[mig→deploy→job] ─┬─> O3 DASH-001 FE (delta)
                                           └─> O5 DASH-005 FE (sparkline)
        ADR-B TenantOwner[mig→deploy→backfill] ─> O4 TEN-010 FE
        TEN-001 query(MAX+sort) ─> O3 TEN-001 FE
        TEN-007 query(join) ─> O4 TEN-007 FE
        TEN-016 endpoint(catálogo) ─> O4 TEN-016 FE
```
Caminho crítico: O0 -> O2 (migrations: schema antes de query) -> ondas FE. As 3 migrations
seguem a ordem boot-verde dos ADRs. Itens FE puros (NAV-*, TEN-002/004/005/009/012/014,
TICK-002/003, DASH-002/003/004) não dependem de O2 e podem correr em paralelo após O0.

### Tensão P3 backend cedo (resolvida)
- DASH-005 (P3) compartilha `MrrSnapshot` com DASH-001 (P1): cria-se a infra na O2 (uma
  migration serve aos dois e começa a acumular histórico já); o FE da sparkline fica na O5.
- TEN-011 (P3) não compartilha infra: difere-se BE+FE inteiro para O5.
Isso preserva "P3 ao fim" com uma exceção justificada por reaproveitamento de migration.

---

## Bloqueados de produto: zero travas duras

Todos têm default reversível barato. Seguem com o default; produto ajusta depois sem
retrabalho estrutural.

| Item | Default | Custo de reverter |
|---|---|---|
| NAV-001 | tooltip explica o número; número = clientes que pedem atenção (suspensos+trial) | barato: troca de 1 expressão Alpine. Se o payload do dashboard não expõe a contagem, é 1 campo extra (pequeno BE), ainda reversível |
| TEN-006 | Inadimplência / Solicitação do cliente / Fraude suspeita / Manutenção / Outro | barato: editar 1 array |
| TEN-016 textos | descrições do catálogo `Features.cs` | barato: editar texto |

Nenhum item permanece bloqueado. O único resíduo é a semântica fina de NAV-001, entregue de
forma reversível (tooltip já agrega valor independente da decisão).

---

## Pendência de input (Achado F): fechada

JSON do relatório truncado em TICK-003. Busca no repo retornou apenas `docs/plan/04-ux.md`,
que é wireframe do módulo Pagamentos (Casa da Babá), não o relatório. Conclusão: o relatório
chegou inline no prompt e não tem cópia no repositório; TICK-004+ não são recuperáveis.
Encaminhamento: assumir TICK-003 como último item recebido de Tickets e pedir ao QA
confirmação de que não há itens além dele. Não bloqueia o épico.

---

## Riscos de segurança (não enfraquecer)

| Item | Risco | Mitigação |
|---|---|---|
| TEN-017 | `disabled` no botão não substitui validação | confirmar gating server-side do export antes de tocar UI; `disabled` é só camada visual |
| TEN-002 | suspensão em massa | manter modal+justificativa+auth/auditoria do backend; snackbar não cria caminho que pule confirmação |
| TEN-010 backfill | marcar dono errado | backfill determinístico (Admin mais antigo por `CriadoEm`); revisar amostra |
| queries novas | vazar cross-tenant | filtro `EmpresaId` obrigatório por item (tabela de isolamento) |

---

## Verificação end-to-end

1. Build/arch (R4): `scripts/poka-yoke/build-check.ps1` + `dotnet test --filter "Category=Architecture"`
   (Admin: Alpine, CssHex, RazorViewColor). Falha bloqueia commit.
2. Backend O2: boot da Api verde com schema novo aplicado ANTES de qualquer leitura; 1 ciclo
   do job de snapshot; backfill de dono confere 1 por tenant.
3. Visual: Admin local + preview_* MCP (`/verify`). Telas: Dashboard, `/Tenants`,
   `/Tenants/Detail/{id}` (7 abas), `/Tickets`. Resize para dark mode e viewport estreito (TEN-012).
4. Segurança: provar que o servidor recusa export sem justificativa (TEN-017) e suspensão
   sem auth (TEN-002), independente do estado da UI.

## Governança
- Issue-guarda do épico + sub-issue por onda (label `admin` + `priority:pN`). Agente abre e
  aguarda OK antes de codar (§4.5).
- R5: O2 toca DTOs/queries/migrations: fatiar em commits build-verdes, avisar antes. Sem
  migration sem ADR (ADR-A e ADR-B acima).
- R14 pt-BR. Commits Conventional + `refs/closes #N`.
- Ao aprovar: sobrescrever `docs/ux-refinement-plan.md` com esta versão.

---

## Diff vs plano anterior

| Mudança | Antes | Agora | Porquê |
|---|---|---|---|
| TEN-002 | ✅ feito | ◐ Onda 3 | barra não é snackbar fixo (verificado) |
| DASH-003 | ✅ feito | ◐ Onda 4 | só 3 de 8 cards clicáveis (verificado) |
| TEN-016 | ◐ FE | ○ FS (O2+O4) | endpoint precisa expor catálogo (Admin é BFF) |
| TEN-013 | "investigar" | FS confirmado | precisa campo `totalNaoFiltrado` no response |
| TEN-010 | "investigar" | FS + migration (ADR-B) | sem flag de dono no schema |
| DASH-001/005 | "BE" genérico | migration (ADR-A), infra compartilhada | MRR passado não reconstruível |
| TEN-001/007/011 | "BE" genérico | query/projeção, sem migration | campos-fonte já existem |
| Bloqueados | 3 travas | 0 travas | todos com default reversível barato |
| Ícones sprite | "6 a adicionar" | 7 exatos: key, eye-off, dollar-sign, package, lock, rotate-cw, upload | grep definitivo no sprite |
| Tempo relativo | "criar" | consolidar padrão pt-BR já inline (`Detail.cshtml:1383`) | evitar 3ª variação |
| Input TICK-004+ | pendente | fechado (não existe no material) | busca no repo |
| Isolamento tenant | não mapeado | tabela por item + regra de PR | base tem histórico de RLS decorativa |
