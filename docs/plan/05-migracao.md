# 05 — Migrations e Backfill

> Parte do [Plano](README.md). Anterior: [04-ux.md](04-ux.md). Próximo: [06-testes.md](06-testes.md).

### F.1 Estratégia geral

**6 migrations sequenciais** (dividida para minimizar tempo de lock em produção):

| # | Migration | Conteúdo | Duração estimada | Concurrent? |
|---|---|---|---|---|
| M01 | `*_AddIdempotencyBodyHash` | `ALTER TABLE idempotency_keys ADD body_hash varchar(64) NULL` | <1s | aditivo |
| M02 | `*_CreateSessaoCaixa` | `CREATE TABLE sessoes_caixa` + índices iniciais | <1s | aditivo |
| M03 | `*_AddCaixaColumnsAditivos` | `ALTER` em movimentos_caixa, fechamentos_caixa, pedido_pagamentos (ADD COLUMN com defaults) | <5s mesmo com Casa da Babá em prod (~22k pedido_pagamentos) | aditivo |
| M04 | `*_AddCaixaIndexesConcurrent` | `CREATE INDEX CONCURRENTLY` para todos os índices novos | depende do tamanho; ~30s para 22k linhas | concorrente, sem lock |
| M05 | `*_BackfillPagamentos` | UPDATE em massa para preencher `empresa_id` denorm + `conciliacao_tipo` derivado do método | ~30s para 22k linhas | sem lock pesado |
| M06 | `*_TightenPagamentosConstraints` | `ALTER` para `empresa_id NOT NULL` (depois do backfill) | <2s | dependente do M05 |

**Total estimado em produção (Casa da Babá ~22k pedido_pagamentos)**: <2 min.

### F.2 Conteúdo das migrations

#### F.2.1 M01 — `*_AddIdempotencyBodyHash`

```csharp
public partial class AddIdempotencyBodyHash : Migration
{
    protected override void Up(MigrationBuilder b) =>
        b.AddColumn<string>("body_hash", "idempotency_keys", "varchar(64)", nullable: true);
    protected override void Down(MigrationBuilder b) =>
        b.DropColumn("body_hash", "idempotency_keys");
}
```

#### F.2.2 M02 — `*_CreateSessaoCaixa`

Tabela completa (B.1.a) + índices não-concurrent iniciais (PK, FKs).

#### F.2.3 M03 — `*_AddCaixaColumnsAditivos`

Todos os `ALTER TABLE ... ADD COLUMN` para `movimentos_caixa`,
`fechamentos_caixa`, `pedido_pagamentos`. Todos com `DEFAULT` adequado para
**permitir aplicação sem `NOT NULL` constraint inicialmente**:

```sql
ALTER TABLE pedido_pagamentos
  ADD COLUMN empresa_id uuid NULL,
  ADD COLUMN status varchar(20) NOT NULL DEFAULT 'confirmado',
  ADD COLUMN conciliacao_tipo varchar(20) NOT NULL DEFAULT 'fisico',
  ADD COLUMN estornado_em timestamptz NULL,
  ADD COLUMN estornado_por_user_id uuid NULL,
  ADD COLUMN estornado_por_nome varchar(120) NULL,
  ADD COLUMN motivo_estorno text NULL,
  ADD COLUMN pagamento_original_id uuid NULL,
  ADD COLUMN movimento_caixa_id uuid NULL;
-- CHECK constraints só após backfill (M06)
```

Defaults garantem que linhas existentes não quebrem: `status = 'confirmado'`
para todos os históricos, `conciliacao_tipo = 'fisico'` (depois corrigido em M05).

#### F.2.4 M04 — `*_AddCaixaIndexesConcurrent`

