using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddMotivoDoTurnoAgente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Motivo",
                table: "atendimento_turnos_agente",
                type: "character varying(48)",
                maxLength: 48,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_atendimento_turnos_agente_empresa_conversa_criado",
                table: "atendimento_turnos_agente",
                columns: new[] { "EmpresaId", "ConversaId", "CriadoEm" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_atendimento_turnos_agente_empresa_conversa_criado",
                table: "atendimento_turnos_agente");

            migrationBuilder.DropColumn(
                name: "Motivo",
                table: "atendimento_turnos_agente");
        }
    }
}
