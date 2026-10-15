using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppPlataformaN6 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Remetente",
                table: "notif_outbox_mensagens",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Loja");

            migrationBuilder.CreateTable(
                name: "notif_templates_meta_estado",
                columns: table => new
                {
                    Nome = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Idioma = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CategoriaAtual = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notif_templates_meta_estado", x => new { x.Nome, x.Idioma });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notif_templates_meta_estado");

            migrationBuilder.DropColumn(
                name: "Remetente",
                table: "notif_outbox_mensagens");
        }
    }
}