```sql
CREATE INDEX CONCURRENTLY ix_pedido_pagamentos_empresa_status ON pedido_pagamentos (empresa_id, status);
CREATE INDEX CONCURRENTLY ix_pedido_pagamentos_movimento_caixa ON pedido_pagamentos (movimento_caixa_id) WHERE movimento_caixa_id IS NOT NULL;
CREATE INDEX CONCURRENTLY ix_pedido_pagamentos_pagamento_original ON pedido_pagamentos (pagamento_original_id) WHERE pagamento_original_id IS NOT NULL;
CREATE INDEX CONCURRENTLY ix_pedido_pagamentos_pago_em_empresa ON pedido_pagamentos (empresa_id, pago_em DESC) WHERE status = 'confirmado';
CREATE INDEX CONCURRENTLY ix_movimentos_caixa_sessao ON movimentos_caixa (sessao_caixa_id) WHERE sessao_caixa_id IS NOT NULL;
CREATE INDEX CONCURRENTLY ix_movimentos_caixa_pagamento ON movimentos_caixa (pagamento_id) WHERE pagamento_id IS NOT NULL;
CREATE INDEX CONCURRENTLY ix_fechamentos_caixa_sessao ON fechamentos_caixa (sessao_caixa_id) WHERE sessao_caixa_id IS NOT NULL;
CREATE UNIQUE INDEX CONCURRENTLY ux_fechamentos_caixa_verificacao_codigo ON fechamentos_caixa (verificacao_codigo) WHERE verificacao_codigo IS NOT NULL;
```

**Importante**: EF Core migration **não suporta `CREATE INDEX CONCURRENTLY`
nativamente**. Usar `migrationBuilder.Sql("CREATE INDEX CONCURRENTLY ...")`
+ marcar a migration como `TransactionalDdl = false` via attribute custom
(ou aplicar fora da TX EF). Padrão para esses casos no projeto: pesquisar
em migrations existentes; se não houver, adicionar helper.

#### F.2.5 M05 — `*_BackfillPagamentos`

```sql
-- 1. Preencher empresa_id denormalizado via JOIN
UPDATE pedido_pagamentos pp
SET empresa_id = p.empresa_id
FROM pedidos p
WHERE pp.pedido_id = p.id AND pp.empresa_id IS NULL;

-- 2. Corrigir conciliacao_tipo baseado em método (sobrescreve default 'fisico')
UPDATE pedido_pagamentos
SET conciliacao_tipo = CASE
    WHEN lower(metodo) = 'dinheiro' THEN 'fisico'
    WHEN lower(metodo) IN ('pix','credito','debito','transferencia') THEN 'adquirente'
    ELSE 'fisico'
END
WHERE conciliacao_tipo = 'fisico'; -- só toca defaults, preserva quem já foi alterado

-- 3. movimento_caixa_id permanece NULL para histórico — NÃO criamos
-- MovimentoCaixa retroativo. Por quê? Caixa existente já foi fechado
-- com agregação atual (GetTotalPagamentosPedidosDoDiaAsync). Criar
-- movimentos retroativos linkados alteraria saldos passados e quebraria
-- relatórios fechados.
```

Idempotente: rodar 2x não duplica (condições `WHERE empresa_id IS NULL` etc).

**Para Casa da Babá (~22k linhas)**: <30s. Bloqueia tabela em UPDATE
(Postgres MVCC, sem lock total, mas escritas concorrentes esperam). Janela
de deploy: noite de domingo após 22h (memory: cliente ativo segunda-sexta).

**Job de background opcional**: se uma empresa específica tem >100k linhas,
fragmentar em batches de 1000:

```sql
DO $$
DECLARE
  batch_size int := 1000;
  affected int;
BEGIN
  LOOP
    WITH cte AS (
      SELECT id FROM pedido_pagamentos WHERE empresa_id IS NULL LIMIT batch_size
    )
    UPDATE pedido_pagamentos pp
    SET empresa_id = p.empresa_id
    FROM pedidos p, cte
    WHERE pp.id = cte.id AND pp.pedido_id = p.id;
    GET DIAGNOSTICS affected = ROW_COUNT;
    EXIT WHEN affected = 0;
    PERFORM pg_sleep(0.1);
  END LOOP;
END$$;
```

#### F.2.6 M06 — `*_TightenPagamentosConstraints`

