using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class IndicesListaPedidosEVagaPorPedido : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_vaga_ocupada_pedido_id",
                table: "vaga_ocupada",
                column: "PedidoId");

            migrationBuilder.CreateIndex(
                name: "ix_pedidos_empresa_status_criado_em",
                table: "pedidos",
                columns: new[] { "EmpresaId", "Status", "CriadoEm" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_vaga_ocupada_pedido_id",
                table: "vaga_ocupada");

            migrationBuilder.DropIndex(
                name: "ix_pedidos_empresa_status_criado_em",
                table: "pedidos");
        }
    }
}
