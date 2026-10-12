using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddIndiceModeloPendenteAtendimento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_atendimento_mensagens_modelo_pendente",
                table: "atendimento_mensagens",
                column: "EnviadaEm",
                filter: "\"ModeloJson\" IS NOT NULL AND \"Status\" = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_atendimento_mensagens_modelo_pendente",
                table: "atendimento_mensagens");
        }
    }
}