```sql
ALTER TABLE pedido_pagamentos
  ALTER COLUMN empresa_id SET NOT NULL,
  ADD CONSTRAINT chk_pedido_pagamentos_status
    CHECK (status IN ('confirmado','estornado','falhou')),
  ADD CONSTRAINT chk_pedido_pagamentos_conciliacao
    CHECK (conciliacao_tipo IN ('fisico','adquirente','nao_conciliavel')),
  ADD CONSTRAINT chk_pedido_pagamentos_valor_positivo CHECK (valor > 0),
  ADD CONSTRAINT fk_pedido_pagamentos_pagamento_original
    FOREIGN KEY (pagamento_original_id) REFERENCES pedido_pagamentos(id) ON DELETE RESTRICT,
  ADD CONSTRAINT fk_pedido_pagamentos_movimento_caixa
    FOREIGN KEY (movimento_caixa_id) REFERENCES movimentos_caixa(id) ON DELETE SET NULL;

ALTER TABLE movimentos_caixa
  ADD CONSTRAINT chk_movimentos_caixa_tipo_novo
    CHECK (tipo IN ('abertura','fechamento','entrada','saida','pagamento','estorno_pagamento','sangria','reforco','despesa'));
-- ↑ amplia enum sem quebrar valores antigos (eles continuam válidos)
```

### F.3 Backfill de SessaoCaixa para histórico

Após M02-M06 estarem aplicadas, rodar **job idempotente** de background
para criar `SessaoCaixa` retroativa para cada dia com movimentos:

```sql
-- Para cada (empresa, loja, data) com movimentos OU fechamento:
INSERT INTO sessoes_caixa (id, empresa_id, loja_id, data_operacional, aberta_em,
                            saldo_inicial, fechada_em, status)
SELECT
  gen_random_uuid(),
  base.empresa_id,
  base.loja_id,
  base.data_operacional,
  COALESCE(ab.data_movimento, base.first_mov_at),
  COALESCE(ab.valor, 0),
  CASE WHEN fc.id IS NOT NULL THEN fc.fechado_em ELSE NULL END,
  CASE WHEN fc.id IS NOT NULL THEN 'fechada' ELSE 'aberta' END
FROM (
  SELECT empresa_id, loja_id,
         DATE(data_movimento AT TIME ZONE 'UTC') AS data_operacional,
         MIN(data_movimento) AS first_mov_at
  FROM movimentos_caixa
  WHERE sessao_caixa_id IS NULL
  GROUP BY empresa_id, loja_id, DATE(data_movimento AT TIME ZONE 'UTC')
) base
LEFT JOIN movimentos_caixa ab
  ON ab.empresa_id = base.empresa_id
  AND COALESCE(ab.loja_id, '00000000-0000-0000-0000-000000000000') = COALESCE(base.loja_id, '00000000-0000-0000-0000-000000000000')
  AND DATE(ab.data_movimento AT TIME ZONE 'UTC') = base.data_operacional
  AND ab.tipo = 'abertura'
LEFT JOIN fechamentos_caixa fc
  ON fc.empresa_id = base.empresa_id
  AND COALESCE(fc.loja_id, '00000000-0000-0000-0000-000000000000') = COALESCE(base.loja_id, '00000000-0000-0000-0000-000000000000')
  AND fc.data = base.data_operacional
ON CONFLICT DO NOTHING; -- idempotente

-- Link movimentos à sessão criada
UPDATE movimentos_caixa mc
SET sessao_caixa_id = sc.id
FROM sessoes_caixa sc
WHERE mc.sessao_caixa_id IS NULL
  AND mc.empresa_id = sc.empresa_id
  AND COALESCE(mc.loja_id, '00000000-0000-0000-0000-000000000000') = COALESCE(sc.loja_id, '00000000-0000-0000-0000-000000000000')
  AND DATE(mc.data_movimento AT TIME ZONE 'UTC') = sc.data_operacional;

-- Link fechamento à sessão
UPDATE fechamentos_caixa fc
SET sessao_caixa_id = sc.id
FROM sessoes_caixa sc
WHERE fc.sessao_caixa_id IS NULL
  AND fc.empresa_id = sc.empresa_id
  AND COALESCE(fc.loja_id, '00000000-0000-0000-0000-000000000000') = COALESCE(sc.loja_id, '00000000-0000-0000-0000-000000000000')
  AND fc.data = sc.data_operacional;
```

Roda como script SQL standalone (executado via `psql` ou helper C# manualmente
após deploy). **Não bloqueia deploy** — pode rodar pós-deploy em janela
calma. Sistema novo cria SessaoCaixa nativamente; histórico fica linkado em
background.

### F.4 Cutover plan

