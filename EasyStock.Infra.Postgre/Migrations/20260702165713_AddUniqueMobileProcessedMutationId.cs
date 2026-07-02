using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueMobileProcessedMutationId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Dedup defensivo ANTES do indice unico: se prod ja tem a mesma MutationId com
            // DeviceIds diferentes (o bug #789), o CREATE UNIQUE falharia. Mantem a linha
            // mais antiga por MutationId (CriadoEm; DeviceId como desempate deterministico).
            migrationBuilder.Sql("""
                DELETE FROM mobile_processed_mutations a
                USING mobile_processed_mutations b
                WHERE a."MutationId" = b."MutationId"
                  AND (a."CriadoEm" > b."CriadoEm"
                       OR (a."CriadoEm" = b."CriadoEm" AND a."DeviceId" > b."DeviceId"));
                """);

            // Idempotente (ADR-0024 §3): banco novo (from-zero) e reconciliacao convergem.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_mpm_mutation_id ON mobile_processed_mutations (\"MutationId\");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_mpm_mutation_id;");
        }
    }
}
