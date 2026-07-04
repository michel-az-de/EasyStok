using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class RestringeUxMovEstoqueReferenciaACompra : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_mov_estoque_referencia",
                table: "movimentacoes_estoque");

            migrationBuilder.CreateIndex(
                name: "ux_mov_estoque_referencia",
                table: "movimentacoes_estoque",
                columns: new[] { "EmpresaId", "ProdutoId", "Natureza", "DocumentoReferencia" },
                unique: true,
                filter: "\"DocumentoReferencia\" IS NOT NULL AND \"Natureza\" = 'Compra'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_mov_estoque_referencia",
                table: "movimentacoes_estoque");

            migrationBuilder.CreateIndex(
                name: "ux_mov_estoque_referencia",
                table: "movimentacoes_estoque",
                columns: new[] { "EmpresaId", "ProdutoId", "Natureza", "DocumentoReferencia" },
                unique: true,
                filter: "\"DocumentoReferencia\" IS NOT NULL");
        }
    }
}
