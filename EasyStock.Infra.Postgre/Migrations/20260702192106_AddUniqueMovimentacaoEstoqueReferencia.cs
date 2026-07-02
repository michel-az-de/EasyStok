using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueMovimentacaoEstoqueReferencia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // FAIL-LOUD (#790): movimento de estoque e historico financeiro — NAO deletamos
            // duplicatas as cegas (alteraria saldo). Se prod ja tem entradas duplicadas do
            // bug de recebimento, a migration ABORTA com a contagem e o Felipe investiga
            // caso a caso antes de reaplicar. Banco novo (from-zero) nao tem dups: segue.
            migrationBuilder.Sql("""
                DO $$
                DECLARE dup int;
                BEGIN
                    SELECT COUNT(*) INTO dup FROM (
                        SELECT 1 FROM movimentacoes_estoque
                        WHERE "DocumentoReferencia" IS NOT NULL
                        GROUP BY "EmpresaId", "ProdutoId", "Natureza", "DocumentoReferencia"
                        HAVING COUNT(*) > 1
                    ) d;
                    IF dup > 0 THEN
                        RAISE EXCEPTION 'C1/#790: % grupo(s) de movimentacoes_estoque duplicados por (EmpresaId, ProdutoId, Natureza, DocumentoReferencia). NAO aplicar antes de resolver (nao deletar movimento as cegas — altera saldo). Investigar: SELECT "EmpresaId","ProdutoId","Natureza","DocumentoReferencia",COUNT(*) FROM movimentacoes_estoque WHERE "DocumentoReferencia" IS NOT NULL GROUP BY 1,2,3,4 HAVING COUNT(*)>1;', dup;
                    END IF;
                END $$;
                """);

            // Idempotente (ADR-0024 §3).
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS ux_mov_estoque_referencia
                  ON movimentacoes_estoque ("EmpresaId", "ProdutoId", "Natureza", "DocumentoReferencia")
                  WHERE "DocumentoReferencia" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_mov_estoque_referencia;");
        }
    }
}
