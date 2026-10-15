using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddEnderecoEntregaPedido : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BairroEntrega",
                table: "pedidos",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CepEntrega",
                table: "pedidos",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EnderecoConfirmadoEm",
                table: "pedidos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EnderecoEntrega",
                table: "pedidos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "EntregaLat",
                table: "pedidos",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "EntregaLng",
                table: "pedidos",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BairroEntrega",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "CepEntrega",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "EnderecoConfirmadoEm",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "EnderecoEntrega",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "EntregaLat",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "EntregaLng",
                table: "pedidos");
        }
    }
}
