-- Reset dos dados operacionais da Casa da Babá (empresaId=ecc90223...).
-- Mantém: empresa, lojas, usuários, plano, categorias, devices APK pareados (não-probe).
-- Apaga: produtos, clientes, lotes, pedidos, vendas, caixa, mobile_* operacional + devices probe.
-- Ordem respeita FK (filhos antes dos pais).
-- Tabelas web usam "PascalCase" (aspas), mobile_* usam snake_case.

\set ON_ERROR_STOP on
\set empresa '\'ecc90223-24f8-4689-bed6-336a57bb4f21\''

BEGIN;

-- ── Cadeia Lote (web) ────────────────────────────────────────────────────
DELETE FROM lote_etiquetas WHERE "LoteId" IN (SELECT "Id" FROM lotes WHERE "EmpresaId" = :empresa);
DELETE FROM lote_itens     WHERE "LoteId" IN (SELECT "Id" FROM lotes WHERE "EmpresaId" = :empresa);
DELETE FROM lotes          WHERE "EmpresaId" = :empresa;

-- ── Mobile batches ───────────────────────────────────────────────────────
DELETE FROM mobile_batch_items WHERE batch_id IN (SELECT "Id" FROM mobile_batches WHERE empresa_id = :empresa);
DELETE FROM mobile_batches     WHERE empresa_id = :empresa;

-- ── Cadeia Pedido (web) ──────────────────────────────────────────────────
DELETE FROM pedido_pagamentos WHERE "PedidoId" IN (SELECT "Id" FROM pedidos WHERE "EmpresaId" = :empresa);
DELETE FROM pedido_eventos    WHERE "PedidoId" IN (SELECT "Id" FROM pedidos WHERE "EmpresaId" = :empresa);
DELETE FROM pedido_itens      WHERE "PedidoId" IN (SELECT "Id" FROM pedidos WHERE "EmpresaId" = :empresa);
DELETE FROM pedidos           WHERE "EmpresaId" = :empresa;

-- ── Mobile orders ────────────────────────────────────────────────────────
DELETE FROM mobile_order_items WHERE order_id IN (SELECT "Id" FROM mobile_orders WHERE empresa_id = :empresa);
DELETE FROM mobile_orders      WHERE empresa_id = :empresa;

-- ── Movimentações de estoque (referenciam Venda/Produto) — antes das vendas
DELETE FROM movimentacao_estoque_alteracoes WHERE "MovimentacaoEstoqueId" IN (
  SELECT "Id" FROM movimentacoes_estoque WHERE "EmpresaId" = :empresa
);
DELETE FROM movimentacoes_estoque WHERE "EmpresaId" = :empresa;

-- ── Vendas (geradas pelos pedidos entregues) ─────────────────────────────
DELETE FROM venda_alteracoes WHERE "VendaId" IN (SELECT "Id" FROM vendas WHERE "EmpresaId" = :empresa);
DELETE FROM itens_venda      WHERE "VendaId" IN (SELECT "Id" FROM vendas WHERE "EmpresaId" = :empresa);
DELETE FROM vendas           WHERE "EmpresaId" = :empresa;

-- ── Caixa ────────────────────────────────────────────────────────────────
DELETE FROM fechamentos_caixa   WHERE "EmpresaId" = :empresa;
DELETE FROM movimentos_caixa    WHERE "EmpresaId" = :empresa;
DELETE FROM mobile_cash_entries WHERE empresa_id = :empresa;

-- ── Cadeia Produto (web) ─────────────────────────────────────────────────
-- itens_estoque referencia produto_variacoes (FK ProdutoVariacaoId), entao
-- precisa morrer ANTES das variacoes.
DELETE FROM produto_alteracoes      WHERE "EmpresaId" = :empresa;
DELETE FROM produto_caracteristicas WHERE "EmpresaId" = :empresa;
DELETE FROM produto_embalagens      WHERE "EmpresaId" = :empresa;
DELETE FROM itens_estoque           WHERE "EmpresaId" = :empresa;
DELETE FROM produto_variacoes       WHERE "EmpresaId" = :empresa;
DELETE FROM produtos                WHERE "EmpresaId" = :empresa;
DELETE FROM mobile_products         WHERE empresa_id = :empresa;

-- ── Cadeia Cliente (web) ─────────────────────────────────────────────────
DELETE FROM cliente_alteracoes WHERE "ClienteId" IN (SELECT "Id" FROM clientes WHERE "EmpresaId" = :empresa);
DELETE FROM cliente_documentos WHERE "ClienteId" IN (SELECT "Id" FROM clientes WHERE "EmpresaId" = :empresa);
DELETE FROM cliente_telefones  WHERE "ClienteId" IN (SELECT "Id" FROM clientes WHERE "EmpresaId" = :empresa);
DELETE FROM cliente_enderecos  WHERE "ClienteId" IN (SELECT "Id" FROM clientes WHERE "EmpresaId" = :empresa);
DELETE FROM clientes           WHERE "EmpresaId" = :empresa;
DELETE FROM mobile_clients     WHERE empresa_id = :empresa;

-- ── Devices probe ────────────────────────────────────────────────────────
DELETE FROM mobile_device_commands WHERE device_id IN (
  SELECT "Id" FROM mobile_devices
  WHERE empresa_id = :empresa AND ("Id" LIKE 'probe-%' OR "Id" LIKE 'backfill-%')
);
DELETE FROM mobile_device_backups WHERE device_id IN (
  SELECT "Id" FROM mobile_devices
  WHERE empresa_id = :empresa AND ("Id" LIKE 'probe-%' OR "Id" LIKE 'backfill-%')
);
DELETE FROM mobile_devices
WHERE empresa_id = :empresa AND ("Id" LIKE 'probe-%' OR "Id" LIKE 'backfill-%');

-- ── Listas de compras ────────────────────────────────────────────────────
DELETE FROM listas_compras WHERE "EmpresaId" = :empresa;

COMMIT;

-- Snapshot pos-reset
SELECT 'mobile_orders'    AS tabela, COUNT(*) FROM mobile_orders    WHERE empresa_id = :empresa
UNION ALL SELECT 'mobile_products',   COUNT(*) FROM mobile_products    WHERE empresa_id = :empresa
UNION ALL SELECT 'mobile_clients',    COUNT(*) FROM mobile_clients     WHERE empresa_id = :empresa
UNION ALL SELECT 'mobile_batches',    COUNT(*) FROM mobile_batches     WHERE empresa_id = :empresa
UNION ALL SELECT 'mobile_cash',       COUNT(*) FROM mobile_cash_entries WHERE empresa_id = :empresa
UNION ALL SELECT 'mobile_devices',    COUNT(*) FROM mobile_devices     WHERE empresa_id = :empresa
UNION ALL SELECT 'pedidos',           COUNT(*) FROM pedidos            WHERE "EmpresaId" = :empresa
UNION ALL SELECT 'produtos',          COUNT(*) FROM produtos           WHERE "EmpresaId" = :empresa
UNION ALL SELECT 'clientes',          COUNT(*) FROM clientes           WHERE "EmpresaId" = :empresa
UNION ALL SELECT 'lotes',             COUNT(*) FROM lotes              WHERE "EmpresaId" = :empresa
UNION ALL SELECT 'vendas',            COUNT(*) FROM vendas             WHERE "EmpresaId" = :empresa
UNION ALL SELECT 'movimentos_caixa',  COUNT(*) FROM movimentos_caixa   WHERE "EmpresaId" = :empresa;
