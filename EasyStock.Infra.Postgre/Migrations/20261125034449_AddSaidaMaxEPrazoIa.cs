using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddSaidaMaxEPrazoIa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "prazo_segundos",
                table: "provedor_ia",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "saida_max",
                table: "modelo_ia",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_provedor_ia_prazo_segundos",
                table: "provedor_ia",
                sql: "prazo_segundos IS NULL OR (prazo_segundos >= 5 AND prazo_segundos <= 300)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_modelo_ia_saida_max",
                table: "modelo_ia",
                sql: "saida_max IS NULL OR (saida_max >= 256 AND saida_max <= 200000)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_provedor_ia_prazo_segundos",
                table: "provedor_ia");

            migrationBuilder.DropCheckConstraint(
                name: "ck_modelo_ia_saida_max",
                table: "modelo_ia");

            migrationBuilder.DropColumn(
                name: "prazo_segundos",
                table: "provedor_ia");

            migrationBuilder.DropColumn(
                name: "saida_max",
                table: "modelo_ia");
        }
    }
}