- **Feature flag por empresa**: `TenantFeatureFlag(Feature="CaixaConciliadoV2",
  EmpresaId, Ativo)` — **entidade e tabela JÁ EXISTEM**
  (`EasyStock.Domain/Entities/TenantFeatureFlag.cs`, tabela
  `TenantFeatureFlags` da migration `20260430205554_AddGovernancaFeatures`).
  Sem migration nova. Reusar `ITenantFeatureFlagService` (se existir; caso
  contrário, criar no F1 — leitura simples).
- **Default**: nenhum registro → flag OFF. Casa da Babá recebe registro
  manual via admin UI / SQL após Felipe acompanhar staging.
- **Cliente vê**:
  - Flag OFF: aba "Caixa" antiga (atual). `RegistrarPagamentoPedidoUseCase`
    legacy intacto, sem mudança de comportamento. **Nada novo gravado em
    DB** para essa empresa (sem SessaoCaixa fantasma, sem MovimentoCaixa
    linkado).
  - Flag ON: dispatcher do `PedidosController` (F4) chama
    `ConfirmarPagamentoUseCase` novo; PWA exibe UI nova. Endpoint
    `GET /api/empresa/me/features` retorna `{ "CaixaConciliadoV2": true }`
    para o PWA decidir qual tela renderizar.
- **Comunicação ao cliente**:
  - Banner in-app 7 dias antes: "Novo Caixa chegou! Em breve liberado para
    sua empresa. Conheça aqui." → link para `/help/caixa-conciliado`.
  - Email para email cadastrado da empresa quando flag for ligada.
- **Rollback**:
  - Flag OFF reverte **comportamento backend + UI** imediatamente. Dados
    criados em sessoes_caixa/colunas novas para a empresa ficam (não
    destrutivo) mas backend nunca mais grava lá enquanto flag estiver OFF.
  - Migrations não revertidas em produção (princípio aditivo).
  - Se invariante de dados violada em produção: bug → fix → redeploy.
    Nunca `DOWN` migration em prod.
- **Teste de rollback obrigatório em F4**: ver F4 critério de pronto
  ("teste de rollback explícito").

### F.5 Validação pós-migração

Queries SQL de validação (rodar manualmente após cada deploy):

```sql
-- 1. Todo pagamento confirmado tem empresa_id
SELECT COUNT(*) FROM pedido_pagamentos WHERE empresa_id IS NULL;
-- Esperado: 0

-- 2. Nenhum pagamento com status inválido
SELECT COUNT(*) FROM pedido_pagamentos WHERE status NOT IN ('confirmado','estornado','falhou');
-- Esperado: 0

-- 3. Soma de pagamentos confirmados <= total do pedido (regra de negócio)
SELECT p.id, p.total, COALESCE(SUM(pp.valor), 0) AS pago
FROM pedidos p
LEFT JOIN pedido_pagamentos pp ON pp.pedido_id = p.id AND pp.status = 'confirmado'
GROUP BY p.id, p.total
HAVING COALESCE(SUM(pp.valor), 0) > p.total;
-- Esperado: 0 linhas (se houver, são bugs históricos — reportar manualmente)

-- 4. Nenhum movimento órfão (sessao_caixa_id NULL para registros pós-feature)
SELECT COUNT(*) FROM movimentos_caixa
WHERE sessao_caixa_id IS NULL AND criado_em > '2026-05-22'; -- data do cutover
-- Esperado: 0 após backfill

-- 5. Nenhuma SessaoCaixa aberta duplicada
SELECT empresa_id, loja_id, data_operacional, COUNT(*)
FROM sessoes_caixa
WHERE status IN ('aberta','em_conferencia')
GROUP BY empresa_id, loja_id, data_operacional
HAVING COUNT(*) > 1;
-- Esperado: 0 linhas

-- 6. Estornos têm pagamento_original_id válido
SELECT COUNT(*) FROM pedido_pagamentos
WHERE status = 'estornado' AND pagamento_original_id IS NULL
  AND pago_em > '2026-05-22'; -- estornos novos (pré-feature: tolerado)
-- Esperado: 0

-- 7. Fechamentos com hash não vazio (pós-feature)
SELECT COUNT(*) FROM fechamentos_caixa
WHERE hash_sha256 IS NULL AND fechado_em > '2026-05-22';
-- Esperado: 0
```

Empacotadas em script `scripts/sql/validacao_caixa_conciliado.sql` versionado.

---
