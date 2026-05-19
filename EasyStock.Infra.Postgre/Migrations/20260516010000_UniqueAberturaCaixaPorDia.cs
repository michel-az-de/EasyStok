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
            // extract(epoch from timestamptz) é STABLE no PostgreSQL (não IMMUTABLE),
            // então não pode ser usado diretamente em expressão de índice.
            // Criamos uma função wrapper marcada como IMMUTABLE — padrão aceito para
            // timestamptz quando o valor retornado é determinístico (epoch UTC não
            // depende de configuração de sessão).
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION public.caixa_abertura_utc_day(ts timestamptz)
                RETURNS bigint LANGUAGE sql IMMUTABLE PARALLEL SAFE STRICT
                AS $$ SELECT date_part('epoch', $1)::bigint / 86400 $$;
                """);

            // Sanity: estorna aberturas duplicadas pré-existentes mantendo a primeira de cada dia/empresa/loja.
            migrationBuilder.Sql("""
                WITH ranked AS (
                    SELECT "Id",
                           ROW_NUMBER() OVER (
                               PARTITION BY "EmpresaId", COALESCE("LojaId", '00000000-0000-0000-0000-000000000000'::uuid), public.caixa_abertura_utc_day("DataMovimento")
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
                    public.caixa_abertura_utc_day("DataMovimento")
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
