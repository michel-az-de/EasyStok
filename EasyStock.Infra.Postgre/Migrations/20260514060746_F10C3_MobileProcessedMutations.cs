using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class F10C3_MobileProcessedMutations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mobile_processed_mutations",
                columns: table => new
                {
                    MutationId = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    DeviceId = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ResponseMeta = table.Column<string>(type: "text", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mobile_processed_mutations", x => new { x.MutationId, x.DeviceId });
                });

            migrationBuilder.CreateIndex(
                name: "ix_mpm_retention",
                table: "mobile_processed_mutations",
                columns: new[] { "EmpresaId", "CriadoEm" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mobile_processed_mutations");
        }
    }
}
