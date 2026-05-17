SELECT table_name, column_name
FROM information_schema.columns
WHERE table_schema='public'
  AND table_name IN (
    'lotes','lote_itens','lote_etiquetas',
    'mobile_batches','mobile_batch_items','mobile_orders','mobile_order_items',
    'mobile_products','mobile_clients','mobile_cash_entries',
    'mobile_devices','mobile_device_commands','mobile_device_backups',
    'pedidos','pedido_itens','pedido_eventos','pedido_pagamentos',
    'vendas','itens_venda','venda_alteracoes',
    'movimentos_caixa','fechamentos_caixa',
    'produtos','produto_alteracoes','produto_caracteristicas','produto_embalagens','produto_variacoes',
    'itens_estoque','movimentacoes_estoque','movimentacao_estoque_alteracoes',
    'clientes','cliente_enderecos','cliente_telefones','cliente_documentos','cliente_alteracoes',
    'listas_compras'
  )
  AND (column_name ILIKE '%id%' OR column_name ILIKE '%empresa%' OR column_name ILIKE '%loja%')
ORDER BY table_name, ordinal_position;
