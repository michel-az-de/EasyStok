using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class TriagemContatoPausaEMoldesDoRobo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MensagemEncaminhamento",
                table: "configuracoes_atendimento",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "Oi, {nome}! Eu sou o atendimento automático da Casa da Baba. Já passei sua mensagem para a responsável, que te responde por aqui.");

            migrationBuilder.AddColumn<string>(
                name: "MensagemEscalada",
                table: "configuracoes_atendimento",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "Vou encaminhar sua mensagem para a equipe conferir e responder por aqui.");

            migrationBuilder.AddColumn<DateTime>(
                name: "AtendimentoAutomaticoPausadoEm",
                table: "clientes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotivoPausaAtendimento",
                table: "clientes",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MensagemEncaminhamento",
                table: "configuracoes_atendimento");

            migrationBuilder.DropColumn(
                name: "MensagemEscalada",
                table: "configuracoes_atendimento");

            migrationBuilder.DropColumn(
                name: "AtendimentoAutomaticoPausadoEm",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "MotivoPausaAtendimento",
                table: "clientes");
        }
    }
}
