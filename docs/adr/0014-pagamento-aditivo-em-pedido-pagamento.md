# ADR 0014 — Pagamento estendido aditivamente em `PedidoPagamento` (sem tabela nova)

**Status:** Proposed (2026-05-16)
**Contexto do plano:** Caixa Conciliado + Pagamentos Múltiplos por Pedido — `docs/plan/`.

## Decisão

**Estender `PedidoPagamento` (tabela física `pedido_pagamentos`) com colunas
aditivas via `ADD COLUMN` em vez de criar tabela `pagamentos` separada.**

Colunas adicionadas (todas com `DEFAULT` seguro para backfill): `EmpresaId`
(denormalizado), `Status` (`confirmado`/`estornado`/`falhou`),
`ConciliacaoTipo` (`fisico`/`adquirente`/`nao_conciliavel`), `EstornadoEm`,
`EstornadoPorUserId`, `EstornadoPorNome`, `MotivoEstorno`,
`PagamentoOriginalId` (self-FK para estornos), `MovimentoCaixaId` (FK
opcional para `movimentos_caixa`).

Nenhuma coluna existente é removida, renomeada ou alterada de tipo. Nome da
entidade C# permanece `PedidoPagamento`.

## Opções consideradas

### Opção A — Estender `PedidoPagamento` (escolhida)

| Dimensão | Avaliação |
|---|---|
| Complexidade | Baixa: 9 ADD COLUMN, 4 índices concurrent, 1 backfill |
| Compatibilidade Mobile MAUI | Mantida: APK v1.0.7 lê apenas campos antigos com defaults novos transparentes |
| Compatibilidade PWA | Mantida: leitura de `pedido.Pagamentos[]` continua válida; apenas DTO mapper passa a filtrar por `Status` |
| Rollback | Trivial: feature flag OFF → backend para de escrever em colunas novas |
| Custo de churn | Zero — nenhum código existente quebra |

**Prós**: solução respeita princípio aditivo do escopo. Sem migração de
dados massiva. Sem fragmentação entre duas tabelas. Sync code do mobile
intacto. Backfill é apenas preenchimento de defaults baseados em colunas
existentes.

**Contras**: tabela ganha colunas nullable que só fazem sentido para
estornos (~5% das linhas em prod). Espaço de armazenamento +~50 bytes/linha.
Para Casa da Babá (~22k linhas/ano): +1MB/ano. Negligível.

### Opção B — Criar tabela `pagamentos` nova

| Dimensão | Avaliação |
|---|---|
| Complexidade | Alta: CREATE TABLE + backfill completo (22k linhas) + sync dual |
| Compatibilidade Mobile MAUI | **QUEBRADA**: APK v1.0.7 sync usa endpoint que retorna `pedido.Pagamentos[]` de `pedido_pagamentos`; precisaria de nova versão APK + período de coexistência |
| Compatibilidade PWA | Refactor obrigatório em todas as telas que lêem `pedido.Pagamentos[]` |
| Rollback | Difícil: dados em duas tabelas, modo coexistência precisa decisão de "fonte da verdade" |
| Custo de churn | Alto: estimativa +1 semana só para sync mobile compatível |

**Prós**: design mais limpo conceitualmente. Tabela nova começa com schema
ótimo, sem colunas legacy desnecessárias para estornos. Permite renomear
`Metodo` → `FormaPagamento`, etc.

**Contras**: bloquear mobile não é aceitável (Casa da Babá depende do APK
em produção). Coexistência dupla é fonte de bug. Princípio aditivo do escopo
proíbe destrutivo, e "deprecar tabela em uso" é destrutivo na prática.

## Análise de trade-off

A diferença está em **valor incremental** vs **custo de churn**. Opção B
melhora design só marginalmente (mesmo schema final caberia em ambas), mas
custa 1 semana e quebra contrato existente. Opção A mantém schema "menos
elegante" mas funcional, sem risco para produção.

ADR-0011 (Nomenclatura PT-BR) já estabelece que "código pré-existente do
projeto que não segue a regra **não é renomeado retroativamente** — custo
de churn maior que ganho". Mesmo princípio se aplica aqui.

## Consequências

**Becomes easier**:
- Implementação em ~3 dias (M01–M06 + entity expansion).
- Rollback de verdade via feature flag.
- Zero risco para Mobile MAUI durante deploy.

**Becomes harder**:
- Schema da tabela `pedido_pagamentos` fica "memorial" — colunas novas só
  preenchidas pós-feature, mistas com defaults históricos. Documentar em
  comentários da tabela é importante.
- Queries de estorno precisam de `WHERE pagamento_original_id IS NOT NULL`
  para localizar contra-lançamentos.

**To revisit**:
- Se o projeto eventualmente decidir reorganizar pagamentos (ex: separar
  pagamentos de pedido vs pagamentos de fatura via tabela mãe abstrata),
  revisitar este ADR. Trigger: módulo Financeiro F0 desbloqueia escopo de
  reorganização.

## Action items

1. [ ] Migration M01 — `*_AddIdempotencyBodyHash` (preparatória).
2. [ ] Migration M02 — `*_CreateSessaoCaixa`.
3. [ ] Migration M03 — `*_AddCaixaColumnsAditivos` (ADD COLUMN em `pedido_pagamentos`).
4. [ ] Migration M04 — `*_AddCaixaIndexesConcurrent`.
5. [ ] Migration M05 — `*_BackfillPagamentos` (popula `EmpresaId`, `ConciliacaoTipo`).
6. [ ] Migration M06 — `*_TightenPagamentosConstraints` (NOT NULL + CHECK + FKs).
7. [ ] EF Core configurations atualizadas para refletir novas colunas.
8. [ ] DTO `PedidoPagamentoDto` recebe novos campos.
9. [ ] PWA atualiza badges para considerar `Status` no cálculo de "pago".

Detalhes técnicos em [`../plan/05-migracao.md`](../plan/05-migracao.md).
