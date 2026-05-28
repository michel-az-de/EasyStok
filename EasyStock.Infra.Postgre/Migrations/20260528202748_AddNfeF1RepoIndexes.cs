using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <summary>
    /// Issue #290 — defesa em DB contra duplicacao de NFC-e por falha do cache
    /// HTTP-level de idempotencia. Adiciona <c>IdempotencyKey</c> em
    /// <c>nfe_documentos</c> + indice partial unique <c>(EmpresaId, IdempotencyKey)</c>
    /// WHERE IdempotencyKey IS NOT NULL. Backfill mantem NULL — indice partial
    /// nao indexa linhas legadas, zero impacto.
    /// </summary>
    public partial class AddNfeF1RepoIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "nfe_documentos",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_nfe_documentos_empresa_idempotency",
                table: "nfe_documentos",
                columns: new[] { "EmpresaId", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_nfe_documentos_empresa_idempotency",
                table: "nfe_documentos");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "nfe_documentos");
        }
    }
}
