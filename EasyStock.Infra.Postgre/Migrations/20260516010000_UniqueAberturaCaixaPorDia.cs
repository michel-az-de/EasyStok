using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class UniqueAberturaCaixaPorDia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Sanity: estorna aberturas duplicadas pré-existentes mantendo a primeira de cada dia/empresa/loja.
            // Sem isso, o UNIQUE INDEX abaixo não consegue ser criado em bases que já têm o problema.
            migrationBuilder.Sql("""
                WITH ranked AS (
                    SELECT "Id",
                           ROW_NUMBER() OVER (
                               PARTITION BY "EmpresaId", COALESCE("LojaId", '00000000-0000-0000-0000-000000000000'::uuid), DATE("DataMovimento")
                               ORDER BY "CriadoEm"
                           ) AS rn
                    FROM movimentos_caixa
                    WHERE "Tipo" = 'abertura' AND "EstornadoEm" IS NULL
                )
                UPDATE movimentos_caixa m
                SET "EstornadoEm" = NOW(),
                    "MotivoEstorno" = COALESCE(NULLIF(m."MotivoEstorno", ''), '') ||
                                      CASE WHEN m."MotivoEstorno" IS NULL OR m."MotivoEstorno" = '' THEN '' ELSE ' | ' END ||
                                      'auto: duplicata de abertura no mesmo dia (mantida a primeira)'
                FROM ranked r
                WHERE r."Id" = m."Id" AND r.rn > 1;
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS ix_movimentos_caixa_abertura_unica
                ON movimentos_caixa(
                    "EmpresaId",
                    COALESCE("LojaId", '00000000-0000-0000-0000-000000000000'::uuid),
                    DATE("DataMovimento")
                )
                WHERE "Tipo" = 'abertura' AND "EstornadoEm" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_movimentos_caixa_abertura_unica;");
        }
    }
}
