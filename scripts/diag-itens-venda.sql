\set empresa '\'ecc90223-24f8-4689-bed6-336a57bb4f21\''
\set loja '\'325692ab-d839-4c29-8c74-297f0eb6c305\''

\echo '=== Vendas criadas (resumo) ==='
SELECT v."Id", v."DataVenda", v."ValorTotal_Valor", v."Observacoes",
  (SELECT COUNT(*) FROM itens_venda iv WHERE iv."VendaId" = v."Id") AS qtd_itens
FROM vendas v WHERE v."EmpresaId" = :empresa
ORDER BY v."DataVenda" DESC;

\echo ''
\echo '=== Mobile orders entregues -> produtos referenciados ==='
SELECT o."Id" AS mobile_order_id, o.erp_pedido_id, o.erp_venda_id,
  oi.product_id AS mobile_product_id,
  p.erp_product_id,
  (SELECT COUNT(*) FROM itens_estoque ie
     WHERE ie."EmpresaId" = :empresa
       AND ie."ProdutoId" = p.erp_product_id
       AND (ie."LojaId" IS NULL OR ie."LojaId" = :loja)) AS itens_estoque_match
FROM mobile_orders o
JOIN mobile_order_items oi ON oi.order_id = o."Id"
LEFT JOIN mobile_products p ON p."Id" = oi.product_id
WHERE o.empresa_id = :empresa AND o."Status" = 'entregue'
ORDER BY o."Id" DESC
LIMIT 50;

\echo ''
\echo '=== ItensEstoque dispon na empresa/loja ==='
SELECT "Id", "ProdutoId", "LojaId", "Quantidade_Valor"
FROM itens_estoque WHERE "EmpresaId" = :empresa
ORDER BY "ProdutoId"
LIMIT 50;
