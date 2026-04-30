using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLogCupomTrial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CupomCodigo",
                table: "assinaturas_empresa",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DescontoAplicado",
                table: "assinaturas_empresa",
                type: "numeric(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TrialFim",
                table: "assinaturas_empresa",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LidoPeloAdmin",
                table: "admin_ticket_mensagens",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AdminAuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdminEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Acao = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Detalhes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Cupons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Codigo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TipoDesconto = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    LimiteUsos = table.Column<int>(type: "integer", nullable: true),
                    TotalUsos = table.Column<int>(type: "integer", nullable: false),
                    ValidoAte = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PlanoId = table.Column<Guid>(type: "uuid", nullable: true),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cupons", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdminAuditLogs_CriadoEm",
                table: "AdminAuditLogs",
                column: "CriadoEm");

            migrationBuilder.CreateIndex(
                name: "IX_AdminAuditLogs_TenantId",
                table: "AdminAuditLogs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Cupons_Ativo",
                table: "Cupons",
                column: "Ativo");

            migrationBuilder.CreateIndex(
                name: "IX_Cupons_Codigo",
                table: "Cupons",
                column: "Codigo",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminAuditLogs");

            migrationBuilder.DropTable(
                name: "Cupons");

            migrationBuilder.DropColumn(
                name: "CupomCodigo",
                table: "assinaturas_empresa");

            migrationBuilder.DropColumn(
                name: "DescontoAplicado",
                table: "assinaturas_empresa");

            migrationBuilder.DropColumn(
                name: "TrialFim",
                table: "assinaturas_empresa");

            migrationBuilder.DropColumn(
                name: "LidoPeloAdmin",
                table: "admin_ticket_mensagens");
        }
    }
}
