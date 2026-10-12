using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddLinhaETempoPreparoCardapioItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LinhaSnapshot",
                table: "pedido_itens",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstrucaoFinalizacao",
                table: "cardapio_item",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Linha",
                table: "cardapio_item",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TempoPreparoMinutos",
                table: "cardapio_item",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LinhaSnapshot",
                table: "pedido_itens");

            migrationBuilder.DropColumn(
                name: "InstrucaoFinalizacao",
                table: "cardapio_item");

            migrationBuilder.DropColumn(
                name: "Linha",
                table: "cardapio_item");

            migrationBuilder.DropColumn(
                name: "TempoPreparoMinutos",
                table: "cardapio_item");
        }
    }
}
