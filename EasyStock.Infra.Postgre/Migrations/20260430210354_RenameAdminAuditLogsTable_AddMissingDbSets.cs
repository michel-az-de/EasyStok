using System;
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

            migrationBuilder.CreateTable(
                name: "ConfiguracoesSistema",
                columns: table => new
                {
                    Chave = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Valor = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Descricao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoPor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfiguracoesSistema", x => x.Chave);
                });

            migrationBuilder.CreateTable(
                name: "TenantFeatureFlags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Feature = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoPor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantFeatureFlags", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CobrancasAssinatura_AssinaturaId",
                table: "CobrancasAssinatura",
                column: "AssinaturaId");

            migrationBuilder.CreateIndex(
                name: "IX_CobrancasAssinatura_EmpresaId",
                table: "CobrancasAssinatura",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantFeatureFlags_EmpresaId_Feature",
                table: "TenantFeatureFlags",
                columns: new[] { "EmpresaId", "Feature" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CobrancasAssinatura");

            migrationBuilder.DropTable(
                name: "ConfiguracoesSistema");

            migrationBuilder.DropTable(
                name: "TenantFeatureFlags");

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
