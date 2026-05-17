using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddCscNoEmpresaConfiguracaoFiscal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CscId",
                table: "empresa_configuracao_fiscal",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CscToken",
                table: "empresa_configuracao_fiscal",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CscId",
                table: "empresa_configuracao_fiscal");

            migrationBuilder.DropColumn(
                name: "CscToken",
                table: "empresa_configuracao_fiscal");
        }
    }
}
