using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddResultadoPedidoAvaliacao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "Estrelas",
                table: "pedido_avaliacao",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "Resultado",
                table: "pedido_avaliacao",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Resultado",
                table: "pedido_avaliacao");

            migrationBuilder.AlterColumn<int>(
                name: "Estrelas",
                table: "pedido_avaliacao",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
