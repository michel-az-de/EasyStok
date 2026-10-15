using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppPhoneNumberIdEmpresa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WhatsAppPhoneNumberId",
                table: "empresas",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_empresas_WhatsAppPhoneNumberId",
                table: "empresas",
                column: "WhatsAppPhoneNumberId",
                unique: true,
                filter: "\"WhatsAppPhoneNumberId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_empresas_WhatsAppPhoneNumberId",
                table: "empresas");

            migrationBuilder.DropColumn(
                name: "WhatsAppPhoneNumberId",
                table: "empresas");
        }
    }
}
