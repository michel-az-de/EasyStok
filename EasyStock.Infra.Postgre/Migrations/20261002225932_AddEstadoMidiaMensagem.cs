using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddEstadoMidiaMensagem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ErroMidia",
                table: "atendimento_mensagens",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MidiaIdExterno",
                table: "atendimento_mensagens",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProximaTentativaMidiaEm",
                table: "atendimento_mensagens",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TentativasMidia",
                table: "atendimento_mensagens",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_atendimento_mensagens_proxima_tentativa_midia",
                table: "atendimento_mensagens",
                column: "ProximaTentativaMidiaEm",
                filter: "\"ProximaTentativaMidiaEm\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_atendimento_mensagens_proxima_tentativa_midia",
                table: "atendimento_mensagens");

            migrationBuilder.DropColumn(
                name: "ErroMidia",
                table: "atendimento_mensagens");

            migrationBuilder.DropColumn(
                name: "MidiaIdExterno",
                table: "atendimento_mensagens");

            migrationBuilder.DropColumn(
                name: "ProximaTentativaMidiaEm",
                table: "atendimento_mensagens");

            migrationBuilder.DropColumn(
                name: "TentativasMidia",
                table: "atendimento_mensagens");
        }
    }
}
