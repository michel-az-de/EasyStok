using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddUniquePlanoNomeInsensivel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // QA 2026-07-09 BUG-08 (#891): unicidade case-insensitive de planos.Nome — backstop de
            // banco para o check de aplicacao (AdminPlanosController -> ExisteNomeAsync), fechando a
            // janela TOCTOU entre o SELECT e o INSERT. Espelha AddUniqueCategoriaNomeInsensivelPorEmpresa
            // (#647): coluna gerada nome_lower + UNIQUE constraint. A coluna extra e invisivel ao
            // SchemaDriftCheck, que so acusa coluna DO MODELO ausente no banco — nao coluna a mais no
            // banco. Por isso PlanoConfiguration nao a declara e o snapshot nao muda.
            //
            // Planos sao globais (nao ha EmpresaId), entao a unicidade e sobre lower(Nome) apenas.
            //
            // O ambiente JA tem colisao: o QA criou um segundo "Starter" (bf0f885b) ao lado do
            // original (4f951c4f). Deduplicamos ANTES da constraint, senao a migration trava o deploy.
            // Mantem o mais antigo (CriadoEm, Id) e renomeia os demais com sufixo derivado do Id —
            // unico por definicao, entao a renomeacao NUNCA gera nova colisao. Nada e apagado: as
            // assinaturas preservam o PlanoId, e o operador renomeia depois pela UI.
            // left(...,60) + ' (dup ' + 8 + ')' = 75 chars, dentro do varchar(80) da coluna.
            migrationBuilder.Sql(@"
                ALTER TABLE planos
                    ADD COLUMN IF NOT EXISTS nome_lower text
                    GENERATED ALWAYS AS (lower(""Nome"")) STORED;

                UPDATE planos p
                SET ""Nome"" = left(p.""Nome"", 60) || ' (dup ' || left(p.""Id""::text, 8) || ')'
                FROM (
                    SELECT ""Id"", row_number() OVER (
                        PARTITION BY lower(""Nome"") ORDER BY ""CriadoEm"", ""Id""
                    ) AS rn
                    FROM planos
                ) r
                WHERE p.""Id"" = r.""Id"" AND r.rn > 1;

                ALTER TABLE planos
                    DROP CONSTRAINT IF EXISTS uq_planos_nome_lower;

                ALTER TABLE planos
                    ADD CONSTRAINT uq_planos_nome_lower UNIQUE (nome_lower);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nao desfaz a deduplicacao: o nome antigo colidiria de novo e nao ha registro do original.
            migrationBuilder.Sql(@"
                ALTER TABLE planos
                    DROP CONSTRAINT IF EXISTS uq_planos_nome_lower;

                ALTER TABLE planos
                    DROP COLUMN IF EXISTS nome_lower;");
        }
    }
}
