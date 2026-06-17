using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueFechamentoCaixaCoalescido : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // O unique modelado pelo EF (IX_fechamentos_caixa_EmpresaId_LojaId_Data) NAO dedupa
            // LojaId nulo (NULL != NULL no Postgres). Como o caixa do Web e operado empresa-level
            // (LojaId = null), uma corrida de duplo-fechamento poderia gravar 2 snapshots para
            // (empresa, loja-null, dia) (issue 640, finding 5b). Este indice coalescido fecha a
            // brecha tratando NULL como o uuid-zero, espelhando ix_movimentos_caixa_abertura_unica.

            // Sanitiza duplicatas pre-existentes (se houver): mantem o fechamento mais antigo
            // (FechadoEm) de cada (empresa, loja-coalescida, dia) e remove os demais. Necessario
            // antes de criar o indice unico; idempotente. Em prod normal e no-op (a idempotencia
            // do FecharCaixaUseCase ja impede duplicatas sequenciais).
            migrationBuilder.Sql("""
                WITH ranked AS (
                    SELECT "Id",
                           ROW_NUMBER() OVER (
                               PARTITION BY "EmpresaId", COALESCE("LojaId", '00000000-0000-0000-0000-000000000000'::uuid), "Data"
                               ORDER BY "FechadoEm"
                           ) AS rn
                    FROM fechamentos_caixa
                )
                DELETE FROM fechamentos_caixa f
                USING ranked r
                WHERE f."Id" = r."Id" AND r.rn > 1;
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS ix_fechamentos_caixa_dia_unica
                ON fechamentos_caixa(
                    "EmpresaId",
                    COALESCE("LojaId", '00000000-0000-0000-0000-000000000000'::uuid),
                    "Data"
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_fechamentos_caixa_dia_unica;");
        }
    }
}
