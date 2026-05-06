using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminNotaTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "admin_notas_tenant",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AutorAdminId = table.Column<Guid>(type: "uuid", nullable: false),
                    AutorEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Texto = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Tipo = table.Column<int>(type: "integer", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExcluidoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin_notas_tenant", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_admin_notas_tenant_TenantId_CriadoEm",
                table: "admin_notas_tenant",
                columns: new[] { "TenantId", "CriadoEm" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "admin_notas_tenant");
        }
    }
}
