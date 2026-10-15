using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddArquivoValidacaoNovidadeCardapioItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ArquivadoEm",
                table: "cardapio_item",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EmValidacao",
                table: "cardapio_item",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "NovidadeAte",
                table: "cardapio_item",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArquivadoEm",
                table: "cardapio_item");

            migrationBuilder.DropColumn(
                name: "EmValidacao",
                table: "cardapio_item");

            migrationBuilder.DropColumn(
                name: "NovidadeAte",
                table: "cardapio_item");
        }
    }
}
