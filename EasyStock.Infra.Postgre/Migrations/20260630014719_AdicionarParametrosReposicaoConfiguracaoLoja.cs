using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarParametrosReposicaoConfiguracaoLoja : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DiasCoberturaAlvo",
                table: "configuracoes_loja",
                type: "integer",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.AddColumn<int>(
                name: "LeadTimePadraoDias",
                table: "configuracoes_loja",
                type: "integer",
                nullable: false,
                defaultValue: 7);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiasCoberturaAlvo",
                table: "configuracoes_loja");

            migrationBuilder.DropColumn(
                name: "LeadTimePadraoDias",
                table: "configuracoes_loja");
        }
    }
}
