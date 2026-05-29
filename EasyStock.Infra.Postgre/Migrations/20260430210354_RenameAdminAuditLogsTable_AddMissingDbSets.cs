using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class RenameAdminAuditLogsTable_AddMissingDbSets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_AdminAuditLogs",
                table: "AdminAuditLogs");

            migrationBuilder.RenameTable(
                name: "AdminAuditLogs",
                newName: "admin_audit_logs");

            migrationBuilder.RenameIndex(
                name: "IX_AdminAuditLogs_TenantId",
                table: "admin_audit_logs",
                newName: "IX_admin_audit_logs_TenantId");

            migrationBuilder.RenameIndex(
                name: "IX_AdminAuditLogs_CriadoEm",
                table: "admin_audit_logs",
                newName: "IX_admin_audit_logs_CriadoEm");

            migrationBuilder.AddPrimaryKey(
                name: "PK_admin_audit_logs",
                table: "admin_audit_logs",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "CobrancasAssinatura",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssinaturaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Txid = table.Column<string>(type: "text", nullable: false),
                    Valor = table.Column<decimal>(type: "numeric", nullable: false),
                    PixCopiaCola = table.Column<string>(type: "text", nullable: false),
                    QrCodeBase64 = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiracaoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PagoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CobrancasAssinatura", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CobrancasAssinatura_assinaturas_empresa_AssinaturaId",
                        column: x => x.AssinaturaId,
                        principalTable: "assinaturas_empresa",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CobrancasAssinatura_empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CobrancasAssinatura_AssinaturaId",
                table: "CobrancasAssinatura",
                column: "AssinaturaId");

            migrationBuilder.CreateIndex(
                name: "IX_CobrancasAssinatura_EmpresaId",
                table: "CobrancasAssinatura",
                column: "EmpresaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CobrancasAssinatura");

            migrationBuilder.DropPrimaryKey(
                name: "PK_admin_audit_logs",
                table: "admin_audit_logs");

            migrationBuilder.RenameTable(
                name: "admin_audit_logs",
                newName: "AdminAuditLogs");

            migrationBuilder.RenameIndex(
                name: "IX_admin_audit_logs_TenantId",
                table: "AdminAuditLogs",
                newName: "IX_AdminAuditLogs_TenantId");

            migrationBuilder.RenameIndex(
                name: "IX_admin_audit_logs_CriadoEm",
                table: "AdminAuditLogs",
                newName: "IX_AdminAuditLogs_CriadoEm");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AdminAuditLogs",
                table: "AdminAuditLogs",
                column: "Id");
        }
    }
}
