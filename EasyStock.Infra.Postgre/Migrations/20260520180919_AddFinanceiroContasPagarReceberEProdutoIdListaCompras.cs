using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddFinanceiroContasPagarReceberEProdutoIdListaCompras : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NOTA (limpeza 2026-05-22): TODA a schema financeira (contas_pagar/receber,
            // parcelas, pagamentos_parcela, categorias_financeiras, centros_custo,
            // *_alteracoes, contas_financeiras_eventos), as colunas de fatura_pagamentos e
            // os flags de configuracoes_loja (GerarContaPagar/Receber..., StatusPedido...)
            // foram REMOVIDOS desta migration: ja sao criados por
            // 20260507223432_Add_Financeiro_CapCar_Core e 20260508010749_AddPaymentOrchestrationCore.
            // Eram creates duplicados (artefato de reset do ModelSnapshot do EF) que quebravam
            // o replay do-zero (42P07/42701). Migration ja aplicada em prod nao re-roda — schema
            // de prod intacto. O unico item genuinamente novo desta migration e o ProdutoId:

            migrationBuilder.AddColumn<Guid>(
                name: "ProdutoId",
                table: "itens_lista_compras",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProdutoId",
                table: "itens_lista_compras");
        }
    }
}
