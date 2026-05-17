# ADR 0015 — `SessaoCaixa` como entidade explícita (não agregação de `MovimentoCaixa`)

**Status:** Proposed (2026-05-16)
**Contexto do plano:** Caixa Conciliado + Pagamentos Múltiplos por Pedido — `docs/plan/`.

## Decisão

**Criar `SessaoCaixa` como entidade nova com tabela própria `sessoes_caixa`**,
em vez de continuar derivando "sessão aberta" via query agregadora sobre
`MovimentoCaixa` (`tipo='abertura'` sem `tipo='fechamento'` no mesmo dia).

`SessaoCaixa` é o aggregate root do módulo Caixa. `MovimentoCaixa` ganha FK
opcional `SessaoCaixaId` (NULL para movimentos pré-feature e movimentos
retroativos pós-fechamento). `FechamentoCaixa` ganha FK 1:1 `SessaoCaixaId`.

State machine: `aberta → em_conferencia → fechada` (terminal). `em_conferencia`
permite voltar a `aberta` se operador cancelar conferência sem fechar.

## Opções consideradas

### Opção A — `SessaoCaixa` como entidade (escolhida)

**Schema** (resumo):
```sql
CREATE TABLE sessoes_caixa (
  id uuid PRIMARY KEY,
  empresa_id uuid NOT NULL,
  loja_id uuid NULL,
  data_operacional date NOT NULL,
  aberta_em timestamptz NOT NULL,
  saldo_inicial numeric(14,2) NOT NULL DEFAULT 0,
  iniciada_conferencia_em timestamptz NULL,
  fechada_em timestamptz NULL,
  status varchar(20) NOT NULL DEFAULT 'aberta',
  -- ... metadata (aberta_por_*, fechada_por_*, observacoes)
  CONSTRAINT chk_status CHECK (status IN ('aberta','em_conferencia','fechada'))
);
CREATE UNIQUE INDEX ux_sessoes_caixa_aberta_por_dia
  ON sessoes_caixa (empresa_id, COALESCE(loja_id, ...uuid_zero...), data_operacional)
  WHERE status IN ('aberta','em_conferencia');
```

**Prós**:
- Estado `em_conferencia` precisa coluna persistida — impossível derivar de
  `MovimentoCaixa` (não há "tipo conferência").
- Aggregate root claro para advisory locks (`pg_advisory_xact_lock(hashtext(sessao_id))`).
- FK 1:1 com `FechamentoCaixa` torna explícito que um fechamento pertence
  a uma sessão.
- Unique partial index garante exclusão atômica de sessões abertas
  duplicadas via constraint do banco.
- Observabilidade: queries de "tempo médio de conferência" ficam triviais
  (`fechada_em - iniciada_conferencia_em`).

**Contras**:
- Mais uma tabela. Mais um índice. Mais um migration.
- Para histórico (pré-feature), precisa backfill criando `SessaoCaixa`
  retroativa para cada par `abertura/fechamento` em `MovimentoCaixa` —
  ~30s para Casa da Babá, ver `docs/plan/05-migracao.md` F.3.

### Opção B — Manter agregação de `MovimentoCaixa` + adicionar `tipo='conferencia'`

**Prós**:
- Zero schema novo.
- Pares `abertura/fechamento` históricos continuam coerentes.

**Contras**:
- Estado `em_conferencia` codificado como movimento é semanticamente esquisito
  (movimento é "evento", não "estado de período"). Confunde leitura.
- Cancelar conferência exige criar `tipo='cancelar_conferencia'` —
  cresce vocabulário do enum sem ganho.
- Advisory locks precisam de chave inventada (`hash(empresa, loja, data)`) que
  pode colidir entre dias diferentes.
- Unique index parcial existente (`UniqueAberturaCaixaPorDia`) protege apenas
  uma aresta da state machine; adicionar mais arestas exigiria mais índices
  parciais condicionais — frágil.

### Opção C — Trigger SQL ou view materializada

Rejeitado de cara: o projeto usa C# code-first EF Core, sem triggers SQL
(ver migrations existentes). Adicionar trigger só para "sessão" introduz
camada paralela de lógica de negócio fora do Domain.

## Análise de trade-off

Opção B economiza 1 tabela, mas paga em complexidade de queries, ambiguidade
semântica, e fragilidade de locks. Opção A custa 1 tabela + backfill, mas
entrega aggregate root claro e proteções de banco corretas.

State machine de 3 estados com estado intermediário é o caso clássico de
"persistir o estado". Não usar entity é falsa economia.

## Consequências

**Becomes easier**:
- Wizard de fechamento (UI) tem ciclo de vida claro mapeado a transições.
- Auditoria fiscal: cada fechamento pertence inequivocamente a uma sessão
  identificável.
- Reabertura proibida via constraint de banco + interceptor EF (ver
  `FechamentoCaixaImutavelInterceptor` em `docs/plan/02-estados-e-eventos.md`).

**Becomes harder**:
- Histórico pré-feature precisa de backfill (script SQL em F.3, idempotente).
- Movimentos retroativos pós-fechamento ficam com `SessaoCaixaId = NULL` —
  precisa documentar que isso é OK e como aparecem no relatório do dia atual
  (seção "ajustes de períodos anteriores").

**To revisit**:
- Se eventualmente precisar de "sessões parciais" (ex: turno manhã + turno
  tarde no mesmo dia), `SessaoCaixa` já tem o esqueleto para suportar — só
  remover unique constraint de `data_operacional` e adicionar `turno`.

## Action items

1. [ ] Migration M02 cria tabela `sessoes_caixa`.
2. [ ] Migration M03 adiciona `SessaoCaixaId` a `movimentos_caixa` e
       `fechamentos_caixa`.
3. [ ] Entity `SessaoCaixa` + factory `Abrir()` + métodos
       `IniciarConferencia()` / `Fechar()` / `CancelarConferencia()`.
4. [ ] `SessaoCaixaStateMachine` em `EasyStock.Domain/Caixa/` (espelha
       `PedidoStateMachine`).
5. [ ] Backfill F.3 (script SQL idempotente).
6. [ ] `FechamentoCaixaImutavelInterceptor` registrado no DbContext.
7. [ ] Test de arquitetura: `SessaoCaixa` não tem dependência fora do Domain.

Detalhes técnicos em [`../plan/01-dominio.md`](../plan/01-dominio.md) B.1.a
e [`../plan/02-estados-e-eventos.md`](../plan/02-estados-e-eventos.md) C.2.
