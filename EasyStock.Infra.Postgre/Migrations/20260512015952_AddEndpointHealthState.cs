using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddEndpointHealthState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "endpoint_health_state",
                columns: table => new
                {
                    endpoint_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    consecutive_failures = table.Column<int>(type: "integer", nullable: false),
                    last_check_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_failure_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_failure_message = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    last_alerted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_alerted_ticket_id = table.Column<Guid>(type: "uuid", nullable: true),
                    criado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_endpoint_health_state", x => x.endpoint_name);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "endpoint_health_state");
        }
    }
}
