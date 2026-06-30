# ADR-0037: Snapshot mensal de MRR (MrrSnapshot)

**Status:** Proposed
**Data:** 2026-06-28
**Escopo:** Onda 2 do refinamento UX do Admin (issue #730), itens DASH-001 e DASH-005.

## Contexto

O MRR exibido no dashboard do Admin e recalculado sob demanda a partir do estado ATUAL
das assinaturas (`MetricasFinanceirasUseCase`: soma de `Plano.PrecoMensal` das assinaturas
`Status=Ativa`). Nao existe historico persistido: preco de plano e status de assinatura
mudam sem trilha temporal, entao o passado NAO e reconstruivel de forma confiavel. O
`MrrArrChurnHandler` tenta recalcular por mes on-the-fly e e impreciso para meses passados.

Dois itens de UX dependem de historico:
- DASH-001 (P1): delta MoM no card de MRR (precisa do MRR do mes anterior).
- DASH-005 (P3): sparkline dos ultimos 6 meses (precisa de serie mensal).

## Decisao

Criar uma tabela de snapshot mensal de MRR, populada por um job:

`MrrSnapshot(Ano, Mes, MrrAtivo, MrrNovas, MrrCanceladas, MrrSuspensas, AtivasInicio,
ReceitaRealizada, CapturadoEm)`.

- DASH-001 le o mes corrente e o anterior e calcula o delta.
- DASH-005 le os ultimos 6 registros.

## Alternativas consideradas

| Opcao | Veredito |
|---|---|
| Reconstruir do estado atual on-the-fly | Rejeitada: nao reconstroi o passado (preco/status mudam sem trilha). |
| Event sourcing das assinaturas | Rejeitada: overkill para a necessidade. |
| Snapshot mensal persistido | **Escolhida**: simples, suficiente, comeca a acumular ja. |

## Ordem de aplicacao (boot-verde antes de qualquer query)

Lição config-before-migration (ADR-0035 / issue #633): propriedade/coluna nova sem migration
aplicada faz TODA query 500. A leitura NAO entra no mesmo deploy que a migration crua.

1. Migration cria a tabela VAZIA.
2. Deploy do schema (a VM auto-aplica migrations no boot da API).
3. Job de captura roda e popula o mes corrente.
4. So entao o codigo de leitura (cards/sparkline) referencia a tabela.

## Consequencias

- A serie de 6 meses comeca PARCIAL e cresce 1 ponto por mes (dados passados nao existem).
- O delta MoM (DASH-001) so tem base de comparacao a partir do 2o snapshot; antes disso o
  FE mostra estado neutro ("sem base de comparacao"), nao uma seta.
- Mais uma tabela e um job recorrente para manter.

## Verificacao

- Boot da Api verde com a tabela vazia (sem 500).
- 1 ciclo do job popula o mes corrente.
- Query de leitura roda sem erro com 1 e com >=2 snapshots.

## Itens de acao

1. [ ] Migration `MrrSnapshot` (tabela nova, aditiva).
2. [ ] Job mensal de captura (idempotente por Ano/Mes).
3. [ ] Endpoint/projeção para DASH-001 (atual + anterior) e DASH-005 (ultimos 6).
4. [ ] FE: delta no card de MRR; sparkline com `<es-sparkline>` (ja existe).

---

## Adendo (2026-06-30): receita contratado/faturado/recebido ao vivo + emissao recorrente

**Status do adendo:** Proposed. **Refs:** #754 (issue umbrella), #700, #627, #274.
**Origem:** relatorio de diagnostico Fase A' (2026-06-30).

### Contexto adicional

O `MrrSnapshot` (acima) cobre a SERIE HISTORICA. Faltava a distincao AO VIVO entre receita
contratada e realizada, e a emissao de fatura por ciclo. Medicao confirmou (arquivo:linha):
MRR = preco de catalogo em 3 implementacoes divergentes (`AdminDashboardQueries.cs:20-22`,
`AssinaturaEmpresaRepository.SomarPrecoMensalAtivasAsync`, `FleetOperationQueries.cs:144-145`);
idempotencia de fatura por-assinatura (`FaturaSaasFactory.cs:105`), nao por ciclo, e a Fatura
nao tem competencia; `AdminDashboardQueries` agrega cross-tenant SEM
`UseRowLevelSecurityBypass()` (truncavel em prod sob role sem BYPASSRLS, ver incidente
2026-05-22), enquanto `FleetOperationQueries.cs:23` usa o bypass.

### Decisao

1. Tres metricas canonicas ao vivo, fonte unica = a DEFINICAO (predicado compartilhado):
   - `mrrContratado` = SUM(`Plano.PrecoMensal`) das assinaturas Ativa.
   - `mrrFaturado` = SUM(`Fatura.Total`) de `Origem=Assinatura` no mes (por `DataEmissao`).
   - `recebidoMes` = SUM por `Fatura.DataPagamentoTotal` no mes (caixa real, nao por emissao).
2. A agregacao cross-tenant roda na camada Infra sob `db.UseRowLevelSecurityBypass()`
   (espelha `FleetOperationQueries`), nao em Application puro.
3. Competencia na Fatura: `DataCompetencia DateTime?` (padrao `ContaPagar`/`ContaReceber`),
   derivando `(Ano,Mes)` para casar com `MrrSnapshot`. Idempotencia por competencia com indice
   unico parcial `WHERE Origem='Assinatura' AND Status<>'Cancelada'` + captura de 23505
   (espelha `IdempotencyKeyRepository`).
4. Emissao recorrente DESACOPLADA de cobranca: job invoice-only sem Efi/Pix, flag global
   `EnableRecorrenciaFaturamentoJob` (default false) + dry-run. Cobranca real fica em #627/#700.

### Consequencia para o MrrSnapshot

Quando o job de captura mensal for implementado, ele chama o read-model de receita unico:
`MrrAtivo <- mrrContratado`; `ReceitaRealizada <- recebidoMes`. Para historico de FATURADO,
adicionar coluna `MrrFaturado` ao `MrrSnapshot` (a tabela proposta acima nao a possui).

### Itens de acao do adendo

5. [ ] Read-model de receita (Infra, sob RLS bypass) com as 3 metricas, fonte unica.
6. [ ] Corrigir RLS cross-tenant de `AdminDashboardQueries` + teste de runtime.
7. [ ] Migration `Fatura.DataCompetencia` + indice unico parcial; emissao recorrente idempotente.
8. [ ] (futuro) Coluna `MrrFaturado` no `MrrSnapshot` para historico do realizado.
