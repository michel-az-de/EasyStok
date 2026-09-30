using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddAvisosStatusPedido : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InstagramUrl",
                table: "storefront",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MetadadosJson",
                table: "notif_templates",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MetadadosJson",
                table: "notif_outbox_mensagens",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InstagramUrl",
                table: "storefront");

            migrationBuilder.DropColumn(
                name: "MetadadosJson",
                table: "notif_templates");

            migrationBuilder.DropColumn(
                name: "MetadadosJson",
                table: "notif_outbox_mensagens");
        }
    }
}
