using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class CaixaContadoEAberturaAConfirmar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ConfirmacaoPendente",
                table: "movimentos_caixa",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "DinheiroEntrouDepois",
                table: "fechamentos_caixa",
                type: "numeric(14,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DinheiroEsperado",
                table: "fechamentos_caixa",
                type: "numeric(14,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ValorContado",
                table: "fechamentos_caixa",
                type: "numeric(14,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConfirmacaoPendente",
                table: "movimentos_caixa");

            migrationBuilder.DropColumn(
                name: "DinheiroEntrouDepois",
                table: "fechamentos_caixa");

            migrationBuilder.DropColumn(
                name: "DinheiroEsperado",
                table: "fechamentos_caixa");

            migrationBuilder.DropColumn(
                name: "ValorContado",
                table: "fechamentos_caixa");
        }
    }
}
