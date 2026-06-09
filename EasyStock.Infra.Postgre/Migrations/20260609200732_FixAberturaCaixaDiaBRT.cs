using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class FixAberturaCaixaDiaBRT : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // #379 — fuso do caixa. A unique de abertura agrupava por DIA UTC
            // (caixa_abertura_utc_day = epoch/86400), divergindo do dia operacional BRT
            // (HorarioBrasil.DataOperacional, America/Sao_Paulo = UTC-3) usado por
            // AbrirCaixaUseCase e pela UI. Na janela 21:00-23:59 BRT (= 00:00-02:59 UTC do
            // dia seguinte) o dia UTC virava +1, causando "Já existe um registro" ao reabrir
            // e a UI mostrando "aguardando abertura" indevidamente.
            //
            // Fix: a função passa a computar o dia BRT deslocando UTC-3 fixo. Mantém-se
            // IMMUTABLE (exigência de índice; AT TIME ZONE seria STABLE). Premissa: Brasil
            // sem horário de verão desde 2019, então UTC-3 fixo == America/Sao_Paulo hoje
            // e alinha com DataOperacional. Mudar a função IMMUTABLE não reindexa sozinho,
            // por isso o índice é dropado e recriado.

            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_movimentos_caixa_abertura_unica;");

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION public.caixa_abertura_utc_day(ts timestamptz)
                RETURNS bigint LANGUAGE sql IMMUTABLE PARALLEL SAFE STRICT
                AS $$ SELECT date_part('epoch', $1 - interval '3 hours')::bigint / 86400 $$;
                """);

            // Sanity: estorna aberturas duplicadas no NOVO agrupamento (dia BRT), mantendo a
            // primeira de cada dia/empresa/loja. Defesa contra MigrationsFailFast: se algum
            // tenant tiver 2 aberturas que caem no mesmo dia BRT, o CREATE UNIQUE abaixo
            // falharia e derrubaria o app no startup.
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
                                      'auto: duplicata de abertura no mesmo dia BRT (fix fuso #379, mantida a primeira)'
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
            // Reverte a função para o dia UTC original e recria o índice.
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_movimentos_caixa_abertura_unica;");

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION public.caixa_abertura_utc_day(ts timestamptz)
                RETURNS bigint LANGUAGE sql IMMUTABLE PARALLEL SAFE STRICT
                AS $$ SELECT date_part('epoch', $1)::bigint / 86400 $$;
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
    }
}
