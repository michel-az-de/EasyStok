using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddInicioPrevistoPedido : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "atraso_notificado_em",
                table: "pedidos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "inicio_previsto_em",
                table: "pedidos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_pedidos_atraso_pendente",
                table: "pedidos",
                column: "inicio_previsto_em",
                filter: "\"Status\" = 'aguardando' AND atraso_notificado_em IS NULL AND inicio_previsto_em IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_pedidos_atraso_pendente",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "atraso_notificado_em",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "inicio_previsto_em",
                table: "pedidos");
        }
    }
}
