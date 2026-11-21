using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <summary>
    /// #2581: RLS tenant_isolation nas tabelas filhas sem EmpresaId (a empresa vem do pai, que ja tem RLS).
    /// Fora desta migracao: empresas (leituras sem tenant no login) e report_runs (empresa_id NULL no AdminSaaS). REVOKE TEMP FROM PUBLIC fica para depois de confirmar que a API nao usa tabela temporaria.
    /// </summary>
    public partial class RlsTabelasFilhasBabaRo : Migration
    {
        // (filha, pai, coluna FK na filha)
        private const string Filhas = """
            ('lote_itens','lotes','LoteId'),
            ('pedidos_fornecedor_itens','pedidos_fornecedor','PedidoFornecedorId'),
            ('itens_lista_compras','listas_compras','ListaComprasId'),
            ('pedido_itens','pedidos','PedidoId'),
            ('pedido_pagamentos','pedidos','PedidoId'),
            ('pedido_eventos','pedidos','PedidoId'),
            ('itens_venda','vendas','VendaId'),
            ('cliente_telefones','clientes','ClienteId'),
            ('cliente_enderecos','clientes','ClienteId'),
            ('cardapio_item','storefront','StorefrontId'),
            ('cardapio_item_variacao','cardapio_item','CardapioItemId'),
            ('cardapio_secao','storefront','StorefrontId'),
            ('frete_zona','storefront','StorefrontId'),
            ('janela_entrega','storefront','StorefrontId'),
            ('viagem_paradas','viagens','ViagemId'),
            ('nfe_itens','nfe_documentos','NfeDocumentoId'),
            ('fatura_itens','faturas','FaturaId')
            """;

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($$"""
                DO $rls$
                DECLARE r record; pred text;
                BEGIN
                  FOR r IN SELECT * FROM (VALUES
                {{Filhas}}
                  ) AS v(filha, pai, fk) LOOP
                    pred := format($p$current_setting('app.bypass_rls', true) = 'true'
                      OR EXISTS (SELECT 1 FROM %I p WHERE p."Id" = %I.%I
                        AND p."EmpresaId" = NULLIF(current_setting('app.empresa_id', true), '')::uuid)$p$, r.pai, r.filha, r.fk);
                    -- cardapio_item_variacao: o pai (cardapio_item) nao tem EmpresaId, vale a RLS do pai
                    IF r.filha = 'cardapio_item_variacao' THEN
                      pred := format($p$current_setting('app.bypass_rls', true) = 'true'
                        OR EXISTS (SELECT 1 FROM cardapio_item p WHERE p."Id" = %I.%I)$p$, r.filha, r.fk);
                    END IF;
                    EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY', r.filha);
                    EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY', r.filha);
                    EXECUTE format('DROP POLICY IF EXISTS tenant_isolation ON %I', r.filha);
                    EXECUTE format('CREATE POLICY tenant_isolation ON %I USING (%s) WITH CHECK (%s)', r.filha, pred, pred);
                  END LOOP;
                END
                $rls$;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($$"""
                DO $rls$
                DECLARE r record;
                BEGIN
                  FOR r IN SELECT * FROM (VALUES
                {{Filhas}}
                  ) AS v(filha, pai, fk) LOOP
                    EXECUTE format('DROP POLICY IF EXISTS tenant_isolation ON %I', r.filha);
                    EXECUTE format('ALTER TABLE %I NO FORCE ROW LEVEL SECURITY', r.filha);
                    EXECUTE format('ALTER TABLE %I DISABLE ROW LEVEL SECURITY', r.filha);
                  END LOOP;
                END
                $rls$;
                """);
        }
    }
}
