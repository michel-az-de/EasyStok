using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddContatoInformadoChatSite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VisitanteEmail",
                table: "sessoes_chat_site",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VisitanteInformadoEm",
                table: "sessoes_chat_site",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisitanteNome",
                table: "sessoes_chat_site",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisitanteTelefone",
                table: "sessoes_chat_site",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContatoEmailInformado",
                table: "atendimento_conversas",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ContatoInformadoEm",
                table: "atendimento_conversas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContatoTelefoneInformado",
                table: "atendimento_conversas",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VisitanteEmail",
                table: "sessoes_chat_site");

            migrationBuilder.DropColumn(
                name: "VisitanteInformadoEm",
                table: "sessoes_chat_site");

            migrationBuilder.DropColumn(
                name: "VisitanteNome",
                table: "sessoes_chat_site");

            migrationBuilder.DropColumn(
                name: "VisitanteTelefone",
                table: "sessoes_chat_site");

            migrationBuilder.DropColumn(
                name: "ContatoEmailInformado",
                table: "atendimento_conversas");

            migrationBuilder.DropColumn(
                name: "ContatoInformadoEm",
                table: "atendimento_conversas");

            migrationBuilder.DropColumn(
                name: "ContatoTelefoneInformado",
                table: "atendimento_conversas");
        }
    }
}
