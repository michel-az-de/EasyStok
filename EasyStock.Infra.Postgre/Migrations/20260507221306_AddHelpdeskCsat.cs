using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <summary>
    /// Adiciona suporte a CSAT (Customer Satisfaction) em admin_tickets:
    /// nota 1..5 + comentario opcional + carimbo de avaliacao + carimbo do
    /// convite (idempotencia para nao reenviar). Check constraint garante a
    /// faixa 1..5 mesmo se a app for bypassada.
    /// </summary>
    public partial class AddHelpdeskCsat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AvaliadoEm",
                table: "admin_tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ComentarioCsat",
                table: "admin_tickets",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ConviteCsatEnviadoEm",
                table: "admin_tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NotaCsat",
                table: "admin_tickets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_admin_tickets_nota_csat_range",
                table: "admin_tickets",
                sql: "\"NotaCsat\" IS NULL OR (\"NotaCsat\" BETWEEN 1 AND 5)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_admin_tickets_nota_csat_range",
                table: "admin_tickets");

            migrationBuilder.DropColumn(
                name: "AvaliadoEm",
                table: "admin_tickets");

            migrationBuilder.DropColumn(
                name: "ComentarioCsat",
                table: "admin_tickets");

            migrationBuilder.DropColumn(
                name: "ConviteCsatEnviadoEm",
                table: "admin_tickets");

            migrationBuilder.DropColumn(
                name: "NotaCsat",
                table: "admin_tickets");
        }
    }
}
