using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddVendaReportingColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // FK para o vendedor que realizou a venda — nullable, sem cascade delete.
            migrationBuilder.AddColumn<Guid>(
                name: "VendedorId",
                table: "vendas",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_vendas_VendedorId",
                table: "vendas",
                column: "VendedorId");

            migrationBuilder.AddForeignKey(
                name: "FK_vendas_usuarios_VendedorId",
                table: "vendas",
                column: "VendedorId",
                principalTable: "usuarios",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Snapshot da forma de pagamento principal.
            migrationBuilder.AddColumn<string>(
                name: "FormaPagamentoPrincipal",
                table: "vendas",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // Subtotal (soma bruta dos itens antes de descontos) — snapshot imutável.
            migrationBuilder.AddColumn<decimal>(
                name: "Subtotal",
                table: "vendas",
                type: "numeric(18,2)",
                nullable: true);

            // Valor total de descontos aplicados — snapshot imutável.
            migrationBuilder.AddColumn<decimal>(
                name: "ValorDesconto",
                table: "vendas",
                type: "numeric(18,2)",
                nullable: true);

            // Índice para o relatório vendas.por-periodo (filtra por empresa_id + data_venda).
            // CREATE INDEX CONCURRENTLY não pode rodar em transação — usar Sql com suppressTransaction.
            migrationBuilder.Sql(
                @"CREATE INDEX CONCURRENTLY IF NOT EXISTS ""IX_vendas_relatorio""
                  ON public.vendas (""EmpresaId"", ""DataVenda"")
                  INCLUDE (""LojaId"", ""ValorTotal"");",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                @"DROP INDEX CONCURRENTLY IF EXISTS ""IX_vendas_relatorio"";",
                suppressTransaction: true);

            migrationBuilder.DropForeignKey(
                name: "FK_vendas_usuarios_VendedorId",
                table: "vendas");

            migrationBuilder.DropIndex(
                name: "IX_vendas_VendedorId",
                table: "vendas");

            migrationBuilder.DropColumn(name: "VendedorId", table: "vendas");
            migrationBuilder.DropColumn(name: "FormaPagamentoPrincipal", table: "vendas");
            migrationBuilder.DropColumn(name: "Subtotal", table: "vendas");
            migrationBuilder.DropColumn(name: "ValorDesconto", table: "vendas");
        }
    }
}
