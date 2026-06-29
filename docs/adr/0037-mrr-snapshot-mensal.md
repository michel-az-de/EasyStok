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
