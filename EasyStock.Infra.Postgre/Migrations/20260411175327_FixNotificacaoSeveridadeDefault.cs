using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class FixNotificacaoSeveridadeDefault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Severidade",
                table: "notificacoes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Media");

            migrationBuilder.AddColumn<string>(
                name: "Titulo",
                table: "notificacoes",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_notificacoes_EmpresaId_Severidade_Lida",
                table: "notificacoes",
                columns: new[] { "EmpresaId", "Severidade", "Lida" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_notificacoes_EmpresaId_Severidade_Lida",
                table: "notificacoes");

            migrationBuilder.DropColumn(
                name: "Severidade",
                table: "notificacoes");

            migrationBuilder.DropColumn(
                name: "Titulo",
                table: "notificacoes");
        }
    }
}
