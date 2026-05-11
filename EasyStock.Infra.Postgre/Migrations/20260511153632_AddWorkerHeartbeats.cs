using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkerHeartbeats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "worker_heartbeats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Servico = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    UltimoTickEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Detalhe = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ItensProcessados = table.Column<int>(type: "integer", nullable: true),
                    DuracaoMs = table.Column<int>(type: "integer", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_worker_heartbeats", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_worker_heartbeats_ultimo_tick",
                table: "worker_heartbeats",
                column: "UltimoTickEm");

            migrationBuilder.CreateIndex(
                name: "ux_worker_heartbeats_servico",
                table: "worker_heartbeats",
                column: "Servico",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "worker_heartbeats");
        }
    }
}
