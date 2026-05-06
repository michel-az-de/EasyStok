using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminAcessoPiiLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "admin_acessos_pii_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdminEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    EntidadeTipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    EntidadeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Campo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Motivo = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin_acessos_pii_logs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_admin_acessos_pii_logs_AdminEmail",
                table: "admin_acessos_pii_logs",
                column: "AdminEmail");

            migrationBuilder.CreateIndex(
                name: "IX_admin_acessos_pii_logs_CriadoEm",
                table: "admin_acessos_pii_logs",
                column: "CriadoEm");

            migrationBuilder.CreateIndex(
                name: "IX_admin_acessos_pii_logs_TenantId_EntidadeId",
                table: "admin_acessos_pii_logs",
                columns: new[] { "TenantId", "EntidadeId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "admin_acessos_pii_logs");
        }
    }
}
