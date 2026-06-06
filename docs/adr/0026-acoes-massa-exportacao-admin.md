# ADR-0026 — Ações em massa + exportação CSV no Admin (helper Csv, convenção bulk, falha parcial)

**Status:** Aceito  
**Data:** 2026-06-05  
**Issue:** [#488](https://github.com/michel-az-de/EasyStok/issues/488)

## Contexto

No painel SaaS superadmin (`EasyStock.Admin`), suporte/billing depende de planilha. Apenas
**Faturas** tinha "Exportar CSV"; **Clientes** (tenants) e **Tickets** não tinham, e **Clientes**
não permitia seleção múltipla — suspender, trocar plano ou tirar relatório exigia abrir tenant por
tenant. Esta entrega adiciona exportação CSV nas duas telas e ações em massa (seleção por página).

Ao implementar, surgiram decisões reutilizáveis que valem registro: o formato do CSV, **onde** mora
a lógica de exportação vs. a de mutação em massa, e a semântica de falha parcial.

## Decisão

1. **Helper CSV compartilhado** — `EasyStock.Application/Common/Csv.cs`. Saída Excel-pt-BR: BOM UTF-8,
   separador `;`, terminador `\r\n` **explícito** (RFC-4180, determinístico — não usa
   `Environment.NewLine`, que varia por SO) e **endurecimento anti-injeção de fórmula** (prefixa `'`
   quando o campo inicia com `= + - @ TAB CR`) além do quoting RFC-4180. Os exporters usam o helper.

2. **Exportação = UseCase puro na camada Application**, backed por um método de leitura na porta
   (`IAdminTenantsQueries.ListarParaExportarAsync`, `IAdminTicketRepository.ListarParaExportarAsync`,
   ambos `AsNoTracking`, projeção plana, `OrderBy` determinístico, `Ids` não vazio &gt; filtros). É a
   parte determinística e a que mais merece teste — coberta por testes unitários (mock da porta).

3. **Mutação em massa = loop no controller da API**, reusando o **corpo exato da operação single**
   (Tenants: 2 trilhas de auditoria — `auditLogRepo` + `AdminAuditService`; Tickets: `HelpdeskTicketService`,
   que self-commita + notifica + grava histórico). Vive no controller, **não** num UseCase, porque os
   side-effects dependem de serviços da camada API que um UseCase de Application não pode referenciar.
   Coberta por testes de integração (`SkippableFact` + Testcontainers).

4. **Falha parcial best-effort** — commit por id, resposta `BulkResult(Total, Sucesso, JaNoEstado, Falhas[])`.
   `db.ChangeTracker.Clear()` por id isola mutação rastreada não-commitada (ex.: erro de banco no meio)
   para não vazar no commit do próximo id. Falha da 2ª trilha de auditoria **após** o commit não derruba
   o id (Sucesso-com-aviso). Erro de domínio esperado (ex.: assinatura cancelada) vira falha legível, não erro opaco.

5. **UI de seleção em massa** — `<es-data-table bulk-name="ids">` + checkbox por linha. Os IDs são
   "carimbados" em hidden inputs no `@click` do botão (helper `esStampIds`), **antes** do submit nativo
   do form, contornando o gotcha do `FormTagHelper` que descarta `@submit.prevent`. Seleção = página
   visível (≤20/25), baseada em IDs. "Atribuir atendente" em tickets virou **"Assumir"** (self), pois
   não existe seletor de atendente no código.

### Convenções de formato fixadas (tiebreaker: igualar o export de Faturas)

| Tema | Decisão |
|---|---|
| Datas | `yyyy-MM-dd HH:mm:ss` / `yyyy-MM-dd`, **UTC cru** (igual Faturas-export; a listagem usa horário local). |
| Número | `F2` cultura pt-BR. |
| Rótulo de Status (tenant) | `enum.ToString()` (= o que a listagem mostra). |
| SLA (ticket) | bools armazenados `SlaRespostaViolado`/`SlaResolucaoViolado` — mesma fonte do filtro e da coluna. |
| Bool | `Sim`/`Nao`. Campo nulo → vazio (nunca a palavra "null"). |

## Motivação

- **Paridade** com o padrão consolidado de Faturas (Admin proxy → API → UseCase/serviço).
- **Testabilidade no nível certo**: formatação CSV em unit; mutação + side-effects em integração.
- **Preservar auditoria e side-effects**: o loop reusa o single, não reescreve a mutação.
- **Segurança**: anti-fórmula no CSV (campos são entrada de usuário multi-tenant), seleção ≤20 + modal
  de confirmação reduzem o risco de ação acidental em massa, tudo `[Authorize(Policy="SuperAdmin")]`.

## Consequências

- Novos exports do Admin devem usar `Csv.Build`/`Csv.Field` (não hand-roll).
- Novas ações em massa seguem o `BulkResult` + loop-por-id-com-`ChangeTracker.Clear()`.
- **Limitação conhecida (v1):** uma falha de banco *no próprio commit* pode deixar o `DbContext`
  request-scoped inconsistente (orientação EF: descartar o contexto após `SaveChanges` falho);
  `ChangeTracker.Clear()` mitiga mas não isola 100%.

### Follow-ups (registrados)

1. Retrofit do `ExportarFaturasCsvUseCase` para usar o helper `Csv` (ganha anti-fórmula + `\r\n`).
2. Picker de atendente para atribuir tickets a terceiros (hoje só "Assumir"/self).
3. `IDbContextFactory` (contexto por id) para isolamento forte do best-effort.
4. "Selecionar todos do filtro" (cross-página) para suspender/relatar volumes &gt; 1 página de uma vez.
