\set empresa '\'ecc90223-24f8-4689-bed6-336a57bb4f21\''

\echo '=== ANTES ==='
SELECT COUNT(*) AS qtd FROM venda_alteracoes va
JOIN vendas v ON v."Id" = va."VendaId" WHERE v."EmpresaId" = :empresa;

\echo ''
\echo '=== INSERT auditoria pras vendas ja criadas pelo mobile sync ==='
INSERT INTO venda_alteracoes ("Id", "VendaId", "AlteradoPorUserId", "AlteradoPorNome", "Campo", "ValorAntigo", "ValorNovo", "AlteradoEm", "Origem")
SELECT
  gen_random_uuid(),
  v."Id",
  NULL,
  COALESCE(mo.last_operator_name, 'Sync mobile'),
  'criada',
  NULL,
  'Pedido mobile ' || mo."Id" || ' entregue. Total=' || v."ValorTotal",
  v."CriadoEm",
  'mobile'
FROM vendas v
LEFT JOIN mobile_orders mo ON mo.erp_venda_id = v."Id"
WHERE v."EmpresaId" = :empresa
  AND NOT EXISTS (SELECT 1 FROM venda_alteracoes va WHERE va."VendaId" = v."Id");

\echo ''
\echo '=== DEPOIS ==='
SELECT v."Id" AS venda_id, va."Campo", va."AlteradoPorNome", va."Origem", va."ValorNovo"
FROM vendas v
LEFT JOIN venda_alteracoes va ON va."VendaId" = v."Id"
WHERE v."EmpresaId" = :empresa
ORDER BY v."CriadoEm" DESC LIMIT 5;
