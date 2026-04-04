using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarMetricasOperacionaisEstoque : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DiasSemMovimentacao",
                table: "itens_estoque",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PrevisaoZeramentoDias",
                table: "itens_estoque",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QuantidadeMinima",
                table: "itens_estoque",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<decimal>(
                name: "VelocidadeSaidaDiaria",
                table: "itens_estoque",
                type: "numeric(10,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiasSemMovimentacao",
                table: "itens_estoque");

            migrationBuilder.DropColumn(
                name: "PrevisaoZeramentoDias",
                table: "itens_estoque");

            migrationBuilder.DropColumn(
                name: "QuantidadeMinima",
                table: "itens_estoque");

            migrationBuilder.DropColumn(
                name: "VelocidadeSaidaDiaria",
                table: "itens_estoque");
        }
    }
}
