using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AdicionarIdempotencyKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "idempotency_keys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    EmpresaId = table.Column<Guid>(type: "uuid", nullable: false),
                    MetodoRecurso = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    HttpStatus = table.Column<int>(type: "integer", nullable: false),
                    RespostaJson = table.Column<string>(type: "text", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiraEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency_keys", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_expira",
                table: "idempotency_keys",
                column: "ExpiraEm");

            migrationBuilder.CreateIndex(
                name: "ux_idempotency_key_empresa_recurso",
                table: "idempotency_keys",
                columns: new[] { "Key", "EmpresaId", "MetodoRecurso" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "idempotency_keys");
        }
    }
}
