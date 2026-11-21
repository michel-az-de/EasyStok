using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class ReacaoEApagarMensagemAtendimento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ApagadaEm",
                table: "atendimento_mensagens",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApagadaPorUsuarioId",
                table: "atendimento_mensagens",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reacao",
                table: "atendimento_mensagens",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApagadaEm",
                table: "atendimento_mensagens");

            migrationBuilder.DropColumn(
                name: "ApagadaPorUsuarioId",
                table: "atendimento_mensagens");

            migrationBuilder.DropColumn(
                name: "Reacao",
                table: "atendimento_mensagens");
        }
    }
}
