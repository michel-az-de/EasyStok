using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddMotivoEntidadeAfetadaToAdminAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EntidadeAfetadaId",
                table: "admin_audit_logs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Motivo",
                table: "admin_audit_logs",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_admin_audit_logs_TenantId_EntidadeAfetadaId",
                table: "admin_audit_logs",
                columns: new[] { "TenantId", "EntidadeAfetadaId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_admin_audit_logs_TenantId_EntidadeAfetadaId",
                table: "admin_audit_logs");

            migrationBuilder.DropColumn(
                name: "EntidadeAfetadaId",
                table: "admin_audit_logs");

            migrationBuilder.DropColumn(
                name: "Motivo",
                table: "admin_audit_logs");
        }
    }
}
