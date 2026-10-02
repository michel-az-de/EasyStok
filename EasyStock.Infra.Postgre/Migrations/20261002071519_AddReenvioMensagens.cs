using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddReenvioMensagens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ProximoReenvioEm",
                table: "atendimento_mensagens",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TentativasEnvio",
                table: "atendimento_mensagens",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_atendimento_mensagens_proximo_reenvio",
                table: "atendimento_mensagens",
                column: "ProximoReenvioEm",
                filter: "\"ProximoReenvioEm\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_atendimento_mensagens_proximo_reenvio",
                table: "atendimento_mensagens");

            migrationBuilder.DropColumn(
                name: "ProximoReenvioEm",
                table: "atendimento_mensagens");

            migrationBuilder.DropColumn(
                name: "TentativasEnvio",
                table: "atendimento_mensagens");
        }
    }
}
